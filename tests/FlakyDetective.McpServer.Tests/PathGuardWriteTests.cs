using FlakyDetective.McpServer;
using NUnit.Framework;

namespace FlakyDetective.McpServer.Tests;

// The write boundary: the agent may change .cs files under sandbox/ and nothing else.
// PathGuard only works out paths, so no files are touched; the root just has to be a plausible folder.
public class PathGuardWriteTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "flakylab-root");

    [TestCase("sandbox/FlakyLab/FlakyLabTests.cs")]
    [TestCase(@"sandbox\FlakyLab\FlakyLabTests.cs")]
    [TestCase("SANDBOX/FlakyLab/FlakyLabTests.cs")]
    [TestCase("sandbox/FlakyLab/../FlakyLab/FlakyLabTests.cs")]
    public void Allows_cs_files_under_sandbox(string path)
    {
        var ok = PathGuard.TryResolveForWrite(Root, path, out var fullPath, out var error);

        Assert.That(ok, Is.True, error);
        Assert.That(fullPath, Does.StartWith(Path.Combine(Path.GetFullPath(Root), "sandbox")).IgnoreCase);
    }

    [TestCase("src/FlakyDetective.Agent/Program.cs", Description = "outside sandbox")]
    [TestCase("sandbox/../src/FlakyDetective.Agent/Program.cs", Description = ".. climbs out of sandbox")]
    [TestCase("src/sandbox/x.cs", Description = "sandbox is not the first folder")]
    [TestCase("sandboxEvil/x.cs", Description = "lookalike folder name")]
    [TestCase("sandbox.cs", Description = "file whose name starts with sandbox")]
    [TestCase("../outside.cs", Description = "outside the solution root")]
    [TestCase("sandbox/../../outside.cs", Description = ".. climbs out of the root")]
    public void Refuses_paths_outside_sandbox(string path)
    {
        AssertRefused(path);
    }

    [TestCase(".git/config")]
    [TestCase("sandbox/.env")]
    [TestCase("sandbox/GROUND_TRUTH.cs")]
    [TestCase("sandbox/FlakyLab/bin/x.cs")]
    [TestCase("sandbox/FlakyLab/obj/x.cs")]
    [TestCase("sandbox/TestResults/x.cs")]
    public void Refuses_denied_files_and_folders_even_inside_sandbox(string path)
    {
        AssertRefused(path);
    }

    [TestCase("sandbox/FlakyLab/FlakyLab.csproj")]
    [TestCase("sandbox/FlakyLab/notes.md")]
    [TestCase("sandbox/FlakyLab/data.json")]
    [TestCase("sandbox/FlakyLab/run.ps1")]
    [TestCase("sandbox/FlakyLab/Makefile")]
    public void Refuses_files_that_are_not_cs(string path)
    {
        AssertRefused(path);
    }

    [Test]
    public void Refuses_absolute_paths_even_inside_sandbox()
    {
        var absoluteInsideSandbox = Path.Combine(Path.GetFullPath(Root), "sandbox", "FlakyLab", "FlakyLabTests.cs");

        AssertRefused(absoluteInsideSandbox);
        AssertRefused(Path.Combine(Path.GetPathRoot(Path.GetFullPath(Root))!, "Windows", "x.cs"));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("sandbox")]
    [TestCase("sandbox/")]
    [TestCase("sandbox/FlakyLab")]
    public void Refuses_empty_paths_and_folders(string path)
    {
        AssertRefused(path);
    }

    private static void AssertRefused(string path)
    {
        var ok = PathGuard.TryResolveForWrite(Root, path, out var fullPath, out var error);

        Assert.That(ok, Is.False, $"'{path}' should have been refused");
        Assert.That(fullPath, Is.Empty, "a refused path must not leave a usable full path behind");
        Assert.That(error, Is.Not.Null.And.Not.Empty);
    }
}
