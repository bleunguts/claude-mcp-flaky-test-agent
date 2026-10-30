using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlakyDetective.Agent;

public record OllamaChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] List<OllamaMessage> Messages,
    [property: JsonPropertyName("tools")] List<OllamaTool>? Tools,
    [property: JsonPropertyName("stream")] bool Stream,
    [property: JsonPropertyName("options")] OllamaOptions? Options);

public record OllamaOptions(
    [property: JsonPropertyName("num_ctx")] int NumCtx);

public record OllamaMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content,
    [property: JsonPropertyName("tool_calls")] List<OllamaToolCall>? ToolCalls = null,
    [property: JsonPropertyName("tool_name")] string? ToolName = null);

public record OllamaToolCall(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("function")] OllamaFunctionCall Function);

public record OllamaFunctionCall(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("arguments")] Dictionary<string, JsonElement> Arguments);

public record OllamaTool(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("function")] OllamaFunctionDefinition Function);

public record OllamaFunctionDefinition(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("parameters")] JsonElement Parameters);

public record OllamaChatResponse(
    [property: JsonPropertyName("message")] OllamaMessage Message,
    [property: JsonPropertyName("done")] bool Done,
    [property: JsonPropertyName("done_reason")] string? DoneReason);
