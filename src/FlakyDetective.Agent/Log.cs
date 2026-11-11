using ModelContextProtocol.Protocol;
using System;
using System.Collections.Generic;
using System.Text;

namespace FlakyDetective.Agent;
public enum LoopStopReason { MaxIterationsReached, EndTurn };
public class Log
{
    public static void PrintToolResult(CallToolResult result2)
    {
        foreach (var block in result2.Content.OfType<TextContentBlock>())
        {
            Console.WriteLine($"Tool Result: {block.Text}. Type:{block.Type}");
        }
    }

    public static void PrintToolResult(ToolResultBlock toolResult)
    {
        Console.WriteLine($"Tool Result: {toolResult.ToolUseId}, Content: {toolResult.Content}, IsError: {toolResult.IsError}");
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
            case ThinkingBlock thinkingBlock:
                Console.WriteLine($"Thinking: ({thinkingBlock.Thinking.Length} chars shown, signature kept)");
                break;
            case RedactedThinkingBlock:
                Console.WriteLine("Thinking: (redacted, kept)");
                break;
            case ToolUseBlock toolUseBlock:
                Console.WriteLine($"Tool Use: {toolUseBlock.Name}, Id: {toolUseBlock.Id}, Input: {string.Join(", ", toolUseBlock.Input.Select(kv => $"{kv.Key}={kv.Value}"))}");
                break;
            case ToolResultBlock toolResultBlock:
                Console.WriteLine($"Tool Result: {toolResultBlock.ToolUseId}, Content: {toolResultBlock.Content}, IsError: {toolResultBlock.IsError}");
                break;
        }
    }

    public static void Iteration(int iteration, int maxIterations)
    {
        Console.WriteLine($"Iteration {iteration}/{maxIterations}");
    }

    public static void LoopStopped(LoopStopReason reason, int iterations)
    {
        var reasonText = reason switch
        {
            LoopStopReason.MaxIterationsReached => $"Stopped: iteration cap reached after {iterations} iterations",
            LoopStopReason.EndTurn => $"Stopped: end_turn after {iterations} iterations",
            _ => "Unknown reason"
        };
        Console.WriteLine(reasonText);
    }

    // What the agent really called, in order. Compare every claim in the final answer with this line.
    public static void ToolsCalled(IReadOnlyList<string> toolsCalled)
    {
        Console.WriteLine(toolsCalled.Count == 0
            ? "Tools called: none"
            : $"Tools called ({toolsCalled.Count}): {string.Join("; ", toolsCalled)}");
    }

    public static void Nudge(int number, int max, string missing)
    {
        Console.WriteLine($"Nudge {number}/{max}: answer not accepted yet. {missing}");
    }

    // Printed only when the loop gave up with the method unfinished, so such a run cannot pass as clean.
    public static void Incomplete(string? missing)
    {
        if (missing != null)
        {
            Console.WriteLine($"INCOMPLETE: the method was not finished. {missing}");
        }
    }

    public static void Ungrounded(IReadOnlyList<string> problems)
    {
        foreach (var problem in problems)
        {
            Console.WriteLine($"UNGROUNDED: {problem}");
        }
    }
}
