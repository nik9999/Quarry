using System;
using System.Collections.Generic;
using Microsoft.Agents.AI;
using Quarry.GigaChat.ChatClient;
using Quarry.GigaChat.Client;
using Quarry.GigaChat.Embeddings;
using Quarry.GigaChat.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Quarry.GigaChat.Extensions;

/// <summary>
/// Extension methods for integrating Sber GigaChat with Microsoft Agent Framework and Microsoft.Extensions.AI.
/// </summary>
public static class GigaChatClientExtensions
{
    /// <summary>
    /// Creates an <see cref="IChatClient"/> from an <see cref="IGigaChatClient"/>.
    /// </summary>
    /// <param name="client">The GigaChat client.</param>
    /// <param name="model">The model to use (e.g. "GigaChat", "GigaChat-Plus", "GigaChat-Pro", "GigaChat-Max").</param>
    /// <param name="enableFunctionInvocation">Whether to wrap the client with automatic function calling middleware.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <returns>An <see cref="IChatClient"/> instance.</returns>
    public static IChatClient AsIChatClient(
        this IGigaChatClient client,
        string? model = null,
        bool enableFunctionInvocation = true,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        IChatClient chatClient = new GigaChatChatClient(client, model, loggerFactory);

        if (enableFunctionInvocation)
        {
            chatClient = chatClient.AsBuilder().UseFunctionInvocation().Build();
        }

        return chatClient;
    }

    /// <summary>
    /// Creates an <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/> from an <see cref="IGigaChatClient"/>.
    /// </summary>
    /// <param name="client">The GigaChat client.</param>
    /// <param name="model">The embeddings model (defaults to "Embeddings").</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <returns>An <see cref="IEmbeddingGenerator{String, Embedding}"/> instance.</returns>
    public static IEmbeddingGenerator<string, Embedding<float>> AsEmbeddingGenerator(
        this IGigaChatClient client,
        string? model = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        return new GigaChatEmbeddingGenerator(client, model, loggerFactory);
    }

    /// <summary>
    /// Creates a Microsoft Agent Framework <see cref="ChatClientAgent"/> powered by GigaChat.
    /// </summary>
    /// <param name="client">The GigaChat client.</param>
    /// <param name="model">The model name to use.</param>
    /// <param name="instructions">System instructions defining the agent's behavior.</param>
    /// <param name="name">Agent name.</param>
    /// <param name="description">Agent description.</param>
    /// <param name="tools">Tools available to the agent.</param>
    /// <param name="clientFactory">Optional factory to customize the underlying <see cref="IChatClient"/> pipeline.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="services">Optional service provider for resolving tool dependencies.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance.</returns>
    public static ChatClientAgent AsAIAgent(
        this IGigaChatClient client,
        string? model = null,
        string? instructions = null,
        string? name = null,
        string? description = null,
        IList<AITool>? tools = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        var options = new ChatClientAgentOptions
        {
            Name = name,
            Description = description,
        };

        if (!string.IsNullOrWhiteSpace(instructions) || tools is { Count: > 0 })
        {
            options.ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                Tools = tools
            };
        }

        return client.AsAIAgent(options, model, clientFactory, loggerFactory, services);
    }

    /// <summary>
    /// Creates a Microsoft Agent Framework <see cref="ChatClientAgent"/> powered by GigaChat with explicit options.
    /// </summary>
    /// <param name="client">The GigaChat client.</param>
    /// <param name="options">Options for configuring the AI agent.</param>
    /// <param name="model">The model name to use.</param>
    /// <param name="clientFactory">Optional factory to customize the underlying <see cref="IChatClient"/> pipeline.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="services">Optional service provider for resolving tool dependencies.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance.</returns>
    public static ChatClientAgent AsAIAgent(
        this IGigaChatClient client,
        ChatClientAgentOptions options,
        string? model = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        var chatClient = client.AsIChatClient(model: model, enableFunctionInvocation: true, loggerFactory: loggerFactory);

        if (clientFactory is not null)
        {
            chatClient = clientFactory(chatClient);
        }

        return new ChatClientAgent(chatClient, options, loggerFactory, services);
    }

    /// <summary>
    /// Creates a Microsoft Agent Framework <see cref="ChatClientAgent"/> from an existing <see cref="GigaChatChatClient"/>.
    /// </summary>
    /// <param name="chatClient">The GigaChat chat client.</param>
    /// <param name="options">Agent options.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="services">Optional service provider.</param>
    /// <returns>A <see cref="ChatClientAgent"/> instance.</returns>
    public static ChatClientAgent AsAIAgent(
        this GigaChatChatClient chatClient,
        ChatClientAgentOptions? options = null,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);

        IChatClient client = chatClient.AsBuilder().UseFunctionInvocation().Build();
        return new ChatClientAgent(client, options ?? new ChatClientAgentOptions(), loggerFactory, services);
    }
}
