using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace FlakyDetective.McpServer;

[McpServerToolType]
public class RunTestsTool
{
    [McpServerTool(Name = "run_test_n_times")]
    [Description("Run a batch of dotnet tests and produce a summary result focusing on failure rates. Prerequisite: build the sandbox first. testFilter is an optional parameter to match partially fully qualified name. n is the number of times to run the tests.")]
    public static async Task<BatchRunResult> RunTests(int n, string? testFilter = null)
    {
        if (n <= 0 || n > 100)
        {
            return new BatchRunResult
            (
                Requested: n,
                Completed: 0,
                Error: "n must be a positive integer between 1 and 100",
                TestFilter: testFilter,
                Runs: new List<RunResult>(),
                TestSummaries: new List<TestSummary>()
            );
        }
        return await TestRunner.RunBatch(n, testFilter);
    }   
}
