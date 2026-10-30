using FlakyDetective.Agent;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks.Dataflow;

Console.WriteLine("Hello, World! from the FlakyDetective.Agent");
var root = Helpers.FindSlnFolderRecursively(AppContext.BaseDirectory);   
Console.WriteLine($"Found solution folder: {root}");

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
// list tools
await using var client = await McpClient.CreateAsync(transport);
var tools = await client.ListToolsAsync();
var toolDefinitions = tools
    .Select(t => new ToolDefinition(t.Name, t.Description, t.JsonSchema))
    .ToList();
foreach (var tool in toolDefinitions)
{
    Console.WriteLine($"Tool: {tool.Name} - {tool.Description} - {tool.InputSchema}");
}

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

#region building blocks for the scripted model and agent loop tests
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

var script = CreateDemoScript();    
for(int i = 0; i < 4; i++)
{
    try
    {
        var response = await script.CompleteAsync(conversation, toolDefinitions);
        Console.WriteLine($"Script completed with stop reason: {response.StopReason}");
        Log.PrintModelResponse(response);
    }
    catch (InvalidOperationException ex)
    {
        Console.WriteLine($"Script failed with exception: {ex.Message}");
    }
}
*/
#endregion 

#region the agent loop
/* the agent loop
Console.WriteLine("===================================================================== using Agent Loop");
string prompt = "Find and read the Reset_clears_cache test";
var result = await AgentLoop.RunLoopAsync(CreateDemoScript(), client, toolDefinitions, prompt, 10);
Console.WriteLine($"Final result: {result}");
*/
#endregion

#region error handling tests
/*
Console.WriteLine("===================================================================== error handling"); 
result = await AgentLoop.RunLoopAsync(RunawayModel(), client, toolDefinitions, prompt, 5);
Console.WriteLine($"Final result: {result}");

result = await AgentLoop.RunLoopAsync(UnknownTool(), client, toolDefinitions, prompt, 5);
Console.WriteLine($"Final result: {result}");

result = await AgentLoop.RunLoopAsync(DeniedFile(), client, toolDefinitions, prompt, 5);
Console.WriteLine($"Final result: {result}");

result = await AgentLoop.RunLoopAsync(EscapeAttempt(), client, toolDefinitions, prompt, 5);
Console.WriteLine($"Final result: {result}");

result = await AgentLoop.RunLoopAsync(BadArgument(), client, toolDefinitions, prompt, 5);
Console.WriteLine($"Final result: {result}");
*/
#endregion

#region OllamaChatRequest tests
/*
Console.WriteLine("===================================================================== ollama chat requests");
var http = new HttpClient { BaseAddress = new Uri("http://localhost:11434"), Timeout = TimeSpan.FromMinutes(5) };
var jsonOptions = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

var request = new OllamaChatRequest(
    "qwen2.5:3b",
    [new OllamaMessage("user", "Say hello in five words.")],
    null, false, new OllamaOptions(8192));
var httpResponse = await http.PostAsJsonAsync("/api/chat", request, jsonOptions);
httpResponse.EnsureSuccessStatusCode();
var reply = await httpResponse.Content.ReadFromJsonAsync<OllamaChatResponse>();
Console.WriteLine(reply!.Message.Content);

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

var toolHttpResponse = await http.PostAsJsonAsync("/api/chat", toolRequest, jsonOptions);
toolHttpResponse.EnsureSuccessStatusCode();
var toolReply = await toolHttpResponse.Content.ReadFromJsonAsync<OllamaChatResponse>();
foreach (var call in toolReply!.Message.ToolCalls ?? [])
{
    Console.WriteLine($"Tool call: {call.Function.Name}, id: {call.Id}");
}
*/
#endregion

Console.WriteLine("===================================================================== using Agent Loop");
var http = new HttpClient { BaseAddress = new Uri("http://localhost:11434"), Timeout = TimeSpan.FromMinutes(5) };
var model = new OllamaModel(http, "qwen2.5:3b", 8192);

/* string prompt = "Use the ping tool and tell me what it returned."; */
string prompt = "Find and read the Reset_clears_cache test";

var result = await AgentLoop.RunLoopAsync(model, client, toolDefinitions, prompt, 8);
Console.WriteLine($"Final result: {result}");

static ScriptedModel CreateDemoScript() => new ScriptedModel([
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

static ScriptedModel UnknownTool() => new ScriptedModel([
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

static ScriptedModel DeniedFile() => new ScriptedModel([
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

static ScriptedModel EscapeAttempt() => new ScriptedModel([
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

static ScriptedModel BadArgument() => new ScriptedModel([
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

static IModel RunawayModel() => new RunawayModel();
public class RunawayModel : IModel
{
    private int _calls;

    public Task<ModelResponse> CompleteAsync(List<Message> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken = default)
    {
        _calls++;
        Console.WriteLine($"RunawayModel: asking for ping again (call {_calls})");
        return Task.FromResult(Script.CallTool($"call_{_calls}", "ping", []));
    }
}