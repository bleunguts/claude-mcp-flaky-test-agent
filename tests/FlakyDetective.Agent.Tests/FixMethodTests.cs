using FlakyDetective.Agent;
using NUnit.Framework;
using static FlakyDetective.Agent.Tests.ExecutionBuilders;

namespace FlakyDetective.Agent.Tests;

// FixMethod.NextMissingStep names the next step of the fix phase, or returns null when the method is complete.
public class FixMethodTests
{
    // ---- step 1 and 2: the edit ----

    [Test]
    public void With_no_calls_it_asks_for_a_dry_run_first()
    {
        var next = FixMethod.NextMissingStep([], Target);

        Assert.That(next, Does.Contain("edit_source").And.Contain("apply=false"));
    }

    [Test]
    public void Investigation_calls_alone_still_ask_for_a_dry_run()
    {
        var next = FixMethod.NextMissingStep([Suite(10, 4), Alone(10, 0)], Target);

        Assert.That(next, Does.Contain("apply=false"));
    }

    [Test]
    public void A_dry_run_edit_the_tool_refused_is_retried_as_a_dry_run()
    {
        var next = FixMethod.NextMissingStep([Edit(false, failed: true)], Target);

        Assert.That(next, Does.Contain("failed").And.Contain("apply=false"));
    }

    [Test]
    public void After_a_dry_run_it_asks_for_the_applied_edit_and_names_the_file()
    {
        var next = FixMethod.NextMissingStep([Edit(false)], Target);

        Assert.That(next, Does.Contain("apply=true").And.Contain("sandbox/FlakyLab/FlakyLabTests.cs"));
    }

    [Test]
    public void An_edit_without_the_apply_argument_counts_as_a_dry_run()
    {
        var next = FixMethod.NextMissingStep([EditWithoutApply()], Target);

        Assert.That(next, Does.Contain("apply=true"));
    }

    [Test]
    public void An_applied_edit_the_tool_refused_does_not_count()
    {
        var next = FixMethod.NextMissingStep([Edit(false), Edit(true, failed: true)], Target);

        Assert.That(next, Does.Contain("apply=true"));
    }

    // The model may apply straight away. The dry run cannot be made up afterwards, so the method moves on.
    [Test]
    public void An_applied_edit_without_a_dry_run_moves_on_to_verification()
    {
        var next = FixMethod.NextMissingStep([Edit(true)], Target);

        Assert.That(next, Does.Contain("run_test_n_times").And.Contain("n=20"));
    }

    // ---- step 3: verification after the applied edit ----

    [Test]
    public void After_the_applied_edit_it_asks_for_20_runs_of_the_test_alone()
    {
        var next = FixMethod.NextMissingStep([Edit(false), Edit(true)], Target);

        Assert.That(next, Does.Contain("n=20").And.Contain($"testFilter {Target}"));
    }

    [Test]
    public void With_the_test_alone_done_it_asks_for_20_runs_of_the_whole_suite()
    {
        var next = FixMethod.NextMissingStep([Edit(false), Edit(true), Alone(20, 0)], Target);

        Assert.That(next, Does.Contain("n=20").And.Contain("no testFilter"));
    }

    [Test]
    public void With_the_suite_done_but_not_the_test_alone_it_asks_for_the_test_alone()
    {
        var next = FixMethod.NextMissingStep([Edit(false), Edit(true), Suite(20, 0)], Target);

        Assert.That(next, Does.Contain($"testFilter {Target}"));
    }

    [Test]
    public void Too_few_runs_after_the_edit_are_asked_for_again()
    {
        var next = FixMethod.NextMissingStep([Edit(false), Edit(true), Alone(19, 0), Suite(20, 0)], Target);

        Assert.That(next, Does.Contain("n=20").And.Contain("19"));
    }

    [Test]
    public void Runs_from_before_the_applied_edit_do_not_count()
    {
        var next = FixMethod.NextMissingStep([Suite(20, 0), Alone(20, 0), Edit(false), Edit(true)], Target);

        Assert.That(next, Does.Contain("run_test_n_times"));
    }

    [Test]
    public void A_second_applied_edit_starts_verification_again()
    {
        var next = FixMethod.NextMissingStep([Edit(false), Edit(true), Alone(20, 0), Suite(20, 0), Edit(true)], Target);

        Assert.That(next, Does.Contain("run_test_n_times"));
    }

    // ---- complete ----

    [Test]
    public void A_proven_fix_completes_the_method()
    {
        var next = FixMethod.NextMissingStep([Edit(false), Edit(true), Suite(20, 0), Alone(20, 0)], Target);

        Assert.That(next, Is.Null);
    }

    [Test]
    public void A_fix_that_still_fails_also_completes_the_method()
    {
        var next = FixMethod.NextMissingStep([Edit(false), Edit(true), Suite(20, 0), Alone(20, 3)], Target);

        Assert.That(next, Is.Null);
    }

    [Test]
    public void Other_tests_failing_in_the_suite_do_not_hold_the_method_open()
    {
        var next = FixMethod.NextMissingStep([Edit(false), Edit(true), Suite(20, 0, otherFailed: 9), Alone(20, 0)], Target);

        Assert.That(next, Is.Null);
    }

    // ---- the apply argument as the adapters really deliver it ----

    [TestCase("true")]
    [TestCase("\"true\"")]
    public void Apply_as_json_true_counts_as_the_applied_edit(string json)
    {
        var next = FixMethod.NextMissingStep([Edit(false), Edit(Json(json))], Target);

        Assert.That(next, Does.Contain("run_test_n_times"));
    }

    [Test]
    public void Apply_as_json_false_is_still_a_dry_run()
    {
        var next = FixMethod.NextMissingStep([Edit(Json("false"))], Target);

        Assert.That(next, Does.Contain("apply=true"));
    }
}
