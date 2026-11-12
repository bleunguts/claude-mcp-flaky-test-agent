namespace FlakyDetective.Agent;

// The fix phase's version of InvestigationMethod. Returns a sentence naming the first step still missing, or null when
// the method is complete, so the loop can refuse an answer that arrives too early and nudge instead.
//
// The order matters here, unlike in the investigation: runs only count when they come after the applied edit, and
// Verification.Check already works that out. So this class decides the two edit steps and hands the rest to Check.
//   1. edit_source with apply=false     show the change first (skipped if the model applied one straight away)
//   2. edit_source with apply=true      one applied edit
//   3. 20 runs of the test alone, and 20 runs of the whole suite, after that edit (Verification.Check)
// A fix that still fails is a complete method: the honest answer is "this did not work".
public static class FixMethod
{
    public static string? NextMissingStep(IReadOnlyList<ToolExecution> calls, string targetTest)
    {
        var verification = Verification.Check(calls, targetTest);

        if (verification.State == VerificationState.NoAppliedEdit)
        {
            return calls.Any(IsDryRunEdit)
                ? "Next apply the change: call edit_source again with the same path, oldText and newText, and apply=true. " +
                  $"Make this one edit only. The proposed change was for {PathOf(calls.Last(IsDryRunEdit))}."
                : calls.Any(c => c.Name == "edit_source")
                    ? "Your edit_source call failed. Call it again with a path under sandbox/, oldText that appears exactly once in the file, newText, and apply=false."
                    : "First propose the change: call edit_source with the path of the test file, oldText, newText and apply=false.";
        }

        return verification.State switch
        {
            VerificationState.SingleRunMissing =>
                $"Next verify the fix: call run_test_n_times with n={Verification.MinRuns} and testFilter {targetTest}. " +
                $"Runs from before the edit do not count ({Seen(verification.Alone)}).",
            VerificationState.SuiteRunMissing =>
                $"Next verify the fix: call run_test_n_times with n={Verification.MinRuns} and no testFilter, to run the whole suite. " +
                $"Runs from before the edit do not count ({Seen(verification.Suite)}).",
            _ => null   // Passed, or Failed: either way the method is complete
        };
    }

    private static string Seen(Measured.TestCount? count) =>
        count is null ? "none counted since the edit" : $"{count.Runs} counted since the edit";

    // A change that was shown but not written: a successful edit_source where apply is false or absent.
    private static bool IsDryRunEdit(ToolExecution call) =>
        call.Name == "edit_source"
        && call.Succeeded
        && !(call.Input.TryGetValue("apply", out var apply) && Verification.IsApply(apply));

    private static string PathOf(ToolExecution call) =>
        call.Input.TryGetValue("path", out var path) ? $"\"{path}\"" : "the file you chose";
}
