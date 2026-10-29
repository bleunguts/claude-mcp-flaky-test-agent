using System;
using System.Collections.Generic;
using System.Text;

namespace FlakyDetective.Agent;

public static class Script
{
    public static ModelResponse CallTool(string id, string name, Dictionary<string, object?> input) =>
        new([new ToolUseBlock(id, name, input)], StopReason.ToolUse);

    public static ModelResponse Finish(string text) => new([new TextBlock(text)], StopReason.ToolUse);
}