using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using static FlakyDetective.Agent.Measured;
using static System.Net.WebRequestMethods;

namespace FlakyDetective.Agent;

public enum VerificationState { NoAppliedEdit, SuiteRunMissing, SingleRunMissing, Failed, Passed }

public record VerificationResult(VerificationState State, string Message, TestCount? Suite, TestCount? Alone);

public static class Verification
{
    public const int MinRuns = 20;

    public static VerificationResult Check(IReadOnlyList<ToolExecution> executions, string targetTest)
    {
        int lastEditIndex = -1;
        for (int i = executions.Count - 1; i >= 0; i--)
        {
            if (executions[i].Name.Equals("edit_source", StringComparison.Ordinal) 
                && executions[i].Succeeded 
                && executions[i].Input.TryGetValue("apply", out var apply) && IsTrue(apply))
            {
                lastEditIndex = i;
                break;
            }
        }                

        if (lastEditIndex == -1)
        {
            return new VerificationResult(VerificationState.NoAppliedEdit, "No applied edit found", null, null);
        }

        var slice = executions.Skip(lastEditIndex + 1).ToList();

        var rates = Measured.Targets(slice).FirstOrDefault(r => r.Test.Equals(targetTest, StringComparison.Ordinal));

        var alone = rates?.Alone;
        var suite = rates?.Suite;

        if (alone is { Failed: > 0 } || suite is { Failed: > 0 }) return new VerificationResult(VerificationState.Failed, "Test failed", suite, alone);
        if (alone is null || alone.Runs < MinRuns) return new VerificationResult(VerificationState.SingleRunMissing, "Single run missing", suite, alone);
        if (suite is null || suite.Runs < MinRuns) return new VerificationResult(VerificationState.SuiteRunMissing, "Suite run missing", suite, alone);

        return new VerificationResult(VerificationState.Passed, "Test passed", suite, alone);

        static bool IsTrue(object? apply)
        {
            switch (apply)
            {
                case bool b: return b;
                case string s: return s.Equals("true", StringComparison.OrdinalIgnoreCase);
                case JsonElement { ValueKind: JsonValueKind.True }: return true;
                case JsonElement { ValueKind: JsonValueKind.String } e: return string.Equals(e.GetString(), "true", StringComparison.OrdinalIgnoreCase);
                default: return false;
            }
        }
    }  
}
