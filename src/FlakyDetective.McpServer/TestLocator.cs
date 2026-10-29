using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace FlakyDetective.McpServer;

[McpServerToolType]
public class TestLocator
{
    [McpServerTool(Name = "find_test_source")]
    [Description("Finds the location of a test method in the source code.")]
    public static FindTestResult FindTest(        
        [Description("The name of the test method to find.")] string testName)
    {
        if(string.IsNullOrEmpty(testName))
        {
            return new FindTestResult(TestName: testName, Matches: new List<TestLocation>(), Error: "testName cannot be null or empty");
        }   

        try
        {
            var root = TestRunner.FindSlnFolderRecursively(AppContext.BaseDirectory);
            var matches = new List<TestLocation>();
            foreach (var absoluteFilePath in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(root, absoluteFilePath);
                if (PathGuard.TryResolve(root, relativePath, out var fullPath, out var error))
                {
                    var lines = File.ReadAllLines(relativePath);
                    var rex = new Regex($@"\b(void|Task(<[^>]+>)?)\s+{Regex.Escape(testName)}\s*\(", RegexOptions.None);                    
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (rex.IsMatch(lines[i]))
                        {
                            matches.Add(new TestLocation(File: relativePath, StartLine: i + 1, EndLine: i + 1));
                        }
                    }
                }                             
            }
            if(matches.Count == 0)
            {
                return new FindTestResult(TestName: testName, Matches: new List<TestLocation>(), Error: $"no test named '{testName}' found");
            }
            return new FindTestResult(TestName: testName, Matches: matches, Error: null);
        }
        catch (Exception ex)
        {
            return new FindTestResult(TestName: testName, Matches: new List<TestLocation>(), Error: $"search failed: {ex.Message}");
        }
    }
}
