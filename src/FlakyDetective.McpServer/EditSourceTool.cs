using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace FlakyDetective.McpServer;

[McpServerToolType]
public class EditSourceTool
{
    private const int MaxChars = 2000;

    [McpServerTool(Name = "edit_source")]
    [Description("Edits a source file under the flakyLabs project. Replaces exactly one occurrence, default dry-run, call again with apply=true, only .cs files under sandbox/, 2000-character cap")]
    public static EditSourceResult EditSource(
        [Description("File path relative to the solution root, for example 'sandbox/FlakyLab/FlakyLabTests.cs'.")] string path,    
        [Description("Old text to be replaced.")] string oldText,
        [Description("New text to replace with.")] string newText,
        [Description("Whether to apply the changes and edit the file.")] bool apply = false
    )
    {
        var root = TestRunner.FindSlnFolderRecursively(AppContext.BaseDirectory);
        return Edit(root, path, oldText, newText, apply);
    }

    // The tool's logic, with the root passed in so tests can run against a temporary folder
    // instead of the real sandbox.
    public static EditSourceResult Edit(string root, string path, string oldText, string newText, bool apply)
    {
        if (!PathGuard.TryResolveForWrite(root, path, out var fullPath, out var error))
        {
            return Fail(path, error);
        }

        if (string.IsNullOrEmpty(oldText))
        {
            return Fail(path, "oldText cannot be empty");
        }

        // The model sends line breaks as \n, the files use Environment.NewLine (Windows only).
        // Convert to \n first so an existing \r\n does not become \r\r\n.
        oldText = oldText.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        newText = newText.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);

        if (!File.Exists(fullPath))
        {
            return Fail(path, "file not found");
        }

        if (newText.Length > MaxChars || oldText.Length > MaxChars)
        {
            return Fail(path, $"Text exceeds maximum length of {MaxChars} characters");
        }

        var sourceCode = File.ReadAllText(fullPath);
        int firstIndex = sourceCode.IndexOf(oldText, 0, StringComparison.Ordinal);
        if (firstIndex == -1)
        {
            return Fail(path, "oldText not found in file");
        }
        int secondIndex = sourceCode.IndexOf(oldText, firstIndex + oldText.Length, StringComparison.Ordinal);
        if (secondIndex != -1)
        {
            return Fail(path, "oldText found multiple times in file, please provide more surrounding text");
        }
        // build the result 
        var newSource = sourceCode[..firstIndex] + newText + sourceCode[(firstIndex + oldText.Length)..];

        if (apply)
        {
            File.WriteAllText(fullPath, newSource);
        }

        StringBuilder diff = BuildDiff(oldText, newText, sourceCode, firstIndex);

        return new EditSourceResult(path, apply, diff.ToString(), null);
    }

    private static StringBuilder BuildDiff(string oldText, string newText, string sourceCode, int firstIndex)
    {        
        var diff = new StringBuilder();
        
        //var count = sourceCode.AsSpan(0, firstIndex).Count("\n");
        // linenumber of first change: count newlines before firstIndex                
        var matches = Regex.Matches(sourceCode.Substring(0, firstIndex), Environment.NewLine);
        int lineNumber = matches.Count + 1;

        diff.AppendLine($"line {lineNumber}");
        // prefix oldText lines with -
        foreach (var old in oldText.Split(Environment.NewLine))
        {
            diff.AppendLine($"- {old}");
        }
        // prefix newText lines with +
        foreach (var @new in newText.Split(Environment.NewLine))
        {
            diff.AppendLine($"+ {@new}");
        }

        return diff;
    }

    private static EditSourceResult Fail(string path, string? error)
    {
        return new EditSourceResult(
            path,  
            false,
            string.Empty,
            error
        );
    }
}
