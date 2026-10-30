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
// SWITCH:   Change `livePrompt` to try the other demo: "Use the ping tool and tell me what it returned."
// LESSON:   With qwen2.5:3b the model chained find_test_source then read_source on its own, which
//           is the whole point of the loop. Small models also pad their answers.
// OUTPUT:   Iteration log, then "Stopped: end_turn after 3 iterations" and the final AgentResult.
Console.WriteLine("===================================================================== using Agent Loop");
var liveHttp = new HttpClient { BaseAddress = new Uri("http://localhost:11434"), Timeout = TimeSpan.FromMinutes(5) };
var liveModel = new OllamaModel(liveHttp, "qwen2.5:3b", 8192);

// string livePrompt = "Use the ping tool and tell me what it returned.";
string livePrompt = "Find and read the Reset_clears_cache test";

var liveResult = await AgentLoop.RunLoopAsync(liveModel, client, toolDefinitions, livePrompt, 8);
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

var claudeResult = await AgentLoop.RunLoopAsync(claudeModel, client, toolDefinitions, claudePrompt, 8);
Console.WriteLine($"Final result: {claudeResult}");
*/
#endregion


