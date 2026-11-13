using System.Diagnostics;

namespace FlakyDetective.McpServer;

public class TestRunner
{
    public static string FindSanboxFolder(string folder)
    {
        var slnFolder = FindSlnFolderRecursively(folder);
        return Path.Combine(slnFolder, "sandbox", "FlakyLab");
    }

    public static string FindSlnFolderRecursively(string folder)
    {
        var currentFolder = Directory.GetParent(folder);
        var fileInfo = currentFolder.GetFiles("FlakyDetective.slnx", SearchOption.TopDirectoryOnly);
        if (fileInfo.Length > 0)
        {
            return currentFolder.FullName;
        }

        if (currentFolder.Parent == null)
        {
            throw new Exception("Could not find sln folder");
        }

        return FindSlnFolderRecursively(currentFolder.FullName);
    }

    public static async Task<BatchRunResult> RunBatch(int n, string? testFilter)
    {
        testFilter = string.IsNullOrWhiteSpace(testFilter) ? null : testFilter.Trim();
        string workingFolder = FindSlnFolderRecursively(AppContext.BaseDirectory);
        string trxFolder = Path.Combine(workingFolder, "TestResults", "mcp");
        
        if (Directory.Exists(trxFolder))
        {
            Directory.Delete(trxFolder, true);
        }
        Directory.CreateDirectory(trxFolder);

        // Build once before the runs. The runs use --no-build, so without this an edit to the source would never be
        // tested: every run would execute the dll built earlier. An up-to-date build costs a few seconds, once per call.
        var build = await BuildSandboxAsync(workingFolder);
        if (build.ExitCode != 0)
        {
            var errors = build.stdout.Split('\n').Where(l => l.Contains("error ")).Take(5).Select(l => l.Trim());
            return new BatchRunResult(
                Requested: n,
                Completed: 0,
                Error: "Build failed, no tests were run. " + string.Join(" ", errors),
                TestFilter: testFilter,
                Runs: [],
                TestSummaries: []);
        }

        int runsCompleted = 0;
        var allTrxResults = new List<RunResult>();  
        var processResults = new List<ProcessResult>(); 
        for (int runNumber = 1; runNumber <= n; runNumber++)
        {     
            var processResult = await RunTestsAsync(workingFolder, trxFolder, runNumber, testFilter);
            processResults.Add(processResult);
            string trxFilePath = Path.Combine(trxFolder, ToTrxFilename(runNumber));            
            var trxResults = TrxParser.ParseResults(trxFilePath, runNumber);
            runsCompleted++;
            allTrxResults.AddRange(trxResults);
        }

        if (!allTrxResults.Any())
        {
            var failedRuns = processResults.FirstOrDefault(r => r.PathToTrxFile == null); 
            
            if(failedRuns != null)
            {
                var reason = failedRuns.Stderr != null ? failedRuns.Stderr : failedRuns.stdout;
                return new BatchRunResult
                (
                    Requested: n,
                    Completed: runsCompleted,
                    Error: $"dotnet test produced no trx file exitCode:{failedRuns.ExitCode} , Reason: {reason}",
                    TestFilter: testFilter,
                    Runs: allTrxResults,
                    TestSummaries: new List<TestSummary>()
                );
            }
            else if (!string.IsNullOrEmpty(testFilter))
            {
                return new BatchRunResult
                (
                    Requested: n,
                    Completed: runsCompleted,
                    Error: $"The test filter '{testFilter}' did not result in any matching tests",
                    TestFilter: testFilter,
                    Runs: allTrxResults,
                    TestSummaries: new List<TestSummary>()
                );
            }
            else
            {
                return new BatchRunResult
               (
                   Requested: n,
                   Completed: runsCompleted,
                   Error: $"dotnet test ran but produced no results",
                   TestFilter: testFilter,
                   Runs: allTrxResults,
                   TestSummaries: new List<TestSummary>()
               );
            }
        }

        var summaries = new List<TestSummary>();
        allTrxResults.GroupBy<RunResult, string>(r => r.TestName).ToList().ForEach(g =>
        {
            var testName = g.Key;
            var runs = g.ToList();
            var outcomes = runs.Select(r => r.Outcome).ToList();
            var durations = runs.Select(r => r.DurationMs).ToList();
            var errors = runs.Where(r => r.Error != null).Select(r => r.Error).ToList();          
            summaries.Add(new TestSummary
            (
                TestName: testName,
                Runs: runs.Count,
                Passed: outcomes.Count(o => o == OutcomeType.Passed),
                Failed: outcomes.Count(o => o == OutcomeType.Failed),
                FailureRate: (double)outcomes.Count(o => o == OutcomeType.Failed) / runs.Count
            ));
        });

        // Return only the runs that did not pass. A passing row says nothing the per-test summary does
        // not already say, and in a whole-suite result it buries the failures: with a small model the
        // rows of different tests get mixed up. The summaries still count every run.
        var notPassedRuns = allTrxResults.Where(r => r.Outcome != OutcomeType.Passed).ToList();

        if(runsCompleted < n)
        {
            return new BatchRunResult
            (
                Requested: n,
                Completed: runsCompleted,
                Error: "Some runs failed to complete, the data is partial",
                TestFilter: testFilter,
                Runs: notPassedRuns,
                TestSummaries: summaries
            );
        }
        
        return new BatchRunResult
        (
            Requested: n,
            Completed: runsCompleted,
            Error: null,
            TestFilter: testFilter,
            Runs: notPassedRuns,
            TestSummaries: summaries
        );
    }

    private static string ToTrxFilename(int runNumber)
    {
        return $"run{runNumber}.trx";
    }

    public static async Task<ProcessResult> BuildSandboxAsync(string workingFolder)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingFolder
        };
        processStartInfo.ArgumentList.Add("build");
        processStartInfo.ArgumentList.Add("sandbox/FlakyLab");
        processStartInfo.ArgumentList.Add("--nologo");
        processStartInfo.ArgumentList.Add("-v");
        processStartInfo.ArgumentList.Add("q");

        using var process = new Process { StartInfo = processStartInfo };
        process.Start();
        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await stdOutTask, await stdErrTask, null);
    }

    public static async Task<ProcessResult> RunTestsAsync(string workingFolder, string trxFolder, int runNumber, string? testFilter)
    {
        string trxFileName = ToTrxFilename(runNumber);
        string trxFilePath = Path.Combine(trxFolder, trxFileName);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingFolder
        };
        processStartInfo.ArgumentList.Add("test");
        processStartInfo.ArgumentList.Add("sandbox/FlakyLab");
        processStartInfo.ArgumentList.Add("--no-build");
        processStartInfo.ArgumentList.Add("--nologo");
        if(testFilter != null)
        {
            processStartInfo.ArgumentList.Add("--filter");
            processStartInfo.ArgumentList.Add("FullyQualifiedName~" + testFilter);
        }
        processStartInfo.ArgumentList.Add("--logger");
        processStartInfo.ArgumentList.Add($"trx;LogFileName={trxFileName}");
        processStartInfo.ArgumentList.Add("--results-directory");
        processStartInfo.ArgumentList.Add(trxFolder);

        using var process = new Process { StartInfo = processStartInfo };
        process.Start();
        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string[] taskResults = await Task.WhenAll(stdOutTask, stdErrTask);        
        string stdOut = taskResults[0];
        string stdErr = taskResults[1]; 
        return new ProcessResult(process.ExitCode, stdOut, stdErr, File.Exists(trxFilePath) ? trxFilePath : null);
    }
}
