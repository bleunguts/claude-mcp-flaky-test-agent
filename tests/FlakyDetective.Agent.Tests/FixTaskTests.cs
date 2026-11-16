using FlakyDetective.Agent;
using NUnit.Framework;
using static FlakyDetective.Agent.Tests.ExecutionBuilders;

namespace FlakyDetective.Agent.Tests;

// FixTask.From builds the fix run's task from the investigation: the location from the tools, the cause from the answer.
public class FixTaskTests
{
    private const string Answer = "Test: Reset_clears_cache\nVerdict: flaky\nSuspected cause: something shared between tests\nConfidence: low";

    [Test]
    public void The_task_names_the_test_the_location_and_the_cause()
    {
        var task = FixTask.From([Find(Target, start: 83, end: 89)], Answer, Target);

        Assert.That(task, Does.Contain($"Fix the test {Target}")
            .And.Contain("sandbox/FlakyLab/FlakyLabTests.cs")
            .And.Contain("lines 83 to 89")
            .And.Contain("something shared between tests")
            .And.Contain("may be wrong"));
    }

    [Test]
    public void The_location_comes_from_the_tool_not_from_the_answer()
    {
        var answer = Answer + "\nEvidence: see src/Made/Up.cs lines 1 to 2";

        var task = FixTask.From([Find(Target, start: 83, end: 89)], answer, Target);

        Assert.That(task, Does.Not.Contain("Made/Up.cs"));
    }

    [Test]
    public void The_latest_successful_find_wins()
    {
        var task = FixTask.From([Find(Target, start: 10, end: 20), Find(Target, start: 83, end: 89)], Answer, Target);

        Assert.That(task, Does.Contain("lines 83 to 89"));
    }

    [Test]
    public void A_find_for_another_test_is_ignored()
    {
        var task = FixTask.From([Find("Mid_is_positive", start: 62, end: 67)], Answer, Target);

        Assert.That(task, Is.Null);
    }

    [Test]
    public void No_find_at_all_fails_the_hand_off()
    {
        Assert.That(FixTask.From([Suite(10, 4), Alone(10, 0)], Answer, Target), Is.Null);
    }

    [Test]
    public void A_find_the_tool_reported_as_failed_fails_the_hand_off()
    {
        Assert.That(FixTask.From([Find(Target, failed: true)], Answer, Target), Is.Null);
    }

    [Test]
    public void Two_matches_are_a_guess_so_the_hand_off_fails()
    {
        Assert.That(FixTask.From([Find(Target, matches: 2)], Answer, Target), Is.Null);
    }

    [Test]
    public void A_failed_later_find_does_not_hide_an_earlier_good_one()
    {
        var task = FixTask.From([Find(Target, start: 83, end: 89), Find(Target, failed: true)], Answer, Target);

        Assert.That(task, Does.Contain("lines 83 to 89"));
    }

    // ---- the cause line ----

    [TestCase("Suspected cause: a race in setup", "a race in setup")]
    [TestCase("**Suspected cause:** a race in setup", "a race in setup")]
    [TestCase("- Suspected cause: a race in setup", "a race in setup")]
    [TestCase("suspected cause:   padded   ", "padded")]
    public void The_cause_is_read_from_its_line(string line, string expected)
    {
        Assert.That(FixTask.Cause("Test: X\nVerdict: flaky\n" + line + "\nConfidence: low"), Is.EqualTo(expected));
    }

    [TestCase("Test: X\nVerdict: flaky\nConfidence: low")]
    [TestCase("Suspected cause: unknown")]
    [TestCase("Suspected cause: Unknown.")]
    [TestCase("Suspected cause:")]
    [TestCase("")]
    public void A_missing_or_unknown_cause_is_unknown(string answer)
    {
        Assert.That(FixTask.Cause(answer), Is.EqualTo("unknown"));
    }

    [Test]
    public void Only_the_cause_line_is_taken_not_the_lines_after_it()
    {
        var cause = FixTask.Cause("Suspected cause: first line\nEvidence: second line");

        Assert.That(cause, Is.EqualTo("first line"));
    }

    [Test]
    public void A_very_long_cause_is_cut()
    {
        var cause = FixTask.Cause("Suspected cause: " + new string('x', 1000));

        Assert.That(cause.Length, Is.EqualTo(300));
    }
}
