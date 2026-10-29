using System;
using System.Collections.Generic;
using System.Text;

namespace FlakyDetective.Agent;

public abstract record ContentBlock;
public record TextBlock(string Text) : ContentBlock;
public record ToolUseBlock(string Id, string Name, Dictionary<string, object?> Input) : ContentBlock;
public record ToolResultBlock(string ToolUseId, string Content, bool IsError) : ContentBlock;
public record Message(string Role, List<ContentBlock> Content);
public enum StopReason { EndTurn, ToolUse }
public record ModelResponse(List<ContentBlock> Content, StopReason StopReason);

public record ToolDefinition(
    string Name,
    string? Description,
    System.Text.Json.JsonElement InputSchema    
);