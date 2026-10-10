using System;
using System.Collections.Generic;
using Microsoft.Agents.AI;
using Quarry.GigaChat.Client;
using Quarry.GigaChat.Extensions;
using Quarry.GigaChat.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Quarry.GigaChat;

/// <summary>
/// Factory helpers for creating GigaChat AI agents compatible with Microsoft Agent Framework.
/// </summary>
public static class GigaChatAgent
{
    /// <summary>
    /// Creates a new <see cref="ChatClientAgent"/> backed by Sber GigaChat.
    /// </summary>
    /// <param name="client">The GigaChat API client.</param>
    /// <param name="model">The model name to use.</param>
    /// <param name="instructions">System instructions defining the agent's behavior.</param>
    /// <param name="name">Agent name.</param>
    /// <param name="description">Agent description.</param>
    /// <param name="tools">Tools available to the agent.</param>
    /// <param name="clientFactory">Optional pipeline factory for the chat client.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="services">Optional service provider for resolving tool dependencies.</param>
    /// <returns>A configured <see cref="ChatClientAgent"/> instance.</returns>
    public static ChatClientAgent Create(
        IGigaChatClient client,
        string? model = null,
        string? instructions = null,
        string? name = null,
        string? description = null,
        IList<AITool>? tools = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? services = null)
    {
        return client.AsAIAgent(
            model: model,
            instructions: instructions,
            name: name,
            description: description,
            tools: tools,
            clientFactory: clientFactory,
            loggerFactory: loggerFactory,
            services: services);
    }

    /// <summary>
    /// Creates a new <see cref="ChatClientAgent"/> backed by Sber GigaChat using explicit options.
    /// </summary>
    /// <param name="client">The GigaChat API client.</param>
    /// <param name="options">Agent options.</param>
    /// <param name="model">Model name to use.</param>
    /// <param name="clientFactory">Optional pipeline factory for the chat client.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="services">Optional service provider for resolving tool dependencies.</param>
    /// <returns>A configured <see cref="ChatClientAgent"/> instance.</returns>
    public static ChatClientAgent Create(
        IGigaChatClient client,
        ChatClientAgentOptions options,
        string? model = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? services = null)
    {
        return client.AsAIAgent(
            options: options,
            model: model,
            clientFactory: clientFactory,
            loggerFactory: loggerFactory,
            services: services);
    }

    /// <summary>
    /// Creates a new <see cref="ChatClientAgent"/> directly from GigaChat client options.
    /// </summary>
    public static ChatClientAgent Create(
        GigaChatClientOptions clientOptions,
        string? model = null,
        string? instructions = null,
        string? name = null,
        string? description = null,
        IList<AITool>? tools = null,
        Func<IChatClient, IChatClient>? clientFactory = null,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? services = null)
    {
        var client = new GigaChatClient(clientOptions);
        return Create(
            client: client,
            model: model,
            instructions: instructions,
            name: name,
            description: description,
            tools: tools,
            clientFactory: clientFactory,
            loggerFactory: loggerFactory,
            services: services);
    }

    /// <summary>
    /// Creates a new <see cref="ChatClientAgent"/> directly from an authorization key.
    /// </summary>
    /// <param name="authKey">GigaChat authorization key.</param>
    /// <param name="instructions">System instructions defining the agent's behavior.</param>
    /// <param name="name">Agent name.</param>
    /// <param name="model">Model name to use (defaults to "GigaChat").</param>
    /// <param name="tools">Tools available to the agent.</param>
    /// <param name="scope">API scope.</param>
    /// <param name="ignoreSslErrors">Whether to bypass Russian CA certificate validation.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="services">Optional service provider.</param>
    /// <returns>A configured <see cref="ChatClientAgent"/> instance.</returns>
    public static ChatClientAgent Create(
        string authKey,
        string? instructions = null,
        string? name = null,
        string? model = null,
        IList<AITool>? tools = null,
        string scope = GigaChatDefaults.ScopePers,
        bool ignoreSslErrors = true,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? services = null)
    {
        return CreateFromAuthKey(
            authKey: authKey,
            instructions: instructions,
            name: name,
            model: model,
            tools: tools,
            scope: scope,
            ignoreSslErrors: ignoreSslErrors,
            loggerFactory: loggerFactory,
            services: services);
    }

    /// <summary>
    /// Creates a new <see cref="ChatClientAgent"/> directly from an authorization key.
    /// </summary>
    public static ChatClientAgent CreateFromAuthKey(
        string authKey,
        string? instructions = null,
        string? name = null,
        string? model = null,
        IList<AITool>? tools = null,
        string scope = GigaChatDefaults.ScopePers,
        bool ignoreSslErrors = true,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? services = null)
    {
        var options = GigaChatClientOptions.FromAuthKey(authKey, scope, ignoreSslErrors);
        var client = new GigaChatClient(options);
        return Create(
            client: client,
            model: model,
            instructions: instructions,
            name: name,
            description: null,
            tools: tools,
            loggerFactory: loggerFactory,
            services: services);
    }

    /// <summary>
    /// Creates a new <see cref="ChatClientAgent"/> directly from Client ID and Client Secret.
    /// </summary>
    /// <param name="clientId">Client ID from Sber Developers portal.</param>
    /// <param name="clientSecret">Client Secret from Sber Developers portal.</param>
    /// <param name="instructions">System instructions defining the agent's behavior.</param>
    /// <param name="name">Agent name.</param>
    /// <param name="model">Model name to use (defaults to "GigaChat").</param>
    /// <param name="tools">Tools available to the agent.</param>
    /// <param name="scope">API scope.</param>
    /// <param name="ignoreSslErrors">Whether to bypass Russian CA certificate validation.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="services">Optional service provider.</param>
    /// <returns>A configured <see cref="ChatClientAgent"/> instance.</returns>
    public static ChatClientAgent CreateFromClientCredentials(
        string clientId,
        string clientSecret,
        string? instructions = null,
        string? name = null,
        string? model = null,
        IList<AITool>? tools = null,
        string scope = GigaChatDefaults.ScopePers,
        bool ignoreSslErrors = true,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? services = null)
    {
        var options = GigaChatClientOptions.FromClientCredentials(clientId, clientSecret, scope, ignoreSslErrors);
        var client = new GigaChatClient(options);
        return Create(
            client: client,
            model: model,
            instructions: instructions,
            name: name,
            description: null,
            tools: tools,
            loggerFactory: loggerFactory,
            services: services);
    }
}
