using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace Quarry.Ingestion.Embeddings.Onnx;

/// <summary>
/// Генератор локальных векторных эмбеддингов на базе ONNX Runtime и Microsoft.ML.Tokenizers.
/// Реализует стандартный интерфейс <see cref="IEmbeddingGenerator{TInput, TEmbedding}"/> из Microsoft.Extensions.AI.
/// </summary>
public sealed class OnnxEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly InferenceSession _session;
    private readonly Tokenizer _tokenizer;
    private readonly Func<IReadOnlyList<int>, IReadOnlyList<long>> _tokenMapper;
    private readonly int _maxSequenceLength;
    private readonly string _outputName;
    private readonly bool _hasTokenTypeIds;
    private bool _disposed;

    public EmbeddingGeneratorMetadata Metadata { get; }

    public OnnxEmbeddingGenerator(
        string modelPath,
        Tokenizer tokenizer,
        string modelId = "local-onnx",
        int maxSequenceLength = 512,
        Func<IReadOnlyList<int>, IReadOnlyList<long>>? tokenMapper = null,
        SessionOptions? sessionOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSequenceLength);

        var options = sessionOptions ?? new SessionOptions();
        _session = new InferenceSession(modelPath, options);
        _tokenizer = tokenizer;
        _tokenMapper = tokenMapper ?? TokenMappers.Direct;
        _maxSequenceLength = maxSequenceLength;

        // Определяем входные и выходные параметры модели
        _hasTokenTypeIds = _session.InputMetadata.ContainsKey("token_type_ids");
        
        // Обычно выходной тензор называется 'last_hidden_state' или первый из метаданных
        _outputName = _session.OutputMetadata.ContainsKey("last_hidden_state")
            ? "last_hidden_state"
            : _session.OutputMetadata.Keys.First();

        var outputShape = _session.OutputMetadata[_outputName].Dimensions;
        int dimensions = outputShape.Length > 2 ? outputShape[2] : 384;
        if (dimensions <= 0) dimensions = 384; // если размерность динамическая (-1)

        Metadata = new EmbeddingGeneratorMetadata(
            providerName: "ONNX Runtime",
            defaultModelId: modelId,
            defaultModelDimensions: dimensions);
    }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(values);

        var textList = values.ToList();
        if (textList.Count == 0)
        {
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>());
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 1. Токенизация всех текстов в батче
        var batchTokens = new List<IReadOnlyList<long>>(textList.Count);
        int maxLen = 0;

        foreach (var text in textList)
        {
            var rawTokens = _tokenizer.EncodeToIds(text);
            var tokens = _tokenMapper(rawTokens);
            
            // Если длина превышает лимит модели, обрезаем
            if (tokens.Count > _maxSequenceLength)
            {
                tokens = tokens.Take(_maxSequenceLength).ToList();
            }

            maxLen = Math.Max(maxLen, tokens.Count);
            batchTokens.Add(tokens);
        }

        // Защита от нулевой длины (пустые строки)
        if (maxLen == 0) maxLen = 1;

        int batchSize = textList.Count;

        // 2. Формирование тензоров для ONNX [batchSize, maxLen]
        var inputIds = new DenseTensor<long>(new[] { batchSize, maxLen });
        var attentionMask = new DenseTensor<long>(new[] { batchSize, maxLen });
        DenseTensor<long>? tokenTypeIds = _hasTokenTypeIds ? new DenseTensor<long>(new[] { batchSize, maxLen }) : null;

        for (int i = 0; i < batchSize; i++)
        {
            var tokens = batchTokens[i];
            for (int j = 0; j < maxLen; j++)
            {
                if (j < tokens.Count)
                {
                    inputIds[i, j] = tokens[j];
                    attentionMask[i, j] = 1;
                }
                else
                {
                    inputIds[i, j] = 0; // Padding token
                    attentionMask[i, j] = 0;
                }

                if (tokenTypeIds != null)
                {
                    tokenTypeIds[i, j] = 0;
                }
            }
        }

        // 3. Вызов инференса ONNX
        var inputs = new List<NamedOnnxValue>(3)
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask)
        };

        if (tokenTypeIds != null)
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds));
        }

        using var results = _session.Run(inputs);
        var outputTensor = results.First(r => r.Name == _outputName).AsTensor<float>();

        // Ожидаемая форма тензора: [batchSize, seqLen, hiddenDim]
        int hiddenDim = outputTensor.Dimensions[2];
        var resultList = new List<Embedding<float>>(batchSize);

        // 4. Mean Pooling и L2-нормализация для каждого элемента батча
        for (int i = 0; i < batchSize; i++)
        {
            var vector = new float[hiddenDim];
            float tokenCount = 0f;

            for (int j = 0; j < maxLen; j++)
            {
                if (attentionMask[i, j] == 1)
                {
                    tokenCount += 1f;
                    for (int d = 0; d < hiddenDim; d++)
                    {
                        vector[d] += outputTensor[i, j, d];
                    }
                }
            }

            if (tokenCount > 0f)
            {
                for (int d = 0; d < hiddenDim; d++)
                {
                    vector[d] /= tokenCount;
                }
            }

            // L2-нормализация (вектор единичной длины)
            NormalizeL2(vector);

            resultList.Add(new Embedding<float>(vector));
        }

        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(resultList));
    }

    private static void NormalizeL2(Span<float> vector)
    {
        float sumSquares = 0f;
        for (int i = 0; i < vector.Length; i++)
        {
            sumSquares += vector[i] * vector[i];
        }

        float norm = MathF.Sqrt(sumSquares);
        if (norm > 0f)
        {
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _session.Dispose();
            _disposed = true;
        }
    }
}
