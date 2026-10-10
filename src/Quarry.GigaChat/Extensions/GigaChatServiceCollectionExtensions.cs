using System;
using Microsoft.Agents.AI;
using Quarry.GigaChat.ChatClient;
using Quarry.GigaChat.Client;
using Quarry.GigaChat.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Quarry.GigaChat.Extensions;

/// <summary>
/// Dependency Injection extensions for registering GigaChat client and AI agents.
/// </summary>
public static class GigaChatServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IGigaChatClient"/> and <see cref="IChatClient"/> configured with GigaChat options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure GigaChat options.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddGigaChatClient(
        this IServiceCollection services,
        Action<GigaChatClientOptions> configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureOptions);

        var options = new GigaChatClientOptions();
        configureOptions(options);

        services.AddSingleton(options);
        services.AddSingleton<IGigaChatClient, GigaChatClient>(sp =>
        {
            var logger = sp.GetService<ILogger<GigaChatClient>>();
            return new GigaChatClient(options, logger);
        });

        services.AddSingleton<IChatClient>(sp =>
        {
            var gigaClient = sp.GetRequiredService<IGigaChatClient>();
            var loggerFactory = sp.GetService<ILoggerFactory>();
            return gigaClient.AsIChatClient(loggerFactory: loggerFactory);
        });

        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
        {
            var gigaClient = sp.GetRequiredService<IGigaChatClient>();
            var loggerFactory = sp.GetService<ILoggerFactory>();
            return gigaClient.AsEmbeddingGenerator(loggerFactory: loggerFactory);
        });

        return services;
    }

    /// <summary>
    /// Registers <see cref="IGigaChatClient"/> and <see cref="IChatClient"/> with a pre-configured <see cref="GigaChatClientOptions"/> instance.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">Configured options.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddGigaChatClient(
        this IServiceCollection services,
        GigaChatClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton<IGigaChatClient, GigaChatClient>(sp =>
        {
            var logger = sp.GetService<ILogger<GigaChatClient>>();
            return new GigaChatClient(options, logger);
        });

        services.AddSingleton<IChatClient>(sp =>
        {
            var gigaClient = sp.GetRequiredService<IGigaChatClient>();
            var loggerFactory = sp.GetService<ILoggerFactory>();
            return gigaClient.AsIChatClient(loggerFactory: loggerFactory);
        });

        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
        {
            var gigaClient = sp.GetRequiredService<IGigaChatClient>();
            var loggerFactory = sp.GetService<ILoggerFactory>();
            return gigaClient.AsEmbeddingGenerator(loggerFactory: loggerFactory);
        });

        return services;
    }

    /// <summary>
    /// Registers <see cref="IGigaChatClient"/> and <see cref="IChatClient"/> with an authorization key.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="authKey">GigaChat authorization key.</param>
    /// <param name="ignoreSslErrors">Whether to bypass Russian CA certificate validation.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddGigaChatClient(
        this IServiceCollection services,
        string authKey,
        bool ignoreSslErrors = true)
    {
        return services.AddGigaChatClient(opts =>
        {
            opts.AuthKey = authKey;
            opts.IgnoreSslErrors = ignoreSslErrors;
        });
    }

    /// <summary>
    /// Registers <see cref="IGigaChatClient"/> and <see cref="IChatClient"/> with Client ID and Client Secret.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="clientId">Client ID from Sber Developers portal.</param>
    /// <param name="clientSecret">Client Secret from Sber Developers portal.</param>
    /// <param name="ignoreSslErrors">Whether to bypass Russian CA certificate validation.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddGigaChatClient(
        this IServiceCollection services,
        string clientId,
        string clientSecret,
        bool ignoreSslErrors = true)
    {
        return services.AddGigaChatClient(opts =>
        {
            opts.ClientId = clientId;
            opts.ClientSecret = clientSecret;
            opts.IgnoreSslErrors = ignoreSslErrors;
        });
    }

    /// <summary>
    /// Registers a named GigaChat AI agent in the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">Agent name.</param>
    /// <param name="instructions">Agent system instructions.</param>
    /// <param name="model">Model name to use.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddGigaChatAgent(
        this IServiceCollection services,
        string name,
        string? instructions = null,
        string? model = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(name);

        services.AddKeyedSingleton<AIAgent>(name, (sp, key) =>
        {
            var gigaClient = sp.GetRequiredService<IGigaChatClient>();
            var loggerFactory = sp.GetService<ILoggerFactory>();
            return gigaClient.AsAIAgent(
                name: name,
                instructions: instructions,
                model: model,
                loggerFactory: loggerFactory,
                services: sp);
        });

        return services;
    }
}
