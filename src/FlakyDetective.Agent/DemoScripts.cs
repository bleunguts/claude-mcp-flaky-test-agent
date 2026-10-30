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
}

// Asks for ping on every call, with a fresh id each time.
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
