using FlakyDetective.Agent;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Net.Http.Json;               
using System.Text.Json;                   
using System.Text.Json.Serialization;     

#region 1. Connect to the MCP server and list its tools (issue #19, steps 1-2)
// WHAT:     Starts the MCP server (src/FlakyDetective.McpServer) as a child process over stdio,
//           does the MCP handshake, and lists the tools it offers: ping, run_test_n_times,
//           read_source, find_test_source.
// REMEMBER: Launch the BUILT dll, not `dotnet run`: build output on stdout would corrupt the
//           protocol. Build the solution first. `await using` shuts the server down on exit,
//           otherwise a stray server process keeps its .exe locked.
// OUTPUT:   Four tools with their descriptions and JSON schemas. ToolDefinition is our own type;
//           name + description + schema is exactly what a model is later told about each tool.
Console.WriteLine("Hello, World! from the FlakyDetective.Agent");
var root = Helpers.FindSlnFolderRecursively(AppContext.BaseDirectory);
var serverDll = Path.Combine(root, "src", "FlakyDetective.McpServer", "bin", "Debug", "net10.0", "FlakyDetective.McpServer.dll");
Console.WriteLine($"Starting MCP server from: {serverDll}");
Console.WriteLine($"File exists: {File.Exists(serverDll)}");

Console.WriteLine("=====================================================================> tools listed");
var transport = new StdioClientTransport(new StdioClientTransportOptions
{
    Name = "FlakyDetective.McpServer",
    Command = "dotnet",
    Arguments = [serverDll],
});
await using var client = await McpClient.CreateAsync(transport);
var tools = await client.ListToolsAsync();
var toolDefinitions = tools
    .Select(t => new ToolDefinition(t.Name, t.Description, t.JsonSchema))
    .ToList();
foreach (var tool in toolDefinitions)
{
    Console.WriteLine($"Tool: {tool.Name} - {tool.Description} - {tool.InputSchema}");
}
#endregion

#region 2. Calling tools by hand, no model involved (issue #19, step 3)
// WHAT:     Calls the tools directly to see the raw results the loop will later have to handle.
//           This runs on every start, so the output is a quick health check of the server.
// LESSON:   There are two kinds of tool failure and the loop must handle both.
//             - A tool's own refusal (".env", paths outside the solution) comes back as normal
//               text containing an "error" field, with IsError = false.
//             - A call the SDK rejects (for example a non-numeric startLine) comes back with
//               IsError = true and a generic message.
// OUTPUT:   pong, the location of Reset_clears_cache, lines 83-92 of FlakyLabTests.cs,
//           then the refusals.
Console.WriteLine("=====================================================================> tools invoked");

// call ping
Log.PrintToolResult(await client.CallToolAsync("ping"));

// call find_test_source with arg
Log.PrintToolResult(await client.CallToolAsync(
    "find_test_source",
    new Dictionary<string, object>
    {
        { "testName", "Reset_clears_cache" }
    }));

Log.PrintToolResult(await client.CallToolAsync("read_source",
    new Dictionary<string, object?>
    {
        ["path"] = "sandbox/FlakyLab/FlakyLabTests.cs",
        ["startLine"] = 83,
        ["endLine"] = 92,
    }));
Log.PrintToolResult(await client.CallToolAsync("read_source",
    new Dictionary<string, object?>
    {
        ["path"] = ".env",
        ["startLine"] = 83,
        ["endLine"] = 92,
    }));

Log.PrintToolResult(await client.CallToolAsync("read_source",
    new Dictionary<string, object?>
    {
        ["path"] = "",
        ["startLine"] = "44",
        ["endLine"] = 92,
    }));
#endregion

#region 3. Messages and the scripted model, tried by hand (issue #19, steps 4-5)
// WHAT:     Builds a conversation by hand (user text -> assistant tool_use -> user tool_result)
//           and drives ScriptedModel one step at a time. Proves the message types and the
//           script work before any loop exists.
// RUN IT:   Remove the /* */. Needs toolDefinitions (region 1) and DemoScripts (region 8).
// LESSON:   A tool_result travels in a *user* message and carries the id of the tool_use it
//           answers. A script step can read the previous result from the history.
//           Gotcha found here: Script.Finish must use StopReason.EndTurn; with ToolUse the loop
//           asks the model again and the script runs out of steps.
// OUTPUT:   Role and block kinds per message, then the stop reason of each script step.
//           Step 3 of the demo script reads the last tool_result, so it only works inside the
//           real loop; here the history ends differently and that step is expected to throw.
/*
Console.WriteLine("===================================================================== conversation");

var conversation = new List<Message>
{
    new Message("user", [new TextBlock("Where is the test Reset_clears_cache?")]),
    new Message("assistant", [new ToolUseBlock("call_1", "find_test_source", new Dictionary<string, object?> { { "testName", "Reset_clears_cache" } })]),

    new Message("user", [new ToolResultBlock("call_1", "{\"testName\":\"Reset_clears_cache\",\"matches\":[{\"file\":\"sandbox/FlakyLab/FlakyLabTests.cs\",\"startLine\":83,\"endLine\":89}]}", false)]),
    //new Message("assistant", [new TextBlock("It is in sandbox/FlakyLab/FlakyLabTests.cs, lines 83 to 89.")]),
};

foreach (var message in conversation)
{
    Console.WriteLine($"Role: {message.Role}, Kinds: {string.Join(", ", message.Content.Select(b => b.GetType().Name))}");
    foreach (var block in message.Content)
    {
        Log.Print(block);
    }
}

var modelResponse1 = Script.CallTool("call_1", "ping", []);
Log.PrintModelResponse(modelResponse1);
var modelResponse2 = Script.CallTool("call_2", "find_test_source", new Dictionary<string, object?> { { "testName", "Reset_clears_cache" } });
Log.PrintModelResponse(modelResponse2);

Console.WriteLine("===================================================================== using ScriptedModel");

var scriptedModel = DemoScripts.Demo();
for(int i = 0; i < 4; i++)
{
    try
    {
        var scriptedResponse = await scriptedModel.CompleteAsync(conversation, toolDefinitions);
        Console.WriteLine($"Script completed with stop reason: {scriptedResponse.StopReason}");
        Log.PrintModelResponse(scriptedResponse);
    }
    catch (InvalidOperationException ex)
    {
        Console.WriteLine($"Script failed with exception: {ex.Message}");
    }
}
*/
#endregion

#region 4. The hand-written agent loop with the scripted model (issue #19, step 6)
// WHAT:     Runs AgentLoop.RunLoopAsync with the scripted demo: ping -> find_test_source ->
//           read_source (lines taken from the previous result) -> done.
// RUN IT:   Remove the /* */. Needs client and toolDefinitions (region 1).
// LESSON:   The loop only talks to IModel, so it cannot tell a script from a real model. One
//           user message carries all the tool_results of an assistant reply. The iteration
//           cap is the guardrail against a model that never finishes.
// OUTPUT:   "Iteration 1/10" ... "Stopped: end_turn after 4 iterations", AgentResult with
//           Iterations = 4 and HitIterationCap = False.
/* the agent loop
Console.WriteLine("===================================================================== using Agent Loop");
string scriptedPrompt = "Find and read the Reset_clears_cache test";
var scriptedResult = await AgentLoop.RunLoopAsync(DemoScripts.Demo(), client, toolDefinitions, scriptedPrompt, 10);
Console.WriteLine($"Final result: {scriptedResult}");
*/
#endregion

#region 5. Failure scripts: how the loop copes when things go wrong (issue #19, step 8)
// WHAT:     Runs the loop against models built to misbehave.
// RUN IT:   Remove the /* */. Needs client and toolDefinitions (region 1).
// EXPECTED: Runaway        asks for ping forever             -> stops at the cap, HitIterationCap = True
//           UnknownTool    calls a tool that does not exist  -> tool_result with IsError = True, loop finishes
//           DeniedFile     reads ".env"                      -> IsError = False, "error" field in the JSON
//           EscapeAttempt  reads "../README.md"              -> IsError = False, "outside the solution root"
//           BadArgument    startLine = "abc"                 -> IsError = True, generic SDK message
// LESSON:   None of them crash the loop: every failure becomes a tool_result the model can read.
/*
Console.WriteLine("===================================================================== error handling");
string failurePrompt = "Find and read the Reset_clears_cache test";

var failureResult = await AgentLoop.RunLoopAsync(DemoScripts.Runaway(), client, toolDefinitions, failurePrompt, 5);
Console.WriteLine($"Final result: {failureResult}");

failureResult = await AgentLoop.RunLoopAsync(DemoScripts.UnknownTool(), client, toolDefinitions, failurePrompt, 5);
Console.WriteLine($"Final result: {failureResult}");

failureResult = await AgentLoop.RunLoopAsync(DemoScripts.DeniedFile(), client, toolDefinitions, failurePrompt, 5);
Console.WriteLine($"Final result: {failureResult}");

failureResult = await AgentLoop.RunLoopAsync(DemoScripts.EscapeAttempt(), client, toolDefinitions, failurePrompt, 5);
Console.WriteLine($"Final result: {failureResult}");

failureResult = await AgentLoop.RunLoopAsync(DemoScripts.BadArgument(), client, toolDefinitions, failurePrompt, 5);
Console.WriteLine($"Final result: {failureResult}");
*/

//   DemoScripts.Demo()           ping -> find_test_source -> read_source -> done
//   DemoScripts.UnknownTool()    asks for a tool that does not exist
//   DemoScripts.DeniedFile()     reads .env
//   DemoScripts.EscapeAttempt()  reads ../README.md
//   DemoScripts.BadArgument()    startLine = "abc"
//   DemoScripts.Runaway()        RunawayModel, which never finishes (a finite script cannot)
#endregion

#region 6. Talking to Ollama directly, before the adapter existed (issue #22, steps 2-4)
// WHAT:     Posts raw /api/chat requests using our Ollama request types: first a plain chat,
//           then a request that offers the ping tool and prints the tool call the model returns.
// RUN IT:   Remove the /* */. Needs Ollama running and `ollama pull qwen2.5:3b`.
// LESSON:   Ollama returns tool calls in message.tool_calls, each with an id. done_reason is "stop"
//           even when a tool is requested, so the stop reason must come from the presence of
//           tool_calls. This region is why OllamaModel looks the way it does.
// OUTPUT:   A short greeting, then "Tool call: ping, id: call_xxxxxxxx".
/*
Console.WriteLine("===================================================================== ollama chat requests");
var ollamaHttp = new HttpClient { BaseAddress = new Uri("http://localhost:11434"), Timeout = TimeSpan.FromMinutes(5) };
var ollamaJson = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

var plainRequest = new OllamaChatRequest(
    "qwen2.5:3b",
    [new OllamaMessage("user", "Say hello in five words.")],
    null, false, new OllamaOptions(8192));
var plainHttpResponse = await ollamaHttp.PostAsJsonAsync("/api/chat", plainRequest, ollamaJson);
plainHttpResponse.EnsureSuccessStatusCode();
var plainReply = await plainHttpResponse.Content.ReadFromJsonAsync<OllamaChatResponse>();
Console.WriteLine(plainReply!.Message.Content);

var pingTool = new OllamaTool("function",
    new OllamaFunctionDefinition(
        "ping",
        "Pings the server with a message and returns a pong response.",
        JsonDocument.Parse("""{"type":"object","properties":{},"required":[]}""").RootElement));

var toolRequest = new OllamaChatRequest(
    "qwen2.5:3b",
    [new OllamaMessage("user", "Use the ping tool and tell me what it returned.")],
    [pingTool],
    false,
    new OllamaOptions(8192));

var toolHttpResponse = await ollamaHttp.PostAsJsonAsync("/api/chat", toolRequest, ollamaJson);
toolHttpResponse.EnsureSuccessStatusCode();
var toolReply = await toolHttpResponse.Content.ReadFromJsonAsync<OllamaChatResponse>();
foreach (var call in toolReply!.Message.ToolCalls ?? [])
{
    Console.WriteLine($"Tool call: {call.Function.Name}, id: {call.Id}");
}
*/
#endregion

#region 7. The live run: the agent loop with a real local model (issue #22)
// WHAT:     The current default. Runs the agent loop against a local Ollama model and lets it
//           decide which tools to call for the prompt below.
// PREREQ:   Ollama running, model pulled (`ollama pull qwen2.5:3b`), solution built.
// PROMPT:   Taken from the command line, so a script can run it repeatedly:
//             dotnet run --project src/FlakyDetective.Agent --no-build -- "Investigate the test Reset_clears_cache."
//           With no arguments it uses the default below. scripts/run_agent_n_times.ps1 runs it N times
//           per test and saves each log. Another demo prompt: "Use the ping tool and tell me what it returned."
// LESSON:   With qwen2.5:3b the model chained find_test_source then read_source on its own, which
//           is the whole point of the loop. Small models also pad their answers.
// OUTPUT:   Iteration log, then "Stopped: end_turn after 3 iterations" and the final AgentResult.
Console.WriteLine("===================================================================== using Agent Loop");
var liveHttp = new HttpClient { BaseAddress = new Uri("http://localhost:11434"), Timeout = TimeSpan.FromMinutes(5) };
var liveModel = new OllamaModel(liveHttp, "qwen2.5:3b", 8192);

string livePrompt = args.Length > 0 ? string.Join(" ", args) : "Investigate the test Mid_is_positive.";
Console.WriteLine($"Prompt: {livePrompt}");

var liveResult = await AgentLoop.RunLoopAsync(
    liveModel, client, toolDefinitions, livePrompt, 12, Prompts.InvestigateV3,
    nextMissingStep: InvestigationMethod.NextMissingStep,
    answerProblem: InvestigationMethod.AnswerProblem,
    stateMeasuredRates: true);   // V3 does not ask the model for rates: the loop appends what the tools measured
Console.WriteLine($"Final result: {liveResult}");
#endregion

#region 8. The live run with Claude through the Anthropic API (issue #27)
// WHAT:     The same loop as region 7 with AnthropicModel behind IModel instead of OllamaModel.
//           Nothing else changes, which is the point of the IModel interface.
// RUN IT:   Remove the /* */, and comment out region 7 if you do not want both to run.
//           Needs ANTHROPIC_API_KEY set to a key with credits (the SDK reads it; never commit it).
// WITHOUT CREDITS: the request is still built and sent. The API refuses it and AnthropicModel throws
//           a readable error: 401 for a missing or invalid key, 400 for a request the API rejects
//           (a missing credit balance is reported this way). The program ends with that message.
// LESSON:   Claude's reasoning comes back as thinking blocks, which must be sent back unchanged on
//           the next turn, so AgentLoop keeps the whole reply in the history. The default model is
//           claude-opus-5-5; the second constructor argument picks another, such as the far
//           cheaper claude-haiku-5-5.
// OUTPUT:   The same iteration log as region 7, plus "Thinking: ..." lines.
/*
Console.WriteLine("===================================================================== using Agent Loop with Claude");
var claudeModel = new AnthropicModel(new Anthropic.AnthropicClient());
// var claudeModel = new AnthropicModel(new Anthropic.AnthropicClient(), "claude-haiku-5-5");

string claudePrompt = "Find and read the Reset_clears_cache test";

var claudeResult = await AgentLoop.RunLoopAsync(
    claudeModel, client, toolDefinitions, claudePrompt, 12, Prompts.InvestigateV3,
    nextMissingStep: InvestigationMethod.NextMissingStep,
    answerProblem: InvestigationMethod.AnswerProblem,
    stateMeasuredRates: true);
Console.WriteLine($"Final result: {claudeResult}");
*/
#endregion

#region 9. Loop guardrails: enforcing the method in code, not in the prompt (issue #29)
// WHAT:     Runs the loop against scripted models that misbehave in the way the real model does,
//           to check that each guardrail holds. No LLM is involved, so the result is deterministic.
// RUN IT:   Remove the /* */. Needs client and toolDefinitions (region 1).
// WHY:      Live runs showed the model skips steps, makes parallel tool calls, answers from one or two
//           measurements however the prompt is worded, and writes evidence it never received. Things
//           code can enforce, code should enforce.
// EXPECTED: TwoToolsAtOnce          guardrail 1, one tool per turn
//             ping runs; find_test_source gets a "Skipped" error result and is not executed.
//             Tools called (2): ping(); find_test_source(testName=Reset_clears_cache) [skipped]
//           AnswersEarlyThenCompletes  guardrail 2, no answer before the method is done
//             "Nudge 1/3 ..." after the early answer, then four tool calls, then a clean finish.
//           Stubborn                guardrail 2, the nudge limit
//             Nudge 1/3, 2/3, 3/3, then "INCOMPLETE: the method was not finished ..." and Incomplete = True.
//           InventedEvidence        guardrail 3, grounding check (report only)
//             "UNGROUNDED: the answer cites read_source, which was never called" and
//             "UNGROUNDED: the evidence quoted for ping does not appear in any of its real results".
//           ReadFailsThenRetries    guardrail 2, a step counts only if the call worked
//             The read_source call returns "file not found", so: "Nudge 1/3 ... Your read_source call
//             failed ... Do not guess a path." The script then reads the real lines and finishes cleanly.
//           AnswerWithoutVerdict    guardrail 2, the answer must follow the format
//             An answer with no Verdict line is nudged: "Your answer must follow the format ...".
//           RelabelledFailure       guardrail 3, failed runs listed in the answer must exist
//             "UNGROUNDED: the answer lists failed run(s) of Mid_is_positive (run 1) that no tool reported"
//             (a small model copied other tests' failed rows out of a suite result and relabelled them).
//           VerdictContradictsMeasured  check 4, the verdict must agree with the measurements (report only)
//             "INCONSISTENT: the verdict says flaky, but the tools measured no failures of Mid_is_positive
//             (alone 0/1 failed; in the suite 0/1 failed)".
//           StateMeasuredRates      the loop states the measured figures itself
//             The final text ends with "Measured by the tools:" and the figures, so a wrong figure written
//             by the model is never the last word. Used with prompt V3, which does not ask for rates.
// LESSON:   A skipped tool_use still needs a tool_result, because the API pairs every request with a
//           result. The skipped call stays in the history; only its execution is refused. A nudge is just
//           a user message after the assistant's answer, so roles still alternate. The grounding check
//           only reports: it cannot tell a lie from a clumsy quote, so a human reads the flags.
/*
Console.WriteLine("===================================================================== guardrail 1: one tool per turn");
var guardrailResult = await AgentLoop.RunLoopAsync(DemoScripts.TwoToolsAtOnce(), client, toolDefinitions, "Investigate the test Reset_clears_cache.", 5);
Console.WriteLine($"Final result: {guardrailResult}");

Console.WriteLine("===================================================================== guardrail 2: no answer before the method is done");
guardrailResult = await AgentLoop.RunLoopAsync(
    GuardrailScripts.AnswersEarlyThenCompletes(), client, toolDefinitions, "Investigate the test Mid_is_positive.", 12,
    nextMissingStep: InvestigationMethod.NextMissingStep);
Console.WriteLine($"Final result: {guardrailResult}");

Console.WriteLine("===================================================================== guardrail 2: the nudge limit");
guardrailResult = await AgentLoop.RunLoopAsync(
    GuardrailScripts.Stubborn(), client, toolDefinitions, "Investigate the test Mid_is_positive.", 12,
    nextMissingStep: InvestigationMethod.NextMissingStep);
Console.WriteLine($"Final result: {guardrailResult}");

Console.WriteLine("===================================================================== guardrail 3: ungrounded evidence");
guardrailResult = await AgentLoop.RunLoopAsync(
    GuardrailScripts.InventedEvidence(), client, toolDefinitions, "Investigate the test Mid_is_positive.", 5);
Console.WriteLine($"Final result: {guardrailResult}");

Console.WriteLine("===================================================================== guardrail 2: a failed step does not count");
guardrailResult = await AgentLoop.RunLoopAsync(
    GuardrailScripts.ReadFailsThenRetries(), client, toolDefinitions, "Investigate the test Mid_is_positive.", 12,
    nextMissingStep: InvestigationMethod.NextMissingStep,
    answerProblem: InvestigationMethod.AnswerProblem);
Console.WriteLine($"Final result: {guardrailResult}");

Console.WriteLine("===================================================================== guardrail 2: the answer needs a Verdict");
guardrailResult = await AgentLoop.RunLoopAsync(
    GuardrailScripts.AnswerWithoutVerdict(), client, toolDefinitions, "Investigate the test Mid_is_positive.", 12,
    nextMissingStep: InvestigationMethod.NextMissingStep,
    answerProblem: InvestigationMethod.AnswerProblem);
Console.WriteLine($"Final result: {guardrailResult}");

Console.WriteLine("===================================================================== guardrail 3: relabelled failures");
guardrailResult = await AgentLoop.RunLoopAsync(
    GuardrailScripts.RelabelledFailure(), client, toolDefinitions, "Investigate the test Mid_is_positive.", 5);
Console.WriteLine($"Final result: {guardrailResult}");

Console.WriteLine("===================================================================== check 4: verdict against the measurements");
guardrailResult = await AgentLoop.RunLoopAsync(
    GuardrailScripts.VerdictContradictsMeasured(), client, toolDefinitions, "Investigate the test Mid_is_positive.", 5);
Console.WriteLine($"Final result: {guardrailResult}");

Console.WriteLine("===================================================================== the loop states the measured rates");
guardrailResult = await AgentLoop.RunLoopAsync(
    GuardrailScripts.VerdictContradictsMeasured(), client, toolDefinitions, "Investigate the test Mid_is_positive.", 5,
    stateMeasuredRates: true);
Console.WriteLine($"Final result: {guardrailResult}");
*/
#endregion

#region 10. The fix phase: edit the source, run it again, see if it passes
// WHAT:     A second loop run, after an investigation. The model reads the test, proposes one edit (shown as a dry
//           run first), applies it, and the loop makes it run the test alone and the whole suite 20 times each.
//           FixMethod enforces that order. Verification decides whether the fix is proven, not the model:
//           the run ends with the MEASURED figures and an UNVERIFIED line if an applied edit is not proven.
// RUN IT:   Remove the /* */, and comment out region 7 so it does not also run. Needs Ollama running. Region 9 and 11 are
//           already commented out; leave them so. To give the model something to fix, invert the assertion in
//           Mid_is_positive by hand first (Is.GreaterThan(0) to Is.LessThan(0)).
// WARNING:  An applied edit really changes the file under sandbox/. Put it back afterwards:
//             git checkout -- sandbox/
//           The sandbox is the test bed for every scored run, so never leave an edit in place.
// NOTE:     run_test_n_times builds the sandbox once per call, so the 20 runs test the edited code.

/*
var fixHttp = new HttpClient { BaseAddress = new Uri("http://localhost:11434"), Timeout = TimeSpan.FromMinutes(5) };
var fixModel = new OllamaModel(fixHttp, "qwen2.5:3b", 8192);
var fixTarget = "Mid_is_positive";   // the test to fix
var fixResult = await AgentLoop.RunLoopAsync(
    fixModel, client, toolDefinitions,
    $"Fix the test {fixTarget}. Its source is in sandbox/FlakyLab/FlakyLabTests.cs, lines 62 to 67.",
    16, Prompts.FixV1,
    nextMissingStep: calls => FixMethod.NextMissingStep(calls, fixTarget),
    targetTest: fixTarget);
Console.WriteLine($"Final result: {fixResult}");

*/
#endregion

#region 11. The fix phase with a scripted model: break the control, repair it, prove it
// WHAT:     The whole fix phase end to end with no LLM, so the result is deterministic. The region first breaks
//           Mid_is_positive on purpose (inverts its assertion) using the same edit_source tool the agent uses.
//           A scripted model then repairs it: dry run, one applied edit, 20 runs of the test alone and 20 of the
//           whole suite. FixMethod enforces that order, and Verification decides whether the repair is proven.
// RUN IT:   Remove the /* */, and comment out regions 7, 9 and 10 so they do not also run. Needs only the solution built.
// EXPECTED: ... the 40 runs, then the answer, then "MEASURED Mid_is_positive: alone 0/20 failed; in the suite 0/20
//           failed" and no UNVERIFIED line. The repair itself puts the sandbox back, so nothing is left behind.
// IF IT STOPS EARLY: the control may still be broken. Put it back with:  git checkout -- sandbox/
// LESSON:   This proves the pipeline (edit, rebuild, 20 + 20 runs, verification) works. Whether a real model can
//           produce the edit is a separate question: in region 10 the 3B model could not copy the source exactly.
/*
var breakResult = await client.CallToolAsync("edit_source", new Dictionary<string, object?>
{
    ["path"] = "sandbox/FlakyLab/FlakyLabTests.cs",
    ["oldText"] = GuardrailScripts.OriginalAssertion,
    ["newText"] = GuardrailScripts.BrokenAssertion,
    ["apply"] = true
});
Console.WriteLine("Control broken on purpose: " + string.Join("", breakResult.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(b => b.Text)));

var repairResult = await AgentLoop.RunLoopAsync(
    GuardrailScripts.RepairBrokenControl(), client, toolDefinitions,
    "Fix the test Mid_is_positive.", 12, Prompts.FixV1,
    nextMissingStep: calls => FixMethod.NextMissingStep(calls, "Mid_is_positive"),
    stateMeasuredRates: true,
    targetTest: "Mid_is_positive");
Console.WriteLine($"Final result: {repairResult}");
*/
#endregion

#region 12. Investigate, then fix: the whole chain on a real flaky test (issue #42)
// WHAT:     Two loop runs in a row. The investigation (prompt V3) examines the test; FixTask turns its answer into the
//           fix run's task: the file and lines come from what find_test_source really returned (never from the
//           model's words), and the model's suspected cause is passed on labelled as possibly wrong. Then the fix run
//           (FixV1, FixMethod) edits the sandbox and the loop makes it run 20 + 20 verification runs.
// RUN IT:   Remove the /* */, comment out region 7 so it does not also run, and have Ollama running. To try a bigger
//           model change only the name below (after `ollama pull qwen2.5:7b`): one change at a time.
// WARNING:  A fix run really edits the file under sandbox/. Afterwards, ALWAYS restore it:
//             git checkout -- sandbox/
//           and read the diff the run printed: a test that passes 20 times because its check was weakened is not fixed.
// OUTPUT:   The investigation log, then "Fix task: ...", then the fix run's log, ending with MEASURED (and UNVERIFIED
//           if an applied edit is not proven). "Hand-off failed" means no fix was attempted.
/*
var e2eTest = "Reset_clears_cache";
var e2eModelName = "qwen2.5:3b";
var e2eHttp = new HttpClient { BaseAddress = new Uri("http://localhost:11434"), Timeout = TimeSpan.FromMinutes(10) };
var e2eModel = new OllamaModel(e2eHttp, e2eModelName, 8192);

var investigationCalls = new List<ToolExecution>();
var investigation = await AgentLoop.RunLoopAsync(
    e2eModel, client, toolDefinitions, $"Investigate the test {e2eTest}.", 12, Prompts.InvestigateV3,
    nextMissingStep: InvestigationMethod.NextMissingStep,
    answerProblem: InvestigationMethod.AnswerProblem,
    stateMeasuredRates: true,
    executionLog: investigationCalls);

var fixTask = FixTask.From(investigationCalls, investigation.Text, e2eTest);
if (fixTask == null)
{
    Console.WriteLine("Hand-off failed: find_test_source did not give one place for the test, so no fix was attempted.");
}
else
{
    Console.WriteLine($"Fix task: {fixTask}");
    var fixCalls = new List<ToolExecution>();
    var fixRun = await AgentLoop.RunLoopAsync(
        e2eModel, client, toolDefinitions, fixTask, 16, Prompts.FixV1,
        nextMissingStep: calls => FixMethod.NextMissingStep(calls, e2eTest),
        targetTest: e2eTest,
        executionLog: fixCalls);
    Console.WriteLine($"Final result: {fixRun}");
}
*/
#endregion
