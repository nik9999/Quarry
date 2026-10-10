namespace Quarry.Ingestion.Chunking;

using System.Runtime.CompilerServices;
using Quarry.Core.Abstractions;
using Quarry.Core.Documents;

/// <summary>
/// Стратегия разбиения текста на чанки фиксированного размера символов с перекрытием.
/// </summary>
public sealed class FixedSizeChunker : IChunker
{
    private readonly int _chunkSize;
    private readonly int _overlap;

    public string Strategy => "fixed";
    public int ChunkSize => _chunkSize;
    public int Overlap => _overlap;

    public FixedSizeChunker(int chunkSize = 512, int overlap = 64)
    {
        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be greater than zero.");
        }

        if (overlap < 0 || overlap >= chunkSize)
        {
            throw new ArgumentOutOfRangeException(nameof(overlap), "Overlap must be non-negative and less than chunk size.");
        }

        _chunkSize = chunkSize;
        _overlap = overlap;
    }

    public async IAsyncEnumerable<DocumentChunk> ChunkAsync(
        Document document,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrEmpty(document.Text))
        {
            yield break;
        }

        await Task.Yield();

        var text = document.Text;
        var step = _chunkSize - _overlap;
        var index = 0;
        var pos = 0;

        while (pos < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var length = Math.Min(_chunkSize, text.Length - pos);
            var chunkText = text.Substring(pos, length);

            yield return ChunkBuilder.Create(
                document,
                index++,
                chunkText,
                pos,
                pos + length);

            if (pos + length >= text.Length)
            {
                break;
            }

            pos += step;
        }
    }
}
