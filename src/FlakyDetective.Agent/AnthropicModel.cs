using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Sdk = Anthropic.Models.Messages;   // the SDK has its own TextBlock, ToolUseBlock, Message ...; keep ours unqualified

namespace FlakyDetective.Agent;

// IModel backed by the Claude API through the official Anthropic C# SDK.
//
// It is a translator, like OllamaModel: our history and tool definitions become a
// MessageCreateParams, client.Messages.Create does the call, and the reply becomes a ModelResponse.
// The loop never sees SDK types.
//
// Needs an API key with credits (ANTHROPIC_API_KEY, read by the SDK). Without credits the API
// answers 400 and this class turns that into a readable message.
public class AnthropicModel : IModel
{
    // Claude Opus 5.5. Thinking is always on for this model, so thinking blocks come back in
    // replies and must be sent back unchanged. Override with ANTHROPIC_MODEL, for example
    // "claude-haiku-5-5" for a much cheaper run.
    public const string DefaultModel = "claude-opus-5-5";

    private readonly AnthropicClient _client;
    private readonly string _model;
    private readonly int _maxTokens;

    public AnthropicModel(AnthropicClient client, string model = DefaultModel, int maxTokens = 16000)
    {
        _client = client;
        _model = model;
        _maxTokens = maxTokens;
    }

    public async Task<ModelResponse> CompleteAsync(
        List<Message> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken = default)
    {
        var request = new Sdk.MessageCreateParams
        {
            Model = _model,
            MaxTokens = _maxTokens,
            Messages = ToSdkMessages(messages),
        };

        if (tools.Count > 0)
        {
            request = request with { Tools = tools.Select(ToSdkTool).ToList() };
        }

        Sdk.Message response;
        try
        {
            response = await _client.Messages.Create(request, cancellationToken);
        }
        catch (AnthropicUnauthorizedException ex)
        {
            throw new InvalidOperationException(
                "Anthropic rejected the API key (401). Is ANTHROPIC_API_KEY set to a valid key?", ex);
        }
        catch (AnthropicBadRequestException ex)
        {
            // A missing credit balance is reported here as an invalid_request_error.
            throw new InvalidOperationException(
                $"Anthropic rejected the request (400). If this mentions the credit balance, the account has no API credits. {ex.Message}", ex);
        }
        catch (AnthropicApiException ex)
        {
            throw new InvalidOperationException($"The Anthropic API call failed. {ex.Message}", ex);
        }

        return ToModelResponse(response);
    }

    // ---- our types -> SDK types ----

    public static List<Sdk.MessageParam> ToSdkMessages(List<Message> history)
    {
        var result = new List<Sdk.MessageParam>();

        foreach (var message in history)
        {
            var blocks = new List<Sdk.ContentBlockParam>();

            foreach (var block in message.Content)
            {
                switch (block)
                {
                    case TextBlock text:
                        // the API rejects empty text blocks
                        if (!string.IsNullOrEmpty(text.Text))
                        {
                            blocks.Add(new Sdk.TextBlockParam { Text = text.Text });
                        }
                        break;
                    case ThinkingBlock thinking:
                        blocks.Add(new Sdk.ThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
                        break;
                    case RedactedThinkingBlock redacted:
                        blocks.Add(new Sdk.RedactedThinkingBlockParam { Data = redacted.Data });
                        break;
                    case ToolUseBlock toolUse:
                        blocks.Add(new Sdk.ToolUseBlockParam
                        {
                            ID = toolUse.Id,
                            Name = toolUse.Name,
                            Input = ToJsonInput(toolUse.Input),
                        });
                        break;
                    case ToolResultBlock toolResult:
                        blocks.Add(new Sdk.ToolResultBlockParam
                        {
                            ToolUseID = toolResult.ToolUseId,
                            Content = toolResult.Content,
                            IsError = toolResult.IsError,
                        });
                        break;
                }
            }

            if (blocks.Count == 0)
            {
                continue;
            }

            // MessageParam is a union of user, assistant and (mid-conversation) system messages
            if (message.Role == "assistant")
            {
                result.Add(new Sdk.AssistantMessageParam { Content = blocks });
            }
            else
            {
                result.Add(new Sdk.UserMessageParam { Content = blocks });
            }
        }

        return result;
    }

    // Our ToolDefinition holds the whole JSON schema (type, properties, required). The SDK's
    // Tool.InputSchema wants the properties and the required list, so take those two parts.
    // Other schema keywords (for example additionalProperties) are not forwarded.
    public static Sdk.ToolUnion ToSdkTool(ToolDefinition tool)
    {
        var properties = new Dictionary<string, JsonElement>();
        var required = new List<string>();

        if (tool.InputSchema.ValueKind == JsonValueKind.Object)
        {
            if (tool.InputSchema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in props.EnumerateObject())
                {
                    properties[property.Name] = property.Value.Clone();
                }
            }

            if (tool.InputSchema.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
            {
                required.AddRange(req.EnumerateArray().Select(r => r.GetString()!));
            }
        }

        return new Sdk.Tool
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = new() { Properties = properties, Required = required },
        };
    }

    // Tool input may hold plain values (from a script) or JsonElements (from another model).
    private static Dictionary<string, JsonElement> ToJsonInput(Dictionary<string, object?> input) =>
        input.ToDictionary(
            kv => kv.Key,
            kv => kv.Value is JsonElement element ? element : JsonSerializer.SerializeToElement(kv.Value));

    // ---- SDK types -> our types ----

    public static ModelResponse ToModelResponse(Sdk.Message response)
    {
        if (response.StopReason == "refusal")
        {
            var detail = response.StopDetails is { } details
                ? $" Category: {details.Category}. {details.Explanation}"
                : string.Empty;
            return new ModelResponse(
                [new TextBlock($"The model declined to continue (refusal).{detail}")], StopReason.EndTurn);
        }

        var blocks = new List<ContentBlock>();

        foreach (var block in response.Content)
        {
            if (block.TryPickText(out Sdk.TextBlock? text))
            {
                blocks.Add(new TextBlock(text.Text));
            }
            else if (block.TryPickThinking(out Sdk.ThinkingBlock? thinking))
            {
                blocks.Add(new ThinkingBlock(thinking.Thinking, thinking.Signature));
            }
            else if (block.TryPickRedactedThinking(out Sdk.RedactedThinkingBlock? redacted))
            {
                blocks.Add(new RedactedThinkingBlock(redacted.Data));
            }
            else if (block.TryPickToolUse(out Sdk.ToolUseBlock? toolUse))
            {
                var input = toolUse.Input.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
                blocks.Add(new ToolUseBlock(toolUse.ID, toolUse.Name, input));
            }
        }

        // Decide by what is in the reply, as OllamaModel does. A reply cut off by max_tokens simply
        // ends the turn with whatever text arrived.
        var stopReason = blocks.OfType<ToolUseBlock>().Any() ? StopReason.ToolUse : StopReason.EndTurn;
        return new ModelResponse(blocks, stopReason);
    }
}
