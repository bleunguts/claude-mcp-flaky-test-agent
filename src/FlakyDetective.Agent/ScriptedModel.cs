using System;
using System.Collections.Generic;
using static System.Net.Mime.MediaTypeNames;

namespace FlakyDetective.Agent;
public interface IModel
{
    Task<ModelResponse> CompleteAsync(List<Message> messages, IReadOnlyList<ToolDefinition> tools, string? systemPrompt = null, CancellationToken cancellationToken = default);
}
public class ScriptedModel(IEnumerable<Func<List<Message>, ModelResponse>> steps) : IModel
{
    private readonly Queue<Func<List<Message>, ModelResponse>> _steps = new(steps);
    public Task<ModelResponse> CompleteAsync(List<Message> messages, IReadOnlyList<ToolDefinition> tools, string? systemPrompt = null, CancellationToken cancellationToken = default)
    {
        if(_steps.Count == 0)
        {
            throw new InvalidOperationException("The script has run out of steps.");
        }
        var nextFunctor = _steps.Dequeue();
        return Task.FromResult(nextFunctor(messages));
    }  
}