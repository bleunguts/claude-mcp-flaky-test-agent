using FlakyDetective.McpServer;
using NUnit.Framework;

namespace FlakyDetective.McpServer.Tests;

// Runs EditSourceTool.Edit against a temporary folder laid out like the solution, so the real sandbox is never touched.
public class EditSourceToolTests
{
    private const string Target = "sandbox/FlakyLab/Sample.cs";
    private const string Original = "line one\nline two\nline three\nline four\n";

    private string _root = "";

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "edit-source-tests-" + Guid.NewGuid().ToString("N"));
        Write(Target, Original);
        Write("src/Outside.cs", Original);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Test]
    public void Dry_run_reports_the_change_and_writes_nothing()
    {
        var result = EditSourceTool.Edit(_root, Target, "line two", "line 2", apply: false);

        Assert.That(result.Error, Is.Null);
        Assert.That(result.Applied, Is.False);
        Assert.That(result.Diff, Does.Contain("line 2").And.Contain("- line two").And.Contain("+ line 2"));
        Assert.That(Read(Target), Is.EqualTo(Original));
    }

    [Test]
    public void Apply_changes_only_the_matched_text_and_keeps_the_rest_of_the_file()
    {
        var result = EditSourceTool.Edit(_root, Target, "line two", "line 2", apply: true);

        Assert.That(result.Error, Is.Null);
        Assert.That(result.Applied, Is.True);
        Assert.That(Read(Target), Is.EqualTo("line one\nline 2\nline three\nline four\n"));
    }

    [Test]
    public void Apply_works_at_the_start_and_at_the_very_end_of_the_file()
    {
        Write(Target, "line one\r\nline two\r\nline three\r\nline four\r\n");

        EditSourceTool.Edit(_root, Target, "line one", "FIRST", apply: true);
        EditSourceTool.Edit(_root, Target, "line four\n", "LAST", apply: true);

        Assert.That(Read(Target), Is.EqualTo("FIRST\r\nline two\r\nline three\r\nLAST"));
    }

    [Test]
    public void Apply_can_delete_text_with_an_empty_replacement()
    {
        Write(Target, "line one\r\nline two\r\nline three\r\nline four\r\n");

        EditSourceTool.Edit(_root, Target, "line two\n", "", apply: true);

        Assert.That(Read(Target), Is.EqualTo("line one\r\nline three\r\nline four\r\n"));
    }

    [Test]
    public void Apply_keeps_crlf_line_endings_elsewhere_in_the_file()
    {
        Write(Target, "one\r\ntwo\r\nthree\r\n");

        EditSourceTool.Edit(_root, Target, "two", "2", apply: true);

        Assert.That(Read(Target), Is.EqualTo("one\r\n2\r\nthree\r\n"));
    }

    [Test]
    public void The_diff_names_the_first_changed_line()
    {
        var nl = Environment.NewLine;
        Write(Target, $"line one{nl}line two{nl}line three{nl}line four{nl}");

        var result = EditSourceTool.Edit(_root, Target, "line three", "line 3", apply: false);

        Assert.That(result.Diff, Does.StartWith("line 3"));
    }

    // Windows only: lines are separated by Environment.NewLine, in the file and in the text the model sends.
    [Test]
    public void The_diff_of_a_multi_line_change_has_one_row_per_line()
    {
        var nl = Environment.NewLine;
        Write(Target, $"line one{nl}line two{nl}line three{nl}line four{nl}");

        var result = EditSourceTool.Edit(_root, Target, $"line two{nl}line three", $"A{nl}B{nl}C", apply: false);

        var rows = result.Diff.Split(nl, StringSplitOptions.RemoveEmptyEntries);
        Assert.That(rows, Is.EqualTo(new[] { "line 2", "- line two", "- line three", "+ A", "+ B", "+ C" }));
    }

    // The model sends line breaks as \n; the files on Windows use \r\n. The edit must still match and must not
    // leave a mix of line endings behind.
    [Test]
    public void Multi_line_text_with_lf_matches_a_crlf_file_and_keeps_crlf()
    {
        Write(Target, "one\r\ntwo\r\nthree\r\nfour\r\n");

        var result = EditSourceTool.Edit(_root, Target, "two\nthree", "A\nB\nC", apply: true);

        Assert.That(result.Error, Is.Null);
        Assert.That(Read(Target), Is.EqualTo("one\r\nA\r\nB\r\nC\r\nfour\r\n"));
    }

    [Test]
    public void Multi_line_text_that_already_uses_crlf_is_not_doubled()
    {
        Write(Target, "one\r\ntwo\r\nthree\r\n");

        EditSourceTool.Edit(_root, Target, "two\r\nthree", "X\r\nY", apply: true);

        Assert.That(Read(Target), Is.EqualTo("one\r\nX\r\nY\r\n"));
    }

    [Test]
    public void Refuses_text_that_is_not_in_the_file()
    {
        var result = EditSourceTool.Edit(_root, Target, "no such text", "x", apply: true);

        Assert.That(result.Error, Does.Contain("not found"));
        Assert.That(result.Applied, Is.False);
        Assert.That(Read(Target), Is.EqualTo(Original));
    }

    [Test]
    public void Refuses_text_that_appears_more_than_once()
    {
        Write(Target, "same\nsame\n");

        var result = EditSourceTool.Edit(_root, Target, "same", "x", apply: true);

        Assert.That(result.Error, Does.Contain("multiple"));
        Assert.That(Read(Target), Is.EqualTo("same\nsame\n"));
    }

    [Test]
    public void Refuses_empty_old_text()
    {
        var result = EditSourceTool.Edit(_root, Target, "", "x", apply: true);

        Assert.That(result.Error, Does.Contain("empty"));
        Assert.That(Read(Target), Is.EqualTo(Original));
    }

    [Test]
    public void Refuses_text_over_the_size_cap()
    {
        var result = EditSourceTool.Edit(_root, Target, "line two", new string('x', 2001), apply: true);
        Assert.That(result.Error, Does.Contain("maximum"));

        result = EditSourceTool.Edit(_root, Target, new string('x', 2001), "y", apply: true);
        Assert.That(result.Error, Does.Contain("maximum"));

        Assert.That(Read(Target), Is.EqualTo(Original));
    }

    [Test]
    public void Refuses_a_file_that_does_not_exist()
    {
        var result = EditSourceTool.Edit(_root, "sandbox/FlakyLab/Missing.cs", "a", "b", apply: true);

        Assert.That(result.Error, Does.Contain("not found"));
        Assert.That(File.Exists(Path.Combine(_root, "sandbox", "FlakyLab", "Missing.cs")), Is.False, "a refused edit must not create the file");
    }

    [TestCase("src/Outside.cs")]
    [TestCase("sandbox/../src/Outside.cs")]
    [TestCase("../Escape.cs")]
    public void Refuses_files_outside_the_sandbox_and_leaves_them_unchanged(string path)
    {
        var result = EditSourceTool.Edit(_root, path, "line two", "HACKED", apply: true);

        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Applied, Is.False);
        Assert.That(Read("src/Outside.cs"), Is.EqualTo(Original));
    }

    [Test]
    public void Refuses_files_that_are_not_cs_even_inside_the_sandbox()
    {
        Write("sandbox/FlakyLab/FlakyLab.csproj", "<Project />");

        var result = EditSourceTool.Edit(_root, "sandbox/FlakyLab/FlakyLab.csproj", "Project", "Hacked", apply: true);

        Assert.That(result.Error, Is.Not.Null);
        Assert.That(Read("sandbox/FlakyLab/FlakyLab.csproj"), Is.EqualTo("<Project />"));
    }

    private void Write(string relative, string content)
    {
        var full = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private string Read(string relative) => File.ReadAllText(Path.Combine(_root, relative));
}
