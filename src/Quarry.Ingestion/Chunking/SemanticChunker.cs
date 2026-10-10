namespace Quarry.Ingestion.Chunking;

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Quarry.Core.Abstractions;
using Quarry.Core.Documents;
using Quarry.Core.Numerics;

/// <summary>
/// Семантический чанкинг: разделяет текст на смысловые блоки на основе падения косинусного сходства между предложениями.
/// </summary>
public sealed partial class SemanticChunker : IChunker
{
    private readonly IEmbeddingService _embeddingService;
    private readonly float _similarityThreshold;
    private readonly int _minSentencesPerChunk;
    private readonly int _maxSentencesPerChunk;

    public string Strategy => "semantic";
    public float SimilarityThreshold => _similarityThreshold;
    public int MinSentencesPerChunk => _minSentencesPerChunk;
    public int MaxSentencesPerChunk => _maxSentencesPerChunk;

    [GeneratedRegex(@"(?<=[.!?])\s+|\r?\n\r?\n|\r?\n", RegexOptions.Compiled)]
    private static partial Regex SentenceSplitRegex();

    public SemanticChunker(
        IEmbeddingService embeddingService,
        float similarityThreshold = 0.70f,
        int minSentencesPerChunk = 1,
        int maxSentencesPerChunk = 8)
    {
        ArgumentNullException.ThrowIfNull(embeddingService);

        if (similarityThreshold is < -1f or > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(similarityThreshold), "Similarity threshold must be in range [-1.0, 1.0].");
        }

        if (minSentencesPerChunk <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minSentencesPerChunk), "Min sentences must be greater than zero.");
        }

        if (maxSentencesPerChunk < minSentencesPerChunk)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSentencesPerChunk), "Max sentences must be greater than or equal to min sentences.");
        }

        _embeddingService = embeddingService;
        _similarityThreshold = similarityThreshold;
        _minSentencesPerChunk = minSentencesPerChunk;
        _maxSentencesPerChunk = maxSentencesPerChunk;
    }

    public async IAsyncEnumerable<DocumentChunk> ChunkAsync(
        Document document,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(document.Text))
        {
            yield break;
        }

        var text = document.Text;
        var sentenceSpans = ExtractSentenceSpans(text);

        if (sentenceSpans.Count == 0)
        {
            yield break;
        }

        // If only 1 sentence, emit directly
        if (sentenceSpans.Count == 1)
        {
            var s = sentenceSpans[0];
            yield return ChunkBuilder.Create(
                document,
                0,
                text[s.Start..s.End],
                s.Start,
                s.End,
                new Dictionary<string, string> { ["sentences_count"] = "1" });
            yield break;
        }

        // Generate temporary sentence chunks for embedding
        var sentenceChunks = new List<DocumentChunk>(sentenceSpans.Count);
        for (var i = 0; i < sentenceSpans.Count; i++)
        {
            var span = sentenceSpans[i];
            var sentenceText = text[span.Start..span.End];
            sentenceChunks.Add(new DocumentChunk(
                $"{document.Id}#sent_{i}",
                document.Id,
                i,
                sentenceText,
                startCharOffset: span.Start,
                endCharOffset: span.End));
        }

        // Embed sentences
        var embeddings = new List<float[]>(sentenceSpans.Count);
        await foreach (var embedded in _embeddingService.EmbedAsync(ToAsyncEnumerable(sentenceChunks, cancellationToken), cancellationToken).ConfigureAwait(false))
        {
            embeddings.Add(embedded.Vector.ToArray());
        }

        // Compute similarities between consecutive sentences
        var breakPoints = new HashSet<int>();
        for (var i = 0; i < embeddings.Count - 1; i++)
        {
            var sim = CosineKernel.CosineSimilarity(embeddings[i], embeddings[i + 1]);
            if (sim < _similarityThreshold)
            {
                breakPoints.Add(i + 1); // Boundary starts at sentence i+1
            }
        }

        // Form chunks respecting min and max sentence constraints
        var chunkIndex = 0;
        var startIdx = 0;

        while (startIdx < sentenceSpans.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var endIdx = startIdx + 1;
            while (endIdx < sentenceSpans.Count)
            {
                var sentenceCount = endIdx - startIdx;

                if (sentenceCount >= _maxSentencesPerChunk)
                {
                    break;
                }

                if (sentenceCount >= _minSentencesPerChunk && breakPoints.Contains(endIdx))
                {
                    break;
                }

                endIdx++;
            }

            var chunkStart = sentenceSpans[startIdx].Start;
            var chunkEnd = sentenceSpans[endIdx - 1].End;
            var chunkText = text[chunkStart..chunkEnd].Trim();
            var count = endIdx - startIdx;

            var meta = new Dictionary<string, string>
            {
                ["sentences_count"] = count.ToString(CultureInfo.InvariantCulture)
            };

            yield return ChunkBuilder.Create(
                document,
                chunkIndex++,
                chunkText,
                chunkStart,
                chunkEnd,
                meta);

            startIdx = endIdx;
        }
    }

    private static async IAsyncEnumerable<DocumentChunk> ToAsyncEnumerable(
        IEnumerable<DocumentChunk> items,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
        }
        await Task.CompletedTask;
    }

    private static List<(int Start, int End)> ExtractSentenceSpans(string text)
    {
        var result = new List<(int Start, int End)>();
        var matches = SentenceSplitRegex().Matches(text);

        var currentStart = 0;
        foreach (Match match in matches)
        {
            var end = match.Index;
            if (end > currentStart)
            {
                var actualStart = currentStart;
                while (actualStart < end && char.IsWhiteSpace(text[actualStart]))
                {
                    actualStart++;
                }

                var actualEnd = end;
                while (actualEnd > actualStart && char.IsWhiteSpace(text[actualEnd - 1]))
                {
                    actualEnd--;
                }

                if (actualEnd > actualStart)
                {
                    result.Add((actualStart, actualEnd));
                }
            }

            currentStart = match.Index + match.Length;
        }

        if (currentStart < text.Length)
        {
            var actualStart = currentStart;
            while (actualStart < text.Length && char.IsWhiteSpace(text[actualStart]))
            {
                actualStart++;
            }

            var actualEnd = text.Length;
            while (actualEnd > actualStart && char.IsWhiteSpace(text[actualEnd - 1]))
            {
                actualEnd--;
            }

            if (actualEnd > actualStart)
            {
                result.Add((actualStart, actualEnd));
            }
        }

        return result;
    }
}
