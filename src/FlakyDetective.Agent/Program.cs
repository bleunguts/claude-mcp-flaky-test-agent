using FlakyDetective.Agent;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

Console.WriteLine("Hello, World! from the FlakyDetective.Agent");
var root = Helpers.FindSlnFolderRecursively(AppContext.BaseDirectory);   
Console.WriteLine($"Found solution folder: {root}");

var serverDll = Path.Combine(root, "src", "FlakyDetective.McpServer", "bin", "Debug", "net10.0", "FlakyDetective.McpServer.dll");
Console.WriteLine($"Starting MCP server from: {serverDll}");
Console.WriteLine($"File exists: {File.Exists(serverDll)}");

Console.WriteLine("=====================================================================> setup");
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

Console.WriteLine("=====================================================================> tools listed");

// call ping
PrintToolResult(await client.CallToolAsync("ping"));
Console.WriteLine("=====================================================================");

// call find_test_source with arg 
PrintToolResult(await client.CallToolAsync(
    "find_test_source",
    new Dictionary<string, object>
    {
        { "testName", "Reset_clears_cache" }
    }));
Console.WriteLine("=====================================================================");

PrintToolResult(await client.CallToolAsync("read_source",
    new Dictionary<string, object?>
    {
        ["path"] = "sandbox/FlakyLab/FlakyLabTests.cs",
        ["startLine"] = 83,
        ["endLine"] = 92,
    }));
Console.WriteLine("=====================================================================");
PrintToolResult(await client.CallToolAsync("read_source",
    new Dictionary<string, object?>
    {
        ["path"] = ".env",
        ["startLine"] = 83,
        ["endLine"] = 92,
    }));

PrintToolResult(await client.CallToolAsync("read_source",
    new Dictionary<string, object?>
    {
        ["path"] = "",
        ["startLine"] = "44",
        ["endLine"] = 92,
    }));

var conversation = new List<Message>
{
    new Message("user", [new TextBlock("Where is the test Reset_clears_cache?")]),
    new Message("assistant", [new ToolUseBlock("call_1", "find_test_source", new Dictionary<string, object?> { { "testName", "Reset_clears_cache" } })]),
    
    new Message("user", [new ToolResultBlock("call_1", "{\"testName\":\"Reset_clears_cache\",\"matches\":[{\"file\":\"sandbox/FlakyLab/FlakyLabTests.cs\",\"startLine\":83,\"endLine\":89}]}", false)]),
    new Message("assistant", [new TextBlock("It is in sandbox/FlakyLab/FlakyLabTests.cs, lines 83 to 89.")]),
};

foreach (var message in conversation)
{
    Console.WriteLine($"Role: {message.Role}, Kinds: {string.Join(", ", message.Content.Select(b => b.GetType().Name))}");
    foreach (var block in message.Content)
    {
        switch (block)
        {
            case TextBlock textBlock:
                Console.WriteLine($"Text: {textBlock.Text}");
                break;
            case ToolUseBlock toolUseBlock:
                Console.WriteLine($"Tool Use: {toolUseBlock.Name}, Id: {toolUseBlock.Id}, Input: {string.Join(", ", toolUseBlock.Input.Select(kv => $"{kv.Key}={kv.Value}"))}");
                break;
            case ToolResultBlock toolResultBlock:
                Console.WriteLine($"Tool Result: {toolResultBlock.ToolUseId}, Content: {toolResultBlock.Content}, IsError: {toolResultBlock.IsError}");
                break;
        }
    }
}

static void PrintToolResult(CallToolResult result2)
{
    foreach (var block in result2.Content.OfType<TextContentBlock>())
    {
        Console.WriteLine($"Tool Result: {block.Text}. Type:{block.Type}");
    }
}