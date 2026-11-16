using System.Text.Json;
using System.Text.RegularExpressions;

namespace FlakyDetective.Agent;

// The hand-off from the investigation to the fix phase: builds the task text the fix run starts from.
//   - The file and line range come from what find_test_source really returned (the tool executions), never from the
//     model's words, so a made-up path cannot reach the fix phase.
//   - The suspected cause is the model's own statement, so it is passed on labelled as possibly wrong.
// Returns null when the location is not known for certain: the hand-off fails and no fix is attempted.
public static class FixTask
{
    private const int MaxCauseLength = 300;

    public static string? From(IReadOnlyList<ToolExecution> investigationCalls, string investigationAnswer, string testName)
    {
        var location = Location(investigationCalls, testName);
        if (location == null)
        {
            return null;
        }

        return $"Fix the test {testName}. Its source is in {location.Value.Path}, lines {location.Value.Start} to {location.Value.End}. " +
               $"The investigation suggested this cause, which may be wrong: {Cause(investigationAnswer)}";
    }

    // The latest find_test_source that worked and found exactly one place for this test. Two matches would be a
    // guess, so that counts as not found.
    internal static (string Path, int Start, int End)? Location(IReadOnlyList<ToolExecution> calls, string testName)
    {
        for (var i = calls.Count - 1; i >= 0; i--)
        {
            var call = calls[i];
            if (call.Name != "find_test_source" || !call.Succeeded)
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(call.Result);
                var root = document.RootElement;
                if (root.GetProperty("testName").GetString() != testName)
                {
                    continue;
                }

                var matches = root.GetProperty("matches");
                if (matches.GetArrayLength() != 1)
                {
                    return null;
                }

                var match = matches[0];
                return (match.GetProperty("file").GetString()!, match.GetProperty("startLine").GetInt32(), match.GetProperty("endLine").GetInt32());
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                continue;
            }
        }

        return null;
    }

    // The text after "Suspected cause:" on its line, or "unknown" when there is none.
    public static string Cause(string answer)
    {
        var match = Regex.Match(answer, @"(?im)^[\s*_#>\-]*Suspected cause[\s*_]*:[\s*_]*(.+)$");
        var cause = match.Success ? match.Groups[1].Value.Trim().Trim('*', '_').Trim() : "";

        if (cause.Length == 0 || cause.TrimEnd('.').Equals("unknown", StringComparison.OrdinalIgnoreCase))
        {
            return "unknown";
        }

        return cause.Length > MaxCauseLength ? cause[..MaxCauseLength] : cause;
    }
}
