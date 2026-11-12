using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text;

namespace FlakyDetective.Agent;

public record AgentResult(string Text, bool HitIterationCap, int Iterations, bool Incomplete = false);

public class AgentLoop
{
    private const string SkippedMessage =
        "Skipped: call one tool at a time. Call this tool again after you have seen the result of the previous call.";

    // The loop is the same for every model. What it enforces beyond that is optional:
    //   systemPrompt     standing instructions, sent on every turn
    //   nextMissingStep  guardrail 2: names the next step still to do, or null when the method is complete.
    //                    An answer that arrives early is not accepted; the model is nudged instead.
    //   answerProblem    guardrail 2: names what is wrong with the answer itself (for example no Verdict
    //                    line), or null when it is fine. A malformed answer is nudged in the same way.
    //   maxNudges        how many times to nudge before giving up and flagging the run as incomplete
    //   stateMeasuredRates  the loop adds the figures the tools measured to the end of the answer itself, so the
    //                    model does not have to copy them (it copied the suite figure well and invented the
    //                    single-test one). Use with a prompt that does not ask the model for failure rates.
    public static async Task<AgentResult> RunLoopAsync(
        IModel model,
        McpClient client,
        IReadOnlyList<ToolDefinition> tools,
        string userPrompt,
        int maxIterations,
        string? systemPrompt = null,
        Func<IReadOnlyList<ToolExecution>, string?>? nextMissingStep = null,
        Func<string, string?>? answerProblem = null,
        int maxNudges = 3,
        bool stateMeasuredRates = false)
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
        var executions = new List<ToolExecution>();
        var nudges = 0;

        // What is still wrong or missing when the model offers an answer: first the method, then the answer.
        string? Outstanding(string answer) => nextMissingStep?.Invoke(executions) ?? answerProblem?.Invoke(answer);

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

                // Guardrail 2: no answer before the method is done. The model has already written its
                // answer, which stays in the history, and gets a short user message naming what is missing.
                // Roles still alternate (assistant answer, then user nudge). The nudge limit and the
                // iteration cap both stop this from looping forever.
                var missing = Outstanding(text);
                if (missing != null && nudges < maxNudges && iteration < maxIterations)
                {
                    nudges++;
                    Log.Nudge(nudges, maxNudges, missing);
                    messages.Add(new Message("user", [new TextBlock($"You have not finished. {missing}")]));
                    continue;
                }

                return Stop(LoopStopReason.EndTurn, iteration, text, false, toolsCalled, executions, tools, Outstanding, stateMeasuredRates);
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
                    executions.Add(new ToolExecution(toolUse.Name, toolUse.Input, toolResult.Content, toolResult.IsError));
                }
                catch (Exception exp)
                {
                    ToolResultBlock toolResult = new(toolUse.Id, $"Tool call failed. {exp.Message}", true);
                    toolResults.Add(toolResult);
                    toolsCalled.Add(Describe(toolUse, toolResult.IsError));
                    executions.Add(new ToolExecution(toolUse.Name, toolUse.Input, toolResult.Content, true));
                    Log.PrintToolResult(toolResult);
                }
            }
            messages.Add(new Message("user", toolResults));
        }

        return Stop(
            LoopStopReason.MaxIterationsReached, maxIterations,
            $"Maximum iterations reached without a successful completion. {userPrompt}", true,
            toolsCalled, executions, tools, Outstanding, stateMeasuredRates);
    }

    // Everything the loop reports when it stops, in one place: why, what really ran, whether the method was
    // finished (guardrail 2, last line of defence), whether the answer's evidence holds up (guardrail 3) and
    // whether its verdict agrees with the measurements.
    private static AgentResult Stop(
        LoopStopReason reason,
        int iterations,
        string text,
        bool hitCap,
        List<string> toolsCalled,
        List<ToolExecution> executions,
        IReadOnlyList<ToolDefinition> tools,
        Func<string, string?> outstanding,
        bool stateMeasuredRates)
    {
        Log.LoopStopped(reason, iterations);
        Log.ToolsCalled(toolsCalled);

        var measured = Measured.Lines(executions);
        Log.Measured(measured);

        var missing = outstanding(text);
        Log.Incomplete(missing);

        if (!hitCap)
        {
            Log.Ungrounded(Grounding.Check(text, tools.Select(t => t.Name), executions));
            Log.Inconsistent(VerdictCheck.Check(text, executions));
        }

        // Follow-up 2: the loop states the measured figures itself, in its own words and after the model's answer,
        // so a wrong figure written by the model cannot be the last word.
        if (stateMeasuredRates && !hitCap && measured.Count > 0)
        {
            text += Environment.NewLine + Environment.NewLine + "Measured by the tools:" + Environment.NewLine
                    + string.Join(Environment.NewLine, measured.Select(line => line.Replace("MEASURED ", "")));
        }

        return new AgentResult(text, hitCap, iterations, missing != null);
    }

    private static string Describe(ToolUseBlock toolUse, bool isError)
    {
        var args = string.Join(", ", toolUse.Input.Select(kv => $"{kv.Key}={kv.Value}"));
        return $"{toolUse.Name}({args}){(isError ? " [error]" : "")}";
    }
}
