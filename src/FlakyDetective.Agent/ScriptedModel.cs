using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System;
using System.Collections.Generic;
using System.Text;
using static System.Net.Mime.MediaTypeNames;

namespace FlakyDetective.Agent;
public interface IModel
{
    Task<ModelResponse> CompleteAsync(List<Message> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken = default);
}
public class ScriptedModel(IEnumerable<Func<List<Message>, ModelResponse>> steps) : IModel
{
    private readonly Queue<Func<List<Message>, ModelResponse>> _steps = new(steps);
    public Task<ModelResponse> CompleteAsync(List<Message> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken = default)
    {
        if(_steps.Count == 0)
        {
            throw new InvalidOperationException("The script has run out of steps.");
        }
        var nextFunctor = _steps.Dequeue();
        return Task.FromResult(nextFunctor(messages));
    }

    public static async Task<string> RunLoopAsync(
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
            var response = await model.CompleteAsync(messages, tools);
            messages.Add(new Message
            (
                Role: "assistant",
                Content: response.Content
            ));
            
            if(response.StopReason != StopReason.ToolUse)
            {
                var message = messages.Last(); 
                var text = message.Content.OfType<TextBlock>().FirstOrDefault()?.Text ?? string.Empty;
                return text;
            }

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
                    foreach(var content in toolResponse.Content.OfType<TextContentBlock>())
                    {
                        append.AppendLine(content.Text);
                    }
                    var toolResult = new ToolResultBlock(toolUse.Id, append.ToString(), toolResponse.IsError == true);
                    messages.Add(new Message("user", [toolResult]));
                } 
                catch(Exception exp)
                {
                    messages.Add(new Message("user", [new ToolResultBlock(toolUse.Id, $"Tool call failed. {exp.Message}", true)]));
                }     
            }
        }

        return $"Maximum iterations reached without a successful completion. {userPrompt} {maxIterations}";
    }
}
