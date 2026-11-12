using System.Text.Json;
using FlakyDetective.Agent;
using NUnit.Framework;

namespace FlakyDetective.Agent.Tests;

// Verification.Check decides from tool results whether a fix is proven. These tests feed it hand-built
// executions, so there is no model and no server.
public class VerificationTests
{
    private const string Target = "Reset_clears_cache";

    [Test]
    public void No_edit_at_all_is_NoAppliedEdit()
    {
        var result = Verification.Check([Suite(20, targetFailed: 6), Alone(20, 0)], Target);

        Assert.That(result.State, Is.EqualTo(VerificationState.NoAppliedEdit));
    }

    [Test]
    public void A_dry_run_edit_does_not_count()
    {
        var result = Verification.Check([Edit(apply: false), Alone(20, 0), Suite(20, 0)], Target);

        Assert.That(result.State, Is.EqualTo(VerificationState.NoAppliedEdit));
    }

    [Test]
    public void An_edit_without_the_apply_argument_does_not_count_and_does_not_throw()
    {
        var result = Verification.Check([EditWithoutApply()], Target);

        Assert.That(result.State, Is.EqualTo(VerificationState.NoAppliedEdit));
    }

    [Test]
    public void An_edit_the_tool_refused_does_not_count()
    {
        var result = Verification.Check([Edit(apply: true, failed: true), Alone(20, 0), Suite(20, 0)], Target);

        Assert.That(result.State, Is.EqualTo(VerificationState.NoAppliedEdit));
    }

    [TestCase(true)]
    [TestCase("true")]
    [TestCase("TRUE")]
    public void Apply_as_a_bool_or_text_counts_as_applied(object apply)
    {
        var result = Verification.Check([Edit(apply)], Target);

        Assert.That(result.State, Is.EqualTo(VerificationState.SingleRunMissing));
    }

    // The Ollama adapter hands tool arguments over as JsonElement values, so this is the real path.
    [Test]
    public void Apply_as_a_json_true_counts_as_applied()
    {
        var result = Verification.Check([Edit(Json("true"))], Target);

        Assert.That(result.State, Is.EqualTo(VerificationState.SingleRunMissing));
    }

    [TestCase("false")]
    [TestCase("\"false\"")]
    public void Apply_as_json_false_does_not_count(string json)
    {
        var result = Verification.Check([Edit(Json(json)), Alone(20, 0), Suite(20, 0)], Target);

        Assert.That(result.State, Is.EqualTo(VerificationState.NoAppliedEdit));
    }

    [Test]
    public void An_applied_edit_with_no_runs_after_it_is_not_verified()
    {
        var result = Verification.Check([Edit(true)], Target);

        Assert.That(result.State, Is.EqualTo(VerificationState.SingleRunMissing));
    }

    [Test]
    public void Runs_from_before_the_edit_do_not_count()
    {
        var result = Verification.Check([Alone(20, 0), Suite(20, 0), Edit(true)], Target);

        Assert.That(result.State, Is.EqualTo(VerificationState.SingleRunMissing));
    }

    [Test]
    public void A_second_applied_edit_starts_verification_again()
    {
        var result = Verification.Check([Edit(true), Alone(20, 0), Suite(20, 0), Edit(true)], Target);

        Assert.That(result.State, Is.EqualTo(VerificationState.SingleRunMissing));
    }

    [Test]
    public void Too_few_runs_alone_is_SingleRunMissing()
    {
        var result = Verification.Check([Edit(true), Alone(19, 0), Suite(20, 0)], Target);

        Assert.That(result.State, Is.EqualTo(VerificationState.SingleRunMissing));
    }

    [Test]
    public void Too_few_runs_in_the_suite_is_SuiteRunMissing()
    {
        var result = Verification.Check([Edit(true), Alone(20, 0), Suite(19, 0)], Target);

        Assert.That(result.State, Is.EqualTo(VerificationState.SuiteRunMissing));
    }

    [Test]
    public void No_suite_run_is_SuiteRunMissing()
    {
        var result = Verification.Check([Edit(true), Alone(20, 0)], Target);

        Assert.That(result.State, Is.EqualTo(VerificationState.SuiteRunMissing));
    }

    [Test]
    public void No_single_test_run_is_SingleRunMissing()
    {
        var result = Verification.Check([Edit(true), Suite(20, 0)], Target);

        Assert.That(result.State, Is.EqualTo(VerificationState.SingleRunMissing));
    }

    [Test]
    public void Clean_runs_of_a_different_test_do_not_verify_the_target()
    {
        var result = Verification.Check([Edit(true), Alone(20, 0, test: "Mid_is_positive"), Suite(20, 0)], Target);

        Assert.That(result.State, Is.EqualTo(VerificationState.SingleRunMissing));
    }

    [Test]
    public void Twenty_clean_runs_in_both_scopes_is_Passed()
    {
        var result = Verification.Check([Edit(true), Suite(20, 0), Alone(20, 0)], Target);

        Assert.That(result.State, Is.EqualTo(VerificationState.Passed));
        Assert.That(result.Alone!.Runs, Is.EqualTo(20));
        Assert.That(result.Suite!.Runs, Is.EqualTo(20));
    }

    [Test]
    public void More_than_the_minimum_runs_also_passes()
    {
        var result = Verification.Check([Edit(true), Suite(30, 0), Alone(30, 0)], Target);

        Assert.That(result.State, Is.EqualTo(VerificationState.Passed));
    }

    [Test]
    public void Other_tests_failing_in_the_suite_do_not_matter_if_the_target_is_clean()
    {
        var result = Verification.Check([Edit(true), Suite(20, 0, otherFailed: 7), Alone(20, 0)], Target);

        Assert.That(result.State, Is.EqualTo(VerificationState.Passed));
    }

    [Test]
    public void A_failure_alone_is_Failed()
    {
        var result = Verification.Check([Edit(true), Suite(20, 0), Alone(20, 1)], Target);

        Assert.That(result.State, Is.EqualTo(VerificationState.Failed));
        Assert.That(result.Alone!.Failed, Is.EqualTo(1));
    }

    [Test]
    public void A_failure_in_the_suite_is_Failed()
    {
        var result = Verification.Check([Edit(true), Suite(20, 2), Alone(20, 0)], Target);

        Assert.That(result.State, Is.EqualTo(VerificationState.Failed));
        Assert.That(result.Suite!.Failed, Is.EqualTo(2));
    }

    [Test]
    public void A_failure_is_Failed_even_with_few_runs()
    {
        var result = Verification.Check([Edit(true), Alone(3, 1)], Target);

        Assert.That(result.State, Is.EqualTo(VerificationState.Failed));
    }

    [Test]
    public void Failures_from_before_the_edit_are_ignored()
    {
        var result = Verification.Check([Suite(20, 8), Alone(20, 0), Edit(true), Suite(20, 0), Alone(20, 0)], Target);

        Assert.That(result.State, Is.EqualTo(VerificationState.Passed));
    }

    // ---- Unproven: what the loop prints at the end of a run ----

    [Test]
    public void Unproven_is_null_when_no_fix_was_tried()
    {
        Assert.That(Verification.Unproven([Suite(20, 6), Alone(20, 0)], Target), Is.Null);
    }

    [Test]
    public void Unproven_is_null_for_a_dry_run_edit()
    {
        Assert.That(Verification.Unproven([Edit(false)], Target), Is.Null);
    }

    [Test]
    public void Unproven_is_null_when_the_fix_is_proven()
    {
        Assert.That(Verification.Unproven([Edit(true), Suite(20, 0), Alone(20, 0)], Target), Is.Null);
    }

    [Test]
    public void Unproven_flags_an_applied_edit_with_no_runs_after_it()
    {
        var problem = Verification.Unproven([Edit(true)], Target);

        Assert.That(problem, Does.Contain(Target).And.Contain("not proven").And.Contain("SingleRunMissing"));
    }

    [Test]
    public void Unproven_flags_an_applied_edit_that_still_fails()
    {
        var problem = Verification.Unproven([Edit(true), Suite(20, 0), Alone(20, 2)], Target);

        Assert.That(problem, Does.Contain("Failed"));
    }

    // ---- helpers: build the executions the loop would have recorded ----

    private static ToolExecution Edit(object? apply, bool failed = false)
    {
        var input = new Dictionary<string, object?>
        {
            ["path"] = "sandbox/FlakyLab/FlakyLabTests.cs", ["oldText"] = "a", ["newText"] = "b", ["apply"] = apply
        };
        return EditWith(input, failed);
    }

    private static ToolExecution EditWithoutApply() =>
        EditWith(new Dictionary<string, object?>
        {
            ["path"] = "sandbox/FlakyLab/FlakyLabTests.cs", ["oldText"] = "a", ["newText"] = "b"
        }, false);

    private static ToolExecution EditWith(Dictionary<string, object?> input, bool failed)
    {
        var result = failed
            ? "{\"path\":\"sandbox/FlakyLab/FlakyLabTests.cs\",\"applied\":false,\"diff\":\"\",\"error\":\"oldText not found in file\"}"
            : "{\"path\":\"sandbox/FlakyLab/FlakyLabTests.cs\",\"applied\":true,\"diff\":\"line 1\"}";
        return new ToolExecution("edit_source", input, result, false);
    }

    // The target test run on its own: n runs, `failed` of them failed.
    private static ToolExecution Alone(int n, int failed, string test = Target) =>
        Run(test, n, new Dictionary<string, int> { [test] = failed });

    // The whole suite: the target plus another test that may be failing too.
    private static ToolExecution Suite(int n, int targetFailed, int otherFailed = 0) =>
        Run(null, n, new Dictionary<string, int> { [Target] = targetFailed, ["Writer_sets_eurusd"] = otherFailed });

    private static ToolExecution Run(string? filter, int n, Dictionary<string, int> failedByTest)
    {
        var summaries = string.Join(",", failedByTest.Select(kv =>
            $"{{\"testName\":\"{kv.Key}\",\"runs\":{n},\"passed\":{n - kv.Value},\"failed\":{kv.Value},\"failureRate\":0}}"));
        var filterPart = filter == null ? "" : $"\"testFilter\":\"{filter}\",";
        var result = $"{{\"requested\":{n},\"completed\":{n},{filterPart}\"runs\":[],\"testSummaries\":[{summaries}]}}";
        var input = new Dictionary<string, object?> { ["n"] = n };
        if (filter != null) input["testFilter"] = filter;
        return new ToolExecution("run_test_n_times", input, result, false);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;
}
