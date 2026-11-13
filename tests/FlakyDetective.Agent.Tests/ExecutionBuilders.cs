using System.Text.Json;
using FlakyDetective.Agent;

namespace FlakyDetective.Agent.Tests;

// Builds the ToolExecution records the loop would have kept, so tests need no model and no server.
internal static class ExecutionBuilders
{
    public const string Target = "Reset_clears_cache";

    public static ToolExecution Edit(object? apply, bool failed = false)
    {
        var input = new Dictionary<string, object?>
        {
            ["path"] = "sandbox/FlakyLab/FlakyLabTests.cs", ["oldText"] = "a", ["newText"] = "b", ["apply"] = apply
        };
        return EditWith(input, failed);
    }

    public static ToolExecution EditWithoutApply() =>
        EditWith(new Dictionary<string, object?>
        {
            ["path"] = "sandbox/FlakyLab/FlakyLabTests.cs", ["oldText"] = "a", ["newText"] = "b"
        }, false);

    private static ToolExecution EditWith(Dictionary<string, object?> input, bool failed)
    {
        var result = failed
            ? "{\"path\":\"sandbox/FlakyLab/FlakyLabTests.cs\",\"applied\":false,\"diff\":\"\",\"error\":\"oldText not found in file\"}"
            : "{\"path\":\"sandbox/FlakyLab/FlakyLabTests.cs\",\"applied\":true,\"diff\":\"line 1\"}";
        return new ToolExecution("edit_source", input, result, false);
    }

    // The target test run on its own: n runs, `failed` of them failed.
    public static ToolExecution Alone(int n, int failed, string test = Target) =>
        Run(test, n, new Dictionary<string, int> { [test] = failed });

    // The whole suite: the target plus another test that may be failing too.
    public static ToolExecution Suite(int n, int targetFailed, int otherFailed = 0) =>
        Run(null, n, new Dictionary<string, int> { [Target] = targetFailed, ["Writer_sets_eurusd"] = otherFailed });

    private static ToolExecution Run(string? filter, int n, Dictionary<string, int> failedByTest)
    {
        var summaries = string.Join(",", failedByTest.Select(kv =>
            $"{{\"testName\":\"{kv.Key}\",\"runs\":{n},\"passed\":{n - kv.Value},\"failed\":{kv.Value},\"failureRate\":0}}"));
        var filterPart = filter == null ? "" : $"\"testFilter\":\"{filter}\",";
        var result = $"{{\"requested\":{n},\"completed\":{n},{filterPart}\"runs\":[],\"testSummaries\":[{summaries}]}}";
        var input = new Dictionary<string, object?> { ["n"] = n };
        if (filter != null) input["testFilter"] = filter;
        return new ToolExecution("run_test_n_times", input, result, false);
    }

    // What find_test_source returns: the file and line range of the test (one entry per match).
    public static ToolExecution Find(string test, string file = "sandbox/FlakyLab/FlakyLabTests.cs", int start = 83, int end = 89, int matches = 1, bool failed = false)
    {
        var entries = string.Join(",", Enumerable.Range(0, matches).Select(_ => $"{{\"file\":\"{file}\",\"startLine\":{start},\"endLine\":{end}}}"));
        var result = failed
            ? $"{{\"testName\":\"{test}\",\"matches\":[],\"error\":\"no test named {test}\"}}"
            : $"{{\"testName\":\"{test}\",\"matches\":[{entries}]}}";
        return new ToolExecution("find_test_source", new Dictionary<string, object?> { ["testName"] = test }, result, false);
    }

    public static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;
}
