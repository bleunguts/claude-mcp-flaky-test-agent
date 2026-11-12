using System.Text.Json;
using System.Text.RegularExpressions;

namespace FlakyDetective.Agent;

// One tool call the loop really ran (skipped calls are not included), with what came back.
public record ToolExecution(string Name, IReadOnlyDictionary<string, object?> Input, string Result, bool IsError)
{
    // A call counts as done only if it worked. Our tools report their own failures inside a normal result
    // ("error": "file not found", "no test named ..."), so IsError alone is not enough. Plain-text results such
    // as "pong" are not JSON and are treated as fine.
    public bool Succeeded => !IsError && !ReportsError(Result);

    private static bool ReportsError(string result)
    {
        try
        {
            using var document = JsonDocument.Parse(result);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("error", out var error)
                   && error.ValueKind == JsonValueKind.String
                   && !string.IsNullOrEmpty(error.GetString());
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

// The investigation method written down as data, so code can check it instead of hoping the model follows it.
public static class InvestigationMethod
{
    // Guardrail 2. Returns a sentence naming the first step that is not done yet, or null when the method is
    // complete. The loop uses it to refuse an answer that comes too early. A step is done only when a call
    // to that tool worked: a read_source that came back "file not found" does not count, and the nudge says
    // so, so a guessed path cannot satisfy the method.
    public static string? NextMissingStep(IReadOnlyList<ToolExecution> calls) =>
        Step(calls, "run_test_n_times", c => !HasFilter(c),
            "First run the whole suite: call run_test_n_times with n=10 and no testFilter.",
            "Your run of the whole suite failed. Call run_test_n_times again with n=10 and no testFilter.")
        ?? Step(calls, "run_test_n_times", HasFilter,
            "Next run the single test: call run_test_n_times with n=10 and testFilter set to the test name.",
            "Your run of the single test failed. Call run_test_n_times again with n=10 and testFilter set to the test name only.")
        ?? Step(calls, "find_test_source", _ => true,
            "Next call find_test_source with the test name.",
            "Your find_test_source call failed. Call it again with the exact test name.")
        ?? Step(calls, "read_source", _ => true,
            $"Next call read_source with {ReadArguments(calls)}.",
            $"Your read_source call failed. Call it again with {ReadArguments(calls)}. Copy the numbers digit for digit and do not guess a path.");

    // The arguments for read_source, taken from the latest find_test_source that worked, so the nudge can quote
    // them. A retry that only says "use what find_test_source returned" was ignored: a model copied endLine 89
    // as 9 three times. These are values a tool returned, not an answer, so nothing is given away.
    private static string ReadArguments(IReadOnlyList<ToolExecution> calls)
    {
        const string fallback = "exactly the file and line range that find_test_source returned";

        var find = calls.LastOrDefault(c => c.Name == "find_test_source" && c.Succeeded);
        if (find == null)
        {
            return fallback;
        }

        try
        {
            using var document = JsonDocument.Parse(find.Result);
            var match = document.RootElement.GetProperty("matches")[0];
            return $"path \"{match.GetProperty("file").GetString()}\", startLine {match.GetProperty("startLine").GetInt32()}, endLine {match.GetProperty("endLine").GetInt32()}";
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            return fallback;
        }
    }

    // Guardrail 2, the answer itself. The prompt asks for a fixed format and the scorecard depends on it. Some
    // runs ended with a sentence like "I will run it again", which is not an answer. Returns null when the
    // answer has a Verdict line, otherwise a sentence for the nudge.
    public static string? AnswerProblem(string answer) =>
        Regex.IsMatch(answer, @"(?im)^[\s*_#>\-]*Verdict[\s*_]*:[\s*_]*(flaky|stable|unknown)\b")
            ? null
            : "Your answer must follow the format and include a line 'Verdict: flaky', 'Verdict: stable' or 'Verdict: unknown'.";

    // null when a call to the tool that matches has worked; the first-time text when there was no such call;
    // the retry text when there were calls but none worked.
    private static string? Step(
        IReadOnlyList<ToolExecution> calls, string tool, Func<ToolExecution, bool> matches, string firstTime, string retry)
    {
        var candidates = calls.Where(c => c.Name == tool && matches(c)).ToList();
        if (candidates.Any(c => c.Succeeded))
        {
            return null;
        }

        return candidates.Count == 0 ? firstTime : retry;
    }

    private static bool HasFilter(ToolExecution call) =>
        call.Input.TryGetValue("testFilter", out var filter) && !string.IsNullOrWhiteSpace(filter?.ToString());
}

public static class Grounding
{
    // Guardrail 3, report only. A small model will write evidence it never received: it cites a tool it did
    // not call, or "quotes" a result that does not exist. The loop knows what really came back, so it checks.
    // Returns one sentence per problem; an empty list means nothing was found.
    public static List<string> Check(string answer, IEnumerable<string> toolNames, IReadOnlyList<ToolExecution> executions)
    {
        var problems = new List<string>();

        // 1. The answer mentions a tool that was never called.
        foreach (var tool in toolNames)
        {
            var mentioned = Regex.IsMatch(answer, $@"\b{Regex.Escape(tool)}\b");
            if (mentioned && !executions.Any(e => e.Name == tool))
            {
                problems.Add($"the answer cites {tool}, which was never called");
            }
        }

        // 2. An evidence line of the form "- tool_name: {json}" must appear in a real result of that tool.
        foreach (var line in answer.Split('\n'))
        {
            var match = Regex.Match(line.Trim(), @"^-\s*(\w+):\s*(\{.*)$");
            if (!match.Success)
            {
                continue;
            }

            var tool = match.Groups[1].Value;
            var quote = RemoveWhitespace(match.Groups[2].Value);
            var results = executions.Where(e => e.Name == tool).Select(e => RemoveWhitespace(e.Result)).ToList();

            if (results.Count > 0 && !results.Any(r => r.Contains(quote)))
            {
                problems.Add($"the evidence quoted for {tool} does not appear in any of its real results");
            }
        }

        // 3. A failed run listed in the answer must be a failed run that some tool really reported. A small model
        // will copy failed rows of other tests out of a whole-suite result and relabel them as the test it was asked
        // about, so the test name and the run number are both checked.
        var reported = executions
            .SelectMany(e => FailedRun.Matches(e.Result))
            .Select(m => (Test: m.Groups[1].Value, Run: m.Groups[2].Value))
            .ToHashSet();

        var invented = FailedRun.Matches(answer)
            .Select(m => (Test: m.Groups[1].Value, Run: m.Groups[2].Value))
            .Distinct()
            .Where(claim => !reported.Contains(claim))
            .GroupBy(claim => claim.Test);

        foreach (var group in invented)
        {
            problems.Add($"the answer lists failed run(s) of {group.Key} (run {string.Join(", ", group.Select(g => g.Run))}) that no tool reported");
        }

        return problems.Distinct().ToList();
    }

    // Matches the shape of a failed row in run_test_n_times results: "testName":"X","runNumber":N,"outcome":"Failed"
    private static readonly Regex FailedRun =
        new(@"""testName""\s*:\s*""([^""]+)""\s*,\s*""runNumber""\s*:\s*(\d+)\s*,\s*""outcome""\s*:\s*""Failed""");

    private static string RemoveWhitespace(string text) => Regex.Replace(text, @"\s+", "");
}

public static class Measured
{
    // What the tools really measured, read from their structured results and not from the model's wording.
    // The model understands "passes alone, fails in the suite" but swaps the numbers, so a person scoring a
    // run compares its Failure rate line with these lines.
    //   MEASURED Reset_clears_cache: alone 0/10 failed; in the suite 6/10 failed
    public static List<string> Lines(IReadOnlyList<ToolExecution> executions)
    {
        var lines = new List<string>();
        var batches = new List<Batch>();

        foreach (var execution in executions.Where(e => e.Name == "run_test_n_times" && !e.IsError))
        {
            var batch = Parse(execution.Result);
            if (batch == null)
            {
                lines.Add("MEASURED: a run_test_n_times result could not be read");
            }
            else if (batch.Error != null)
            {
                lines.Add($"MEASURED: run_test_n_times returned an error: {batch.Error}");
            }
            else
            {
                batches.Add(batch);
            }
        }

        // The latest whole-suite batch, and the latest batch for each test that was run on its own.
        var suite = batches.LastOrDefault(b => string.IsNullOrWhiteSpace(b.Filter));
        var targets = batches.Where(b => !string.IsNullOrWhiteSpace(b.Filter)).Select(b => b.Filter!.Trim()).Distinct().ToList();

        foreach (var target in targets)
        {
            var alone = batches.Last(b => b.Filter?.Trim() == target);
            var aloneSummary = alone.Summaries.FirstOrDefault(s => s.TestName == target)
                               ?? (alone.Summaries.Count == 1 ? alone.Summaries[0] : null);
            var suiteSummary = suite?.Summaries.FirstOrDefault(s => s.TestName == target);
            lines.Add($"MEASURED {target}: alone {Describe(aloneSummary)}; in the suite {Describe(suiteSummary)}");
        }

        if (targets.Count == 0 && suite != null)
        {
            lines.Add("MEASURED in the suite: " + string.Join(", ", suite.Summaries.Select(s => $"{s.TestName} {s.Failed}/{s.Runs}")));
        }

        return lines;
    }

    private static string Describe(TestCount? count) =>
        count == null ? "not measured" : $"{count.Failed}/{count.Runs} failed";

    private static Batch? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var error = root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String
                ? errorElement.GetString()
                : null;
            var filter = root.TryGetProperty("testFilter", out var filterElement) && filterElement.ValueKind == JsonValueKind.String
                ? filterElement.GetString()
                : null;

            var summaries = new List<TestCount>();
            if (root.TryGetProperty("testSummaries", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    summaries.Add(new TestCount(
                        item.GetProperty("testName").GetString() ?? "",
                        item.GetProperty("runs").GetInt32(),
                        item.GetProperty("failed").GetInt32()));
                }
            }

            return new Batch(filter, error, summaries);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private record Batch(string? Filter, string? Error, List<TestCount> Summaries);
    private record TestCount(string TestName, int Runs, int Failed);
}
