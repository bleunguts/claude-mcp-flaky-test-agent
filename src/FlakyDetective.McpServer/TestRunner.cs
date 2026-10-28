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

    public static async Task<ProcessResult> RunTestsAsync(string workingFolder, string? testFilter = null)
    {        
        string trxFolder = Path.Combine(workingFolder, "TestResults", "mcp");       
        if (Directory.Exists(trxFolder))
        {
            Directory.Delete(trxFolder, true);
        }
        Directory.CreateDirectory(trxFolder);
        string trxFilePath = Path.Combine(trxFolder, "run1.trx");

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
        processStartInfo.ArgumentList.Add("trx;LogFileName=run1.trx");
        processStartInfo.ArgumentList.Add("--results-directory");
        processStartInfo.ArgumentList.Add(trxFolder);

        using var process = new Process { StartInfo = processStartInfo };
        process.Start();
        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Task.WaitAll(stdOutTask, stdErrTask);
        var stdErr = stdErrTask.Result;
        return new ProcessResult(process.ExitCode, stdErr, File.Exists(trxFilePath) ? trxFilePath : null);
    }
}