using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text;

namespace FlakyDetective.Agent;

public record AgentResult(string Text, bool HitIterationCap, int Iterations);

public class AgentLoop
{
    private const string SkippedMessage =
        "Skipped: call one tool at a time. Call this tool again after you have seen the result of the previous call.";

    public static async Task<AgentResult> RunLoopAsync(
        IModel model,
        McpClient client,
        IReadOnlyList<ToolDefinition> tools,
        string userPrompt,
        int maxIterations,
        string? systemPrompt = null)
    {
        var messages = new List<Message>
        {
            new
            (
                Role: "user",
                Content: [new TextBlock(userPrompt)]
            )
        };

        var toolsCalled = new List<string>();
        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            Log.Iteration(iteration, maxIterations);
            var response = await model.CompleteAsync(messages, tools, systemPrompt);
            messages.Add(new Message
            (
                Role: "assistant",
                Content: response.Content
            ));
            Log.PrintModelResponse(response);

            if (response.StopReason != StopReason.ToolUse)
            {
                var message = messages.Last();
                var text = message.Content.OfType<TextBlock>().FirstOrDefault()?.Text ?? string.Empty;
                Log.LoopStopped(LoopStopReason.EndTurn, iteration);
                Log.ToolsCalled(toolsCalled);
                return new AgentResult(text, false, iteration);
            }
            var toolResults = new List<ContentBlock>();
            var firstToolCall = true;
            foreach (var toolUse in response.Content.OfType<ToolUseBlock>())
            {
                // Guardrail 1: one tool per turn. The prompt asks for this and the model ignores it,
                // so the loop enforces it. Only the first call in a reply runs. Every other call still
                // gets a tool_result, because the API needs one for every tool_use, but it is an error
                // that tells the model to call it again once it has seen the first result.
                if (!firstToolCall)
                {
                    var skipped = new ToolResultBlock(toolUse.Id, SkippedMessage, true);
                    toolResults.Add(skipped);
                    toolsCalled.Add($"{Describe(toolUse, false)} [skipped]");
                    Log.PrintToolResult(skipped);
                    continue;
                }
                firstToolCall = false;

                try
                {
                    var tool = tools.FirstOrDefault(t => t.Name == toolUse.Name);
                    if (tool == null)
                    {
                        throw new Exception($"Tool {toolUse.Name} not found.");
                    }

                    var append = new StringBuilder();                    
                    var toolResponse = await client.CallToolAsync(toolUse.Name, toolUse.Input, cancellationToken: CancellationToken.None);
                    foreach (var content in toolResponse.Content.OfType<TextContentBlock>())
                    {
                        append.AppendLine(content.Text);
                    }
                    var toolResult = new ToolResultBlock(toolUse.Id, append.ToString(), toolResponse.IsError == true);
                    Log.PrintToolResult(toolResult);
                    toolResults.Add(toolResult);
                    toolsCalled.Add(Describe(toolUse, toolResult.IsError));
                }
                catch (Exception exp)
                {
                    ToolResultBlock toolResult = new(toolUse.Id, $"Tool call failed. {exp.Message}", true);
                    toolResults.Add(toolResult);
                    toolsCalled.Add(Describe(toolUse, toolResult.IsError));
                    Log.PrintToolResult(toolResult);
                }
            }
            messages.Add(new Message("user", toolResults));
        }
        Log.LoopStopped(LoopStopReason.MaxIterationsReached, maxIterations);
        Log.ToolsCalled(toolsCalled);
        return new AgentResult($"Maximum iterations reached without a successful completion. {userPrompt}", true, maxIterations);
    }

    private static string Describe(ToolUseBlock toolUse, bool isError)
    {
        var args = string.Join(", ", toolUse.Input.Select(kv => $"{kv.Key}={kv.Value}"));
        return $"{toolUse.Name}({args}){(isError ? " [error]" : "")}";
    }
}