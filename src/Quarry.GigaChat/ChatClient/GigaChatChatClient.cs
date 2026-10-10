using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Quarry.GigaChat.Client;
using Quarry.GigaChat.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Quarry.GigaChat.ChatClient;

/// <summary>
/// An <see cref="IChatClient"/> implementation powered by Sber GigaChat.
/// </summary>
public sealed class GigaChatChatClient : IChatClient
{
    private readonly IGigaChatClient _gigaChatClient;
    private readonly ILogger _logger;
    private readonly string _defaultModel;
    private readonly ChatClientMetadata _metadata;

    /// <summary>
    /// Gets the underlying GigaChat client.
    /// </summary>
    public IGigaChatClient Client => _gigaChatClient;

    /// <inheritdoc/>
    public ChatClientMetadata Metadata => _metadata;

    /// <summary>
    /// Initializes a new instance of <see cref="GigaChatChatClient"/>.
    /// </summary>
    /// <param name="gigaChatClient">The GigaChat API client.</param>
    /// <param name="defaultModel">Optional default model name (defaults to "GigaChat").</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    public GigaChatChatClient(
        IGigaChatClient gigaChatClient,
        string? defaultModel = null,
        ILoggerFactory? loggerFactory = null)
    {
        _gigaChatClient = gigaChatClient ?? throw new ArgumentNullException(nameof(gigaChatClient));
        _logger = loggerFactory?.CreateLogger<GigaChatChatClient>() ?? (ILogger)NullLogger.Instance;
        _defaultModel = defaultModel ?? _gigaChatClient.Options.DefaultModel ?? GigaChatDefaults.ModelGigaChat;

        if (GigaChatDefaults.IsEmbeddingModel(_defaultModel))
        {
            _logger.LogWarning("Model '{Model}' appears to be an embeddings model, but GigaChat Chat API expects a generative chat model (such as GigaChat, GigaChat-2-Pro, GigaChat-2-Max, or GigaChat-3-Ultra).", _defaultModel);
        }

        _metadata = new ChatClientMetadata(
            providerName: "GigaChat",
            providerUri: new Uri("https://developers.sber.ru/portal/products/gigachat"),
            defaultModelId: _defaultModel);
    }

    /// <inheritdoc/>
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var request = BuildRequest(messages, options, isStreaming: false);

        _logger.LogDebug("Sending GigaChat completion request for model {Model} with {MessageCount} messages", request.Model, request.Messages.Count);

        var response = await _gigaChatClient.CreateChatCompletionAsync(request, cancellationToken).ConfigureAwait(false);

        return MapResponse(response);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var request = BuildRequest(messages, options, isStreaming: true);

        _logger.LogDebug("Starting GigaChat streaming request for model {Model}", request.Model);

        var stream = _gigaChatClient.CreateChatCompletionStreamAsync(request, cancellationToken);

        await foreach (var chunk in stream.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (chunk.Choices is null || chunk.Choices.Count == 0)
            {
                continue;
            }

            foreach (var choice in chunk.Choices)
            {
                var update = new ChatResponseUpdate
                {
                    ModelId = chunk.Model,
                    ResponseId = chunk.Created.ToString(System.Globalization.CultureInfo.InvariantCulture)
                };

                if (choice.Delta is not null)
                {
                    if (!string.IsNullOrEmpty(choice.Delta.Content))
                    {
                        update.Role = ChatRole.Assistant;
                        update.Contents.Add(new TextContent(choice.Delta.Content));
                    }

                    if (choice.Delta.FunctionCall is not null)
                    {
                        update.Role = ChatRole.Assistant;
                        var args = ParseArguments(choice.Delta.FunctionCall.Arguments);
                        var fnContent = new FunctionCallContent(
                            callId: Guid.NewGuid().ToString("N"),
                            name: choice.Delta.FunctionCall.Name,
                            arguments: args);
                        update.Contents.Add(fnContent);
                        update.FinishReason = ChatFinishReason.ToolCalls;
                    }
                }

                if (!string.IsNullOrEmpty(choice.FinishReason))
                {
                    update.FinishReason = MapFinishReason(choice.FinishReason);
                }

                if (update.Contents.Count > 0 || update.FinishReason.HasValue)
                {
                    yield return update;
                }
            }
        }
    }

    /// <inheritdoc/>
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceKey is null)
        {
            if (serviceType == typeof(ChatClientMetadata))
            {
                return _metadata;
            }

            if (serviceType.IsInstanceOfType(this))
            {
                return this;
            }

            if (serviceType.IsInstanceOfType(_gigaChatClient))
            {
                return _gigaChatClient;
            }

            if (serviceType.IsInstanceOfType(_gigaChatClient.Options))
            {
                return _gigaChatClient.Options;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _gigaChatClient.Dispose();
    }

    private GigaChatCompletionRequest BuildRequest(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options,
        bool isStreaming)
    {
        var model = options?.ModelId ?? _defaultModel;
        var request = new GigaChatCompletionRequest
        {
            Model = model,
            Stream = isStreaming
        };

        if (options is not null)
        {
            if (options.Temperature.HasValue)
            {
                request.Temperature = options.Temperature.Value;
            }

            if (options.TopP.HasValue)
            {
                request.TopP = options.TopP.Value;
            }

            if (options.MaxOutputTokens.HasValue)
            {
                request.MaxTokens = options.MaxOutputTokens.Value;
            }

            // Map Tools to GigaChat Functions
            if (options.Tools is { Count: > 0 })
            {
                request.Functions = new List<GigaChatFunction>();
                foreach (var tool in options.Tools)
                {
                    if (tool is AIFunction aiFunction)
                    {
                        request.Functions.Add(new GigaChatFunction
                        {
                            Name = aiFunction.Name,
                            Description = aiFunction.Description,
                            Parameters = aiFunction.JsonSchema
                        });
                    }
                }

                if (request.Functions.Count > 0)
                {
                    request.FunctionCall = "auto";
                }
            }
        }

        // Add instructions as first system message if provided
        if (!string.IsNullOrWhiteSpace(options?.Instructions))
        {
            request.Messages.Add(new GigaChatMessage("system", options.Instructions));
        }

        // Map ChatMessages
        foreach (var message in chatMessages)
        {
            // Function call result (tool response)
            var fnResult = message.Contents.OfType<FunctionResultContent>().FirstOrDefault();
            if (fnResult is not null)
            {
                var resultString = fnResult.Result is string str
                    ? str
                    : JsonSerializer.Serialize(fnResult.Result);

                request.Messages.Add(new GigaChatMessage
                {
                    Role = "function",
                    Name = fnResult.CallId, // In GigaChat, 'name' is the function name or call id
                    Content = resultString
                });
                continue;
            }

            // Assistant with function call
            var fnCall = message.Contents.OfType<FunctionCallContent>().FirstOrDefault();
            if (fnCall is not null)
            {
                var argsElement = JsonSerializer.SerializeToElement(fnCall.Arguments ?? new Dictionary<string, object?>());
                request.Messages.Add(new GigaChatMessage
                {
                    Role = "assistant",
                    Content = message.Text,
                    FunctionCall = new GigaChatFunctionCall
                    {
                        Name = fnCall.Name,
                        Arguments = argsElement
                    }
                });
                continue;
            }

            // Normal text message
            var role = message.Role == ChatRole.System
                ? "system"
                : message.Role == ChatRole.Assistant
                    ? "assistant"
                    : "user";

            List<string>? attachments = null;
            if (message.AdditionalProperties?.TryGetValue("attachments", out var attObj) == true)
            {
                if (attObj is IEnumerable<string> attList)
                {
                    attachments = new List<string>(attList);
                }
                else if (attObj is string attStr)
                {
                    attachments = [attStr];
                }
            }

            var text = message.Text ?? string.Join("", message.Contents.OfType<TextContent>().Select(c => c.Text));
            request.Messages.Add(new GigaChatMessage(role, text)
            {
                Attachments = attachments
            });
        }

        return request;
    }

    private static ChatResponse MapResponse(GigaChatCompletionResponse response)
    {
        var messages = new List<ChatMessage>();

        foreach (var choice in response.Choices)
        {
            var contents = new List<AIContent>();

            if (choice.Message.FunctionCall is not null)
            {
                var args = ParseArguments(choice.Message.FunctionCall.Arguments);
                contents.Add(new FunctionCallContent(
                    callId: choice.Message.FunctionCall.Name, // Use function name as call ID for GigaChat mapping
                    name: choice.Message.FunctionCall.Name,
                    arguments: args));
            }

            if (!string.IsNullOrEmpty(choice.Message.Content))
            {
                contents.Add(new TextContent(choice.Message.Content));
            }

            var chatMsg = new ChatMessage(ChatRole.Assistant, contents);
            messages.Add(chatMsg);
        }

        var finishReason = response.Choices.Count > 0
            ? MapFinishReason(response.Choices[0].FinishReason)
            : ChatFinishReason.Stop;

        UsageDetails? usage = null;
        if (response.Usage is not null)
        {
            usage = new UsageDetails
            {
                InputTokenCount = response.Usage.PromptTokens,
                OutputTokenCount = response.Usage.CompletionTokens,
                TotalTokenCount = response.Usage.TotalTokens
            };
        }

        return new ChatResponse(messages)
        {
            ModelId = response.Model,
            FinishReason = finishReason,
            Usage = usage,
            RawRepresentation = response
        };
    }

    private static ChatFinishReason MapFinishReason(string? finishReason)
    {
        return finishReason?.ToLowerInvariant() switch
        {
            "function_call" => ChatFinishReason.ToolCalls,
            "length" => ChatFinishReason.Length,
            "stop" => ChatFinishReason.Stop,
            "blacklist" => ChatFinishReason.ContentFilter,
            _ => ChatFinishReason.Stop
        };
    }

    private static Dictionary<string, object?>? ParseArguments(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Undefined || element.ValueKind == JsonValueKind.Null)
        {
            return new Dictionary<string, object?>();
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            var dict = new Dictionary<string, object?>();
            foreach (var prop in element.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => prop.Value.Clone()
                };
            }
            return dict;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, object?>>(element.GetString()!);
            }
            catch
            {
                // return empty if parsing string fails
            }
        }

        return new Dictionary<string, object?>();
    }
}
