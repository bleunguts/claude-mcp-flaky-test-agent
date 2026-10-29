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

    public static async Task<BatchRunResult> RunBatch(int n, string? testFilter = null)
    {
        string workingFolder = FindSlnFolderRecursively(AppContext.BaseDirectory);
        string trxFolder = Path.Combine(workingFolder, "TestResults", "mcp");
        
        if (Directory.Exists(trxFolder))
        {
            Directory.Delete(trxFolder, true);
        }
        Directory.CreateDirectory(trxFolder);

        int runsCompleted = 0;
        var allTrxResults = new List<RunResult>();  
        var processResults = new List<ProcessResult>(); 
        for (int runNumber = 1; runNumber <= n; runNumber++)
        {     
            var processResult = await RunTestsAsync(workingFolder, trxFolder, runNumber, testFilter);
            processResults.Add(processResult);
            string trxFilePath = Path.Combine(trxFolder, ToTrxFilename(runNumber));
            if (!File.Exists(trxFilePath))
            {
                continue;   
            }
            var trxResults = TrxParser.ParseResults(trxFilePath, runNumber);
            runsCompleted++;
            allTrxResults.AddRange(trxResults);
        }

        if (!allTrxResults.Any())
        {
            string? stderr = processResults.FirstOrDefault()?.Stderr;
            if (!string.IsNullOrEmpty(stderr))
            {
                return new BatchRunResult
                (
                    Requested: n,
                    Completed: runsCompleted,
                    Error: $"The runner failed to run, no TRX results were found , Reason: {stderr}",
                    TestFilter: testFilter,
                    Runs: allTrxResults,
                    TestSummaries: new List<TestSummary>()
                );
            }

            if(testFilter != null && testFilter != string.Empty)
            {
                return new BatchRunResult
                (
                    Requested: n,
                    Completed: runsCompleted,
                    Error: $"No TRX results were found, the test filter '{testFilter}' did not result in any matching tests",
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

        if(runsCompleted < n)
        {
            return new BatchRunResult
            (
                Requested: n,
                Completed: runsCompleted,
                Error: "Some runs failed to complete, the data is partial",
                TestFilter: testFilter,
                Runs: allTrxResults,
                TestSummaries: summaries
            );
        }
        
        return new BatchRunResult
        (
            Requested: n,
            Completed: runsCompleted,
            Error: null,
            TestFilter: testFilter,
            Runs: allTrxResults,
            TestSummaries: summaries
        );
    }

    private static string ToTrxFilename(int runNumber)
    {
        return $"run{runNumber}.trx";
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
