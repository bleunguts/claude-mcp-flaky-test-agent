using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text;

namespace FlakyDetective.Agent;

public record AgentResult(string Text, bool HitIterationCap, int Iterations);

public class AgentLoop
{
    public static async Task<AgentResult> RunLoopAsync(
        IModel model,
        McpClient client,
        IReadOnlyList<ToolDefinition> tools,
        string userPrompt,
        int maxIterations)
    {
        var messages = new List<Message>
        {
            new
            (
                Role: "user",
                Content: [new TextBlock(userPrompt)]
            )
        };

        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            Log.Iteration(iteration, maxIterations);
            var response = await model.CompleteAsync(messages, tools);
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
                return new AgentResult(text, false, iteration);
            }
            var toolResults = new List<ContentBlock>();
            foreach (var toolUse in response.Content.OfType<ToolUseBlock>())
            {
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
                }
                catch (Exception exp)
                {
                    ToolResultBlock toolResult = new(toolUse.Id, $"Tool call failed. {exp.Message}", true);
                    toolResults.Add(toolResult);
                    Log.PrintToolResult(toolResult);
                }
            }
            messages.Add(new Message("user", toolResults));
        }
        Log.LoopStopped(LoopStopReason.MaxIterationsReached, maxIterations);
        return new AgentResult($"Maximum iterations reached without a successful completion. {userPrompt}", true, maxIterations);
    }
}