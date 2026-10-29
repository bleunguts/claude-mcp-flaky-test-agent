using ModelContextProtocol.Protocol;
using System;
using System.Collections.Generic;
using System.Text;

namespace FlakyDetective.Agent;

public class Log
{
    public static void PrintToolResult(CallToolResult result2)
    {
        foreach (var block in result2.Content.OfType<TextContentBlock>())
        {
            Console.WriteLine($"Tool Result: {block.Text}. Type:{block.Type}");
        }
    }
    public static void PrintModelResponse(ModelResponse modelResponse1)
    {
        foreach (var block in modelResponse1.Content)
        {
            Print(block);
        }
    }

    public static void Print(ContentBlock block)
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
