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
