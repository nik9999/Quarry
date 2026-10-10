namespace Quarry.Ingestion.Chunking;

using System.Runtime.CompilerServices;
using Microsoft.ML.Tokenizers;
using Quarry.Core.Abstractions;
using Quarry.Core.Documents;

/// <summary>
/// Стратегия разбиения текста на чанки с контролем бюджета токенов на основе токенизатора.
/// </summary>
public sealed class TokenAwareChunker : IChunker
{
    private readonly Tokenizer _tokenizer;
    private readonly int _maxTokens;
    private readonly int _tokenOverlap;

    public string Strategy => "token";
    public int MaxTokens => _maxTokens;
    public int TokenOverlap => _tokenOverlap;

    public TokenAwareChunker(Tokenizer tokenizer, int maxTokens = 256, int tokenOverlap = 32)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);

        if (maxTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTokens), "Max tokens must be greater than zero.");
        }

        if (tokenOverlap < 0 || tokenOverlap >= maxTokens)
        {
            throw new ArgumentOutOfRangeException(nameof(tokenOverlap), "Token overlap must be non-negative and less than max tokens.");
        }

        _tokenizer = tokenizer;
        _maxTokens = maxTokens;
        _tokenOverlap = tokenOverlap;
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
        var tokens = _tokenizer.EncodeToTokens(text, out _);

        if (tokens.Count == 0)
        {
            yield break;
        }

        var step = _maxTokens - _tokenOverlap;
        var chunkIndex = 0;
        var pos = 0;

        while (pos < tokens.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var count = Math.Min(_maxTokens, tokens.Count - pos);
            var firstToken = tokens[pos];
            var lastToken = tokens[pos + count - 1];

            var startOffset = firstToken.Offset.Start.Value;
            var endOffset = lastToken.Offset.End.Value;

            // Safe slice boundaries
            startOffset = Math.Clamp(startOffset, 0, text.Length);
            endOffset = Math.Clamp(endOffset, startOffset, text.Length);

            var chunkText = text[startOffset..endOffset].Trim();

            if (chunkText.Length > 0)
            {
                var additionalMeta = new Dictionary<string, string>
                {
                    ["token_count"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                };

                yield return ChunkBuilder.Create(
                    document,
                    chunkIndex++,
                    chunkText,
                    startOffset,
                    endOffset,
                    additionalMeta);
            }

            if (pos + count >= tokens.Count)
            {
                break;
            }

            pos += step;
        }
    }
}
