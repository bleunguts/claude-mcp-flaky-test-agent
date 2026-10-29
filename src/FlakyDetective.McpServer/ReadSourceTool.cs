using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Security;
using System.Text;

namespace FlakyDetective.McpServer;

[McpServerToolType]
public class ReadSourceTool
{
    private const int MaxLines = 300;

    [McpServerTool(Name = "read_source")]
    [Description("Reads a source file from the solution and returns its lines prefixed with line numbers. " +
        "Paths are relative to the solution root, for example 'sandbox/FlakyLab/FlakyLabTests.cs'. " +
        "Only text and source files can be read; build output, .git, .claude, .env and similar files are refused. " +
        "At most 300 lines are returned per call: if Truncated is true, call again with startLine = EndLine + 1. " +
        "Failures are reported in the Error field.")]
    public static ReadSourceResult ReadSource(
        [Description("File path relative to the solution root, for example 'sandbox/FlakyLab/FlakyLabTests.cs'.")] string path,
        [Description("First line to return (1-based). Defaults to 1.")] int? startLine = null,
        [Description("Last line to return, inclusive. Defaults to the end of the file, limited to 300 lines per call.")] int? endLine = null)
    {
        var shownPath = path.Replace('\\', '/');

        if (startLine is < 1 || endLine is < 1 || (startLine.HasValue && endLine.HasValue && startLine > endLine))
            return Fail(shownPath, "invalid line range");

        var root = TestRunner.FindSlnFolderRecursively(AppContext.BaseDirectory);

        if (!PathGuard.TryResolve(root, path, out var fullPath, out var error))
            return Fail(shownPath, error);

        if (!File.Exists(fullPath))
            return Fail(shownPath, "file not found");

        var relativePath = Path.GetRelativePath(root, fullPath).Replace('\\', '/');

        var allLines = File.ReadAllLines(fullPath);
        var totalLines = allLines.Length;
        if (totalLines == 0)
        {
            return new ReadSourceResult(relativePath, "", 0, 0, 0, false, null);
        }

        var start = startLine ?? 1;
        if (start > totalLines)
            return Fail(relativePath, $"file has {totalLines} lines");

        var end = Math.Min(endLine ?? totalLines, totalLines);
        var truncated = false;
        if (end - start + 1 > MaxLines)
        {
            end = start + MaxLines - 1;
            truncated = true;
        }

        var content = string.Join("\n", allLines[(start - 1)..end].Select((line, i) => $"{start + i}: {line}"));
        return new ReadSourceResult(relativePath, content, start, end, totalLines, truncated, null);
    }

    private static ReadSourceResult Fail(string path, string error)
    {
        return new ReadSourceResult(
            path,
            string.Empty,
            0,
            0,
            0,
            false,
            error
        );
    }
}
