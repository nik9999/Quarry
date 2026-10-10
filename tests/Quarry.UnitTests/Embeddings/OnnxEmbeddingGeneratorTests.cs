using Microsoft.ML.Tokenizers;
using Quarry.Ingestion.Embeddings.Onnx;
using Xunit;

namespace Quarry.Ingestion.Tests.Embeddings;

public class OnnxEmbeddingGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_WithLocalOnnxModel_ProducesL2Normalized384DVector()
    {
        var modelDir = FindModelDirectory("model/multilingual-e5-small");
        if (modelDir == null)
        {
            // Model files not found in standard paths, skip test
            return;
        }

        var modelPath = Path.Combine(modelDir, "model.onnx");
        var spPath = Path.Combine(modelDir, "sentencepiece.bpe.model");

        if (!File.Exists(modelPath) || !File.Exists(spPath))
        {
            return;
        }

        await using var spStream = File.OpenRead(spPath);
        var tokenizer = SentencePieceTokenizer.Create(spStream, addBeginningOfSentence: false, addEndOfSentence: false);

        using var generator = new OnnxEmbeddingGenerator(
            modelPath: modelPath,
            tokenizer: tokenizer,
            modelId: "multilingual-e5-small",
            maxSequenceLength: 128,
            tokenMapper: TokenMappers.XlmRoberta);

        var result = await generator.GenerateAsync(["passage: Тестовый документ для проверки ONNX Runtime."]);

        Assert.Single(result);
        var vector = result[0].Vector;
        Assert.Equal(384, vector.Length);

        // Проверка L2-нормализации: длина единичного вектора должна равняться ~1.0
        float normSq = 0f;
        for (int i = 0; i < vector.Length; i++)
        {
            normSq += vector.Span[i] * vector.Span[i];
        }

        float norm = MathF.Sqrt(normSq);
        Assert.Equal(1.0f, norm, precision: 3);
    }

    private static string? FindModelDirectory(string relativeSubPath)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, relativeSubPath);
            if (Directory.Exists(candidate)) return candidate;

            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        var cwdCandidate = Path.Combine(Directory.GetCurrentDirectory(), relativeSubPath);
        return Directory.Exists(cwdCandidate) ? cwdCandidate : null;
    }
}
