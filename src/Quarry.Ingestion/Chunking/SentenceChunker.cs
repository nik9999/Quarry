namespace Quarry.Ingestion.Chunking;

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Quarry.Core.Abstractions;
using Quarry.Core.Documents;

/// <summary>
/// Стратегия разбиения текста на предложения с агрегацией по максимальному размеру чанка и перекрытием предложений.
/// </summary>
public sealed partial class SentenceChunker : IChunker
{
    private readonly int _maxChunkSize;
    private readonly int _overlapSentences;

    public string Strategy => "sentence";
    public int MaxChunkSize => _maxChunkSize;
    public int OverlapSentences => _overlapSentences;

    [GeneratedRegex(@"(?<=[.!?])\s+|\r?\n\r?\n|\r?\n", RegexOptions.Compiled)]
    private static partial Regex SentenceSplitRegex();

    public SentenceChunker(int maxChunkSize = 1000, int overlapSentences = 1)
    {
        if (maxChunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxChunkSize), "Max chunk size must be greater than zero.");
        }

        if (overlapSentences < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(overlapSentences), "Overlap sentences must be non-negative.");
        }

        _maxChunkSize = maxChunkSize;
        _overlapSentences = overlapSentences;
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

        await Task.Yield();

        var text = document.Text;
        var sentences = ExtractSentences(text);

        if (sentences.Count == 0)
        {
            yield break;
        }

        var chunkIndex = 0;
        var i = 0;

        while (i < sentences.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var startSentence = i;
            var currentLen = 0;
            var endSentence = i;

            while (endSentence < sentences.Count)
            {
                var s = sentences[endSentence];
                var sentenceLen = s.End - s.Start;

                if (endSentence > startSentence && currentLen + sentenceLen > _maxChunkSize)
                {
                    break;
                }

                currentLen += sentenceLen;
                endSentence++;
            }

            var chunkStart = sentences[startSentence].Start;
            var chunkEnd = sentences[endSentence - 1].End;
            var chunkText = text[chunkStart..chunkEnd].Trim();

            if (chunkText.Length > 0)
            {
                yield return ChunkBuilder.Create(
                    document,
                    chunkIndex++,
                    chunkText,
                    chunkStart,
                    chunkEnd);
            }

            if (endSentence >= sentences.Count)
            {
                break;
            }

            // Step forward taking overlap sentences into account
            var step = Math.Max(1, (endSentence - startSentence) - _overlapSentences);
            i = startSentence + step;
        }
    }

    private static List<(int Start, int End)> ExtractSentences(string text)
    {
        var result = new List<(int Start, int End)>();
        var matches = SentenceSplitRegex().Matches(text);

        var currentStart = 0;
        foreach (Match match in matches)
        {
            var end = match.Index;
            if (end > currentStart)
            {
                // Trim leading whitespace from currentStart
                var actualStart = currentStart;
                while (actualStart < end && char.IsWhiteSpace(text[actualStart]))
                {
                    actualStart++;
                }

                // Trim trailing whitespace from end
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
