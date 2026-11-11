using System.Text.Json;
using System.Text.RegularExpressions;

namespace FlakyDetective.Agent;

// One tool call the loop really ran (skipped calls are not included), with what came back.
public record ToolExecution(string Name, IReadOnlyDictionary<string, object?> Input, string Result, bool IsError);

// The investigation method written down as data, so code can check it instead of hoping the model follows it.
public static class InvestigationMethod
{
    // Guardrail 2. Returns a sentence naming the first step that has not happened yet, or null when the
    // method is complete. The loop uses it to refuse an answer that comes too early.
    public static string? NextMissingStep(IReadOnlyList<ToolExecution> calls)
    {
        if (!calls.Any(c => c.Name == "run_test_n_times" && !HasFilter(c)))
        {
            return "First run the whole suite: call run_test_n_times with n=10 and no testFilter.";
        }

        if (!calls.Any(c => c.Name == "run_test_n_times" && HasFilter(c)))
        {
            return "Next run the single test: call run_test_n_times with n=10 and testFilter set to the test name.";
        }

        if (!calls.Any(c => c.Name == "find_test_source"))
        {
            return "Next call find_test_source with the test name.";
        }

        if (!calls.Any(c => c.Name == "read_source"))
        {
            return "Next call read_source with the file and line range that find_test_source returned.";
        }

        return null;
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

        return problems.Distinct().ToList();
    }

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
