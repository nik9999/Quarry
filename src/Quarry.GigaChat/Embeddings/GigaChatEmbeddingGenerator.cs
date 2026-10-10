using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Quarry.GigaChat.Client;
using Quarry.GigaChat.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Quarry.GigaChat.Embeddings;

/// <summary>
/// An <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/> implementation using GigaChat embeddings API.
/// </summary>
public sealed class GigaChatEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly IGigaChatClient _client;
    private readonly string _model;
    private readonly ILogger _logger;
    private readonly EmbeddingGeneratorMetadata _metadata;

    /// <inheritdoc/>
    public EmbeddingGeneratorMetadata Metadata => _metadata;

    /// <summary>
    /// Initializes a new instance of <see cref="GigaChatEmbeddingGenerator"/>.
    /// </summary>
    /// <param name="client">The GigaChat API client.</param>
    /// <param name="model">Model to use (defaults to "Embeddings").</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    public GigaChatEmbeddingGenerator(
        IGigaChatClient client,
        string? model = null,
        ILoggerFactory? loggerFactory = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = loggerFactory?.CreateLogger<GigaChatEmbeddingGenerator>() ?? (ILogger)NullLogger.Instance;

        if (!string.IsNullOrWhiteSpace(model))
        {
            if (GigaChatDefaults.IsGenerativeModel(model))
            {
                _logger.LogWarning("Model '{Model}' appears to be a chat generation model, but GigaChat Embeddings API expects an embeddings model (such as Embeddings, Embeddings-2, EmbeddingsGigaR, or Embeddings-3B-2025-09).", model);
            }
            _model = model;
        }
        else
        {
            _model = !string.IsNullOrWhiteSpace(client.Options?.DefaultEmbeddingModel)
                ? client.Options.DefaultEmbeddingModel
                : GigaChatDefaults.ModelEmbeddings;
        }

        _metadata = new EmbeddingGeneratorMetadata(
            providerName: "GigaChat",
            providerUri: new Uri("https://developers.sber.ru/portal/products/gigachat"),
            defaultModelId: _model);
    }

    /// <inheritdoc/>
    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        var inputList = values.ToList();
        if (inputList.Count == 0)
        {
            return new GeneratedEmbeddings<Embedding<float>>(Array.Empty<Embedding<float>>());
        }

        var model = options?.ModelId ?? _model;
        var request = new GigaChatEmbeddingRequest(inputList, model);

        _logger.LogDebug("Generating embeddings for {Count} items with model {Model}", inputList.Count, model);

        var response = await _client.CreateEmbeddingsAsync(request, cancellationToken).ConfigureAwait(false);

        var embeddings = response.Data
            .OrderBy(d => d.Index)
            .Select(d => new Embedding<float>(d.Embedding))
            .ToList();

        return new GeneratedEmbeddings<Embedding<float>>(embeddings)
        {
            Usage = new UsageDetails
            {
                InputTokenCount = inputList.Count
            }
        };
    }

    /// <inheritdoc/>
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceKey is null)
        {
            if (serviceType == typeof(EmbeddingGeneratorMetadata))
            {
                return _metadata;
            }

            if (serviceType.IsInstanceOfType(this))
            {
                return this;
            }

            if (serviceType.IsInstanceOfType(_client))
            {
                return _client;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _client.Dispose();
    }
}
