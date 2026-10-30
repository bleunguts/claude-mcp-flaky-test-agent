using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlakyDetective.Agent;

public class OllamaModel(HttpClient http, string modelName, int numCtx = 8192) : IModel
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public async Task<ModelResponse> CompleteAsync(
        List<Message> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken = default)
    {
        var request = new OllamaChatRequest(
            modelName, ToOllamaMessages(messages), ToOllamaTools(tools), false, new OllamaOptions(numCtx));

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await http.PostAsJsonAsync("/api/chat", request, JsonOptions, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Could not reach Ollama at {http.BaseAddress}. Is it running?", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Ollama did not answer in time.", ex);
        }

        if (!httpResponse.IsSuccessStatusCode)
        {
            // for example 404 {"error":"model 'x' not found"}
            var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Ollama returned {(int)httpResponse.StatusCode}: {body}");
        }

        var reply = await httpResponse.Content.ReadFromJsonAsync<OllamaChatResponse>(JsonOptions, cancellationToken)
                    ?? throw new InvalidOperationException("Ollama returned an empty response.");

        return ToModelResponse(reply.Message);
    }

    public static List<OllamaTool>? ToOllamaTools(IReadOnlyList<ToolDefinition> tools) =>
        tools.Count == 0
            ? null
            : tools.Select(t => new OllamaTool("function",
                  new OllamaFunctionDefinition(t.Name, t.Description, t.InputSchema))).ToList();

    public static List<OllamaMessage> ToOllamaMessages(List<Message> history)
    {
        // a tool result only knows the id of its request, but Ollama wants the tool's name
        var toolNames = history.SelectMany(m => m.Content).OfType<ToolUseBlock>()
                               .ToDictionary(b => b.Id, b => b.Name);
        var result = new List<OllamaMessage>();

        foreach (var message in history)
        {
            var text = string.Join("\n", message.Content.OfType<TextBlock>().Select(b => b.Text));

            if (message.Role == "assistant")
            {
                var toolCalls = message.Content.OfType<ToolUseBlock>()
                    .Select(b => new OllamaToolCall(b.Id, new OllamaFunctionCall(b.Name, ToJsonArguments(b.Input))))
                    .ToList();
                result.Add(new OllamaMessage("assistant", text, toolCalls.Count > 0 ? toolCalls : null));
                continue;
            }

            // a user message carries tool results (one Ollama message each) and/or plain text
            foreach (var toolResult in message.Content.OfType<ToolResultBlock>())
            {
                toolNames.TryGetValue(toolResult.ToolUseId, out var toolName);
                result.Add(new OllamaMessage("tool", toolResult.Content, ToolName: toolName));
            }
            if (text.Length > 0)
            {
                result.Add(new OllamaMessage("user", text));
            }
        }

        return result;
    }

    public static ModelResponse ToModelResponse(OllamaMessage message)
    {
        var blocks = new List<ContentBlock>();

        if (!string.IsNullOrEmpty(message.Content))
        {
            blocks.Add(new TextBlock(message.Content));
        }

        foreach (var call in message.ToolCalls ?? [])
        {
            var id = string.IsNullOrEmpty(call.Id) ? "call_" + Guid.NewGuid().ToString("N")[..8] : call.Id;
            blocks.Add(new ToolUseBlock(id, call.Function.Name, ToInputDictionary(call.Function.Arguments)));
        }

        if (blocks.Count == 0)
        {
            blocks.Add(new TextBlock(string.Empty));
        }

        // done_reason is "stop" even when a tool is requested, so look at the tool calls instead
        var stopReason = blocks.OfType<ToolUseBlock>().Any() ? StopReason.ToolUse : StopReason.EndTurn;
        return new ModelResponse(blocks, stopReason);
    }

    static Dictionary<string, JsonElement> ToJsonArguments(Dictionary<string, object?> input) =>
        input.ToDictionary(kv => kv.Key, kv => JsonSerializer.SerializeToElement(kv.Value));

    static Dictionary<string, object?> ToInputDictionary(Dictionary<string, JsonElement>? arguments) =>
        (arguments ?? new()).ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
}