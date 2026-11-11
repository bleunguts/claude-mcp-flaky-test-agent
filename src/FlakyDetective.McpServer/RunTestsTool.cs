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
    [Description("Runs the sandbox tests n times, one run after another, and reports how often each test failed. " +
        "Build the sandbox first. Leave testFilter empty to run the whole suite, " +
        "or set it to one test name to run only that test.")]
    public static async Task<BatchRunResult> RunTests(
          [Description("How many times to run the tests, from 1 to 100. Use 10 unless told otherwise.")] int n,
           [Description("Optional. Just the test method name, for example Reset_clears_cache. Do not add a namespace or class. Leave empty to run the whole suite.")] string? testFilter = null)
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
