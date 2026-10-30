using System;
using System.Collections.Generic;
using System.Text;

namespace FlakyDetective.Agent;

public abstract record ContentBlock;
public record TextBlock(string Text) : ContentBlock;
public record ToolUseBlock(string Id, string Name, Dictionary<string, object?> Input) : ContentBlock;
public record ToolResultBlock(string ToolUseId, string Content, bool IsError) : ContentBlock;

// Claude's reasoning. The API requires these blocks (with their signatures) to be passed back
// unchanged on the next turn, so the loop keeps them in the history. The text may be empty
// because the default display is "omitted"; the signature is what matters.
// The Ollama adapter and the loop simply ignore them.
public record ThinkingBlock(string Thinking, string Signature) : ContentBlock;
public record RedactedThinkingBlock(string Data) : ContentBlock;

public record Message(string Role, List<ContentBlock> Content);
public enum StopReason { EndTurn, ToolUse }
public record ModelResponse(List<ContentBlock> Content, StopReason StopReason);

public record ToolDefinition(
    string Name,
    string? Description,
    System.Text.Json.JsonElement InputSchema    
);