namespace Quarry.Ingestion.Chunking;

using System.Runtime.CompilerServices;
using Quarry.Core.Abstractions;
using Quarry.Core.Documents;

/// <summary>
/// Рекурсивное разбиение текста по иерархическому набору разделителей (абзацы, переводы строк, пробелы, символы).
/// </summary>
public sealed class RecursiveCharacterChunker : IChunker
{
    private static readonly string[] DefaultSeparators = ["\n\n", "\r\n\r\n", "\n", "\r\n", " ", ""];

    private readonly int _chunkSize;
    private readonly int _chunkOverlap;
    private readonly string[] _separators;

    public string Strategy => "recursive";
    public int ChunkSize => _chunkSize;
    public int ChunkOverlap => _chunkOverlap;

    public RecursiveCharacterChunker(
        int chunkSize = 500,
        int chunkOverlap = 50,
        IReadOnlyList<string>? separators = null)
    {
        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be greater than zero.");
        }

        if (chunkOverlap < 0 || chunkOverlap >= chunkSize)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkOverlap), "Chunk overlap must be non-negative and less than chunk size.");
        }

        _chunkSize = chunkSize;
        _chunkOverlap = chunkOverlap;
        _separators = separators?.ToArray() ?? DefaultSeparators;
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
        var initialSegment = new Segment(0, text.Length, text);
        var splits = SplitText(initialSegment, 0);
        var merged = MergeSplits(splits, text);

        var index = 0;
        foreach (var chunk in merged)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunkText = text[chunk.Start..chunk.End].Trim();
            if (chunkText.Length > 0)
            {
                yield return ChunkBuilder.Create(
                    document,
                    index++,
                    chunkText,
                    chunk.Start,
                    chunk.End);
            }
        }
    }

    private List<Segment> SplitText(Segment segment, int separatorIndex)
    {
        var result = new List<Segment>();
        if (segment.End - segment.Start <= _chunkSize)
        {
            result.Add(segment);
            return result;
        }

        if (separatorIndex >= _separators.Length)
        {
            // No more separators available, force-split by characters
            var pos = segment.Start;
            while (pos < segment.End)
            {
                var end = Math.Min(pos + _chunkSize, segment.End);
                result.Add(new Segment(pos, end, segment.FullText));
                pos = end;
            }

            return result;
        }

        var separator = _separators[separatorIndex];
        if (string.IsNullOrEmpty(separator))
        {
            // Character level split
            return SplitText(segment, separatorIndex + 1);
        }

        var pieces = SplitBySeparator(segment, separator);
        if (pieces.Count <= 1)
        {
            // Separator wasn't found or only one piece, try next separator
            return SplitText(segment, separatorIndex + 1);
        }

        foreach (var piece in pieces)
        {
            if (piece.End - piece.Start > _chunkSize)
            {
                result.AddRange(SplitText(piece, separatorIndex + 1));
            }
            else
            {
                result.Add(piece);
            }
        }

        return result;
    }

    private static List<Segment> SplitBySeparator(Segment segment, string separator)
    {
        var pieces = new List<Segment>();
        var text = segment.FullText;
        var start = segment.Start;

        while (start < segment.End)
        {
            var nextIndex = text.IndexOf(separator, start, segment.End - start, StringComparison.Ordinal);
            if (nextIndex < 0)
            {
                pieces.Add(new Segment(start, segment.End, text));
                break;
            }

            var pieceEnd = nextIndex + separator.Length;
            pieces.Add(new Segment(start, pieceEnd, text));
            start = pieceEnd;
        }

        return pieces;
    }

    private List<(int Start, int End)> MergeSplits(List<Segment> splits, string fullText)
    {
        var result = new List<(int Start, int End)>();
        if (splits.Count == 0)
        {
            return result;
        }

        var currentStart = splits[0].Start;
        var currentEnd = splits[0].End;

        for (var i = 1; i < splits.Count; i++)
        {
            var next = splits[i];
            var potentialLength = next.End - currentStart;

            if (potentialLength <= _chunkSize)
            {
                currentEnd = next.End;
            }
            else
            {
                result.Add((currentStart, currentEnd));

                // Find rewind point for overlap
                var overlapTarget = currentEnd - _chunkOverlap;
                var rewindIndex = i;
                while (rewindIndex > 0 && splits[rewindIndex - 1].Start >= overlapTarget)
                {
                    rewindIndex--;
                }

                currentStart = splits[rewindIndex].Start;
                currentEnd = next.End;
            }
        }

        if (currentEnd > currentStart)
        {
            result.Add((currentStart, currentEnd));
        }

        return result;
    }

    private readonly record struct Segment(int Start, int End, string FullText);
}
