using System.Text.Json;

namespace FlakyDetective.Agent;

public static class DemoScripts
{
    // The happy path: ping, find a test, read the lines that were found, finish.
    public static ScriptedModel Demo() => new ScriptedModel([
        messages =>
        {
            Console.WriteLine("Step 1: Call ping");
            return Script.CallTool("call_1", "ping", []);
        },
        messages =>
        {
            Console.WriteLine("Step 2: Call find_test_source");
            return Script.CallTool("call_2", "find_test_source", new Dictionary<string, object?> { { "testName", "Reset_clears_cache" } });
        },
        messages =>
        {
            Console.WriteLine("Step 3: Read");
            var lastMessage = messages.Last();
            var answer = lastMessage.Content.OfType<ToolResultBlock>().First();
            var json = JsonDocument.Parse(answer.Content).RootElement;
            var match = json.GetProperty("matches")[0];
            var filePath = match.GetProperty("file").GetString();
            var startLine = match.GetProperty("startLine").GetInt32();
            var endLine = match.GetProperty("endLine").GetInt32();
            return Script.CallTool("call_3", "read_source", new Dictionary<string, object?>
            {
                { "path", filePath },
                { "startLine", startLine },
                { "endLine", endLine }
            });
        },
        messages =>
        {
            Console.WriteLine("Step 4: Finish");
            return Script.Finish("Done!");
        }
    ]);

    // Asks for a tool that does not exist. Expect an IsError tool_result and a normal finish.
    public static ScriptedModel UnknownTool() => new ScriptedModel([
        messages =>
        {
            Console.WriteLine("Step 1: Call unknown tool");
            return Script.CallTool("call_1", "no_such_tool", []);
        },
        messages =>
        {
            Console.WriteLine("Step 2: Finish");
            return Script.Finish("Done!");
        }
    ]);

    // Reads .env. The server refuses with an "error" field in normal text (IsError = false).
    public static ScriptedModel DeniedFile() => new ScriptedModel([
        messages =>
        {
            Console.WriteLine("Step 1: Call read_source with denied file argument");
            return Script.CallTool("call_1", "read_source", new Dictionary<string, object?>
            {
                { "path", ".env" }
            });
        },
        messages =>
        {
            Console.WriteLine("Step 2: Finish");
            return Script.Finish("Done!");
        }
    ]);

    // Reads a file outside the solution root. Refused the same way as DeniedFile.
    public static ScriptedModel EscapeAttempt() => new ScriptedModel([
        messages =>
        {
            Console.WriteLine("Step 1: Call read_source with escape");
            return Script.CallTool("call_1", "read_source", new Dictionary<string, object?>
            {
                { "path", "../README.md" }
            });
        },
        messages =>
        {
            Console.WriteLine("Step 2: Finish");
            return Script.Finish("Done!");
        }
    ]);

    // Sends a non-numeric startLine. The SDK rejects the call: IsError = true with a generic message.
    public static ScriptedModel BadArgument() => new ScriptedModel([
        messages =>
        {
            Console.WriteLine("Step 1: Call read_source with bad argument");
            return Script.CallTool("call_1", "read_source", new Dictionary<string, object?>
            {
                { "path", "sandbox/FlakyLab/FlakyLabTests.cs" },
                { "startLine", "abc" },
                { "endLine", 101 }
            });
        },
        messages =>
        {
            Console.WriteLine("Step 2: Finish");
            return Script.Finish("Done!");
        }
    ]);

    // Never finishes. The loop's iteration cap is the only thing that stops it.
    public static IModel Runaway() => new RunawayModel();

    // Asks for two tools in a single reply, as the small model keeps doing. Guardrail 1 should run
    // only the first (ping) and answer the second with a "skipped" error result.
    public static ScriptedModel TwoToolsAtOnce() => new ScriptedModel([
        messages =>
        {
            Console.WriteLine("Step 1: two tool calls in one reply");
            return new ModelResponse(
                [
                    new ToolUseBlock("call_1", "ping", new Dictionary<string, object?>()),
                    new ToolUseBlock("call_2", "find_test_source", new Dictionary<string, object?> { { "testName", "Reset_clears_cache" } }),
                ],
                StopReason.ToolUse);
        },
        messages =>
        {
            Console.WriteLine("Step 2: Finish");
            return Script.Finish("Done!");
        }
    ]);
}

// Guardrail 2 test helpers. These scripts are used with InvestigationMethod.NextMissingStep.
public static class GuardrailScripts
{
    // Answers at once, is nudged, then does the whole method with n=1 (fast) and answers again.
    // Expect one nudge and a clean finish.
    public static ScriptedModel AnswersEarlyThenCompletes() => new ScriptedModel([
        messages => Script.Finish("Verdict: stable (an early answer, before any measuring)"),
        messages => Script.CallTool("call_1", "run_test_n_times", new Dictionary<string, object?> { { "n", 1 } }),
        messages => Script.CallTool("call_2", "run_test_n_times", new Dictionary<string, object?> { { "n", 1 }, { "testFilter", "Mid_is_positive" } }),
        messages => Script.CallTool("call_3", "find_test_source", new Dictionary<string, object?> { { "testName", "Mid_is_positive" } }),
        messages => Script.CallTool("call_4", "read_source", new Dictionary<string, object?>
        {
            { "path", "sandbox/FlakyLab/FlakyLabTests.cs" }, { "startLine", 62 }, { "endLine", 67 }
        }),
        messages => Script.Finish("Verdict: stable"),
    ]);

    // Cites a tool it never called and quotes a result that does not exist. With no required steps set,
    // the loop accepts the answer and reports UNGROUNDED lines (guardrail 3, report only).
    // Note: the invented quote must not contain a method declaration such as "public void <TestName>()".
    // find_test_source scans every .cs file under the solution, so such text in this file would be
    // returned as a second match for that test.
    public static ScriptedModel InventedEvidence() => new ScriptedModel([
        messages => Script.CallTool("call_1", "ping", []),
        messages => Script.Finish(
            "Test: Mid_is_positive\n" +
            "Verdict: flaky\n" +
            "Evidence:\n" +
            "- read_source: {\"path\":\"sandbox/FlakyLab/FlakyLabTests.cs\",\"text\":\"invented source for the test\"}\n" +
            "- ping: {\"result\":\"not what the tool said\"}\n" +
            "Confidence: low"),
    ]);

    // Never does any step and answers every time. The nudge limit is reached and the run is flagged INCOMPLETE.
    public static IModel Stubborn() => new StubbornModel();

    // Does every step, but the read goes to a path that does not exist. A step counts only if the call worked, so
    // the loop nudges ("Your read_source call failed ...") and the script then reads the real lines.
    // Expect one nudge about read_source and a clean finish.
    public static ScriptedModel ReadFailsThenRetries() => new ScriptedModel([
        messages => Script.CallTool("call_1", "run_test_n_times", new Dictionary<string, object?> { { "n", 1 } }),
        messages => Script.CallTool("call_2", "run_test_n_times", new Dictionary<string, object?> { { "n", 1 }, { "testFilter", "Mid_is_positive" } }),
        messages => Script.CallTool("call_3", "find_test_source", new Dictionary<string, object?> { { "testName", "Mid_is_positive" } }),
        messages => Script.CallTool("call_4", "read_source", new Dictionary<string, object?>
        {
            { "path", "src/MyProject.Tests/Mid_is_positive.cs" }, { "startLine", 1 }, { "endLine", 10 }
        }),
        messages => Script.Finish("Verdict: stable"),
        messages => Script.CallTool("call_5", "read_source", new Dictionary<string, object?>
        {
            { "path", "sandbox/FlakyLab/FlakyLabTests.cs" }, { "startLine", 62 }, { "endLine", 67 }
        }),
        messages => Script.Finish("Verdict: stable"),
    ]);

    // Does every step, then ends with a sentence of intent instead of an answer, as a small model did. The answer
    // has no Verdict line, so the loop nudges, and the second answer is accepted. Expect one nudge about the format.
    public static ScriptedModel AnswerWithoutVerdict() => new ScriptedModel([
        messages => Script.CallTool("call_1", "run_test_n_times", new Dictionary<string, object?> { { "n", 1 } }),
        messages => Script.CallTool("call_2", "run_test_n_times", new Dictionary<string, object?> { { "n", 1 }, { "testFilter", "Mid_is_positive" } }),
        messages => Script.CallTool("call_3", "find_test_source", new Dictionary<string, object?> { { "testName", "Mid_is_positive" } }),
        messages => Script.CallTool("call_4", "read_source", new Dictionary<string, object?>
        {
            { "path", "sandbox/FlakyLab/FlakyLabTests.cs" }, { "startLine", 62 }, { "endLine", 67 }
        }),
        messages => Script.Finish("To confirm if this test is flaky, I will run it again."),
        messages => Script.Finish("Verdict: stable"),
    ]);

    // Measures Mid_is_positive (which never fails) and then calls it flaky. The verdict contradicts the
    // measurements, so expect an INCONSISTENT line. Use with no required steps.
    public static ScriptedModel VerdictContradictsMeasured() => new ScriptedModel([
        messages => Script.CallTool("call_1", "run_test_n_times", new Dictionary<string, object?> { { "n", 1 } }),
        messages => Script.CallTool("call_2", "run_test_n_times", new Dictionary<string, object?> { { "n", 1 }, { "testFilter", "Mid_is_positive" } }),
        messages => Script.Finish("Test: Mid_is_positive\nVerdict: flaky\nSuspected cause: unknown\nConfidence: low"),
    ]);

    // The fix phase, end to end, with no LLM. Mid_is_positive has been broken on purpose (its assertion inverted, see
    // region 11 of Program.cs), and this script puts the original assertion back: a dry run, one applied edit, then 20
    // runs of the test alone and 20 of the whole suite, which is the order FixMethod enforces. This restores a
    // deliberately broken control. It diagnoses nothing about any flaky test.
    public const string BrokenAssertion = "Assert.That(mid, Is.LessThan(0));";
    public const string OriginalAssertion = "Assert.That(mid, Is.GreaterThan(0));";

    public static ScriptedModel RepairBrokenControl()
    {
        Dictionary<string, object?> Edit(bool apply) => new()
        {
            ["path"] = "sandbox/FlakyLab/FlakyLabTests.cs",
            ["oldText"] = BrokenAssertion,
            ["newText"] = OriginalAssertion,
            ["apply"] = apply
        };

        return new ScriptedModel([
            messages => Script.CallTool("call_1", "edit_source", Edit(false)),
            messages => Script.CallTool("call_2", "edit_source", Edit(true)),
            messages => Script.CallTool("call_3", "run_test_n_times", new Dictionary<string, object?> { { "n", 20 }, { "testFilter", "Mid_is_positive" } }),
            messages => Script.CallTool("call_4", "run_test_n_times", new Dictionary<string, object?> { { "n", 20 } }),
            messages => Script.Finish(string.Join(Environment.NewLine, "Test: Mid_is_positive", "Change: restored the assertion that was inverted.")),
        ]);
    }

    // Runs the whole suite once, then lists a failed run of Mid_is_positive that no tool reported (it never fails).
    // This is the model relabelling another test's failures. Use with no required steps; expect an UNGROUNDED line.
    public static ScriptedModel RelabelledFailure() => new ScriptedModel([
        messages => Script.CallTool("call_1", "run_test_n_times", new Dictionary<string, object?> { { "n", 1 } }),
        messages => Script.Finish(
            "Test: Mid_is_positive\n" +
            "Verdict: flaky\n" +
            "Evidence:\n" +
            "- \"testName\":\"Mid_is_positive\",\"runNumber\":1,\"outcome\":\"Failed\",\"durationMs\":58.8\n" +
            "Confidence: medium"),
    ]);
}

// Answers immediately on every call, however often it is nudged.
public class StubbornModel : IModel
{
    public Task<ModelResponse> CompleteAsync(List<Message> messages, IReadOnlyList<ToolDefinition> tools, string? systemPrompt = null, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("StubbornModel: answering without doing anything");
        return Task.FromResult(Script.Finish("I am done."));
    }
}

// Asks for ping on every call, with a fresh id each time.
public class RunawayModel : IModel
{
    private int _calls;

    public Task<ModelResponse> CompleteAsync(List<Message> messages, IReadOnlyList<ToolDefinition> tools, string? systemPrompt = null, CancellationToken cancellationToken = default)
    {
        _calls++;
        Console.WriteLine($"RunawayModel: asking for ping again (call {_calls})");
        return Task.FromResult(Script.CallTool($"call_{_calls}", "ping", []));
    }
}
