using System.ComponentModel;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

namespace FlakyDetective.McpServer;

[McpServerToolType]
public class TestLocator
{
    [McpServerTool(Name = "find_test_source")]
    [Description("Finds the source location of a test method by its exact name. Returns the file (relative to the solution root) " +
        "and the first and last line of the method, 1-based, with its attributes included. " +
        "Next, call read_source with that file and line range to read the method. " +
        "Only methods declared as void or Task are found. Failures are reported in the Error field.")]
    public static FindTestResult FindTest(
        [Description("The exact name of the test method to find, for example 'Reset_clears_cache'.")] string testName)
    {
        if (string.IsNullOrWhiteSpace(testName))
        {
            return Fail(testName, "testName cannot be null or empty");
        }

        try
        {
            var root = TestRunner.FindSlnFolderRecursively(AppContext.BaseDirectory);
            var declaration = new Regex($@"\b(void|Task(<[^>]+>)?)\s+{Regex.Escape(testName.Trim())}\s*\(");
            var matches = new List<TestLocation>();

            foreach (var absoluteFilePath in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(root, absoluteFilePath);
                if (!PathGuard.TryResolve(root, relativePath, out var fullPath, out _))
                {
                    continue;
                }

                var lines = File.ReadAllLines(fullPath);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (!declaration.IsMatch(lines[i]))
                    {
                        continue;
                    }

                    var start = FindStartIndex(lines, i);
                    var endLine = FindEndLine(lines, i);
                    if (endLine < 0)
                    {
                        // braces never balanced: report just the declaration line
                        endLine = i + 1;
                    }

                    matches.Add(new TestLocation(relativePath.Replace('\\', '/'), start + 1, endLine));
                }
            }

            return matches.Count == 0
                ? Fail(testName, $"no test named '{testName}' found")
                : new FindTestResult(testName, matches, null);
        }
        catch (Exception ex)
        {
            // details go to stderr, not into the result, so local paths do not leak
            Console.Error.WriteLine(ex);
            return Fail(testName, "search failed");
        }
    }

    // zero-based index of the first attribute line above the declaration, or the declaration itself
    private static int FindStartIndex(string[] lines, int declarationIndex)
    {
        var start = declarationIndex;
        while (start > 0 && lines[start - 1].TrimStart().StartsWith('['))
        {
            start--;
        }
        return start;
    }

    // 1-based line where the member ends, or -1 if it never ends
    private static int FindEndLine(string[] lines, int declarationIndex)
    {
        var depth = 0;
        var started = false;
        var expressionBodied = false;

        for (var line = declarationIndex; line < lines.Length; line++)
        {
            var text = lines[line];
            for (var j = 0; j < text.Length; j++)
            {
                var c = text[j];

                if (!started && !expressionBodied && c == '=' && j + 1 < text.Length && text[j + 1] == '>')
                {
                    expressionBodied = true;
                    j++;
                    continue;
                }

                if (expressionBodied)
                {
                    if (c == ';')
                    {
                        return line + 1;
                    }
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                    started = true;
                }
                else if (c == '}')
                {
                    depth--;
                    if (started && depth == 0)
                    {
                        return line + 1;
                    }
                }
            }
        }

        return -1;
    }

    private static FindTestResult Fail(string? testName, string error) =>
        new(testName ?? "", new List<TestLocation>(), error);
}
