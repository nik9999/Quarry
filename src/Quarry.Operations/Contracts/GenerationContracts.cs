using System.Text.Json;
using System.Text.RegularExpressions;
using Quarry.Core.Documents;

namespace Quarry.Generation
{
    /// <summary>The grounded answer plus its citations and telemetry.</summary>
    public sealed record RagResponse(
        string Answer,
        IReadOnlyList<RetrievalResult> Sources,
        long LatencyMs,
        string Strategy);

    /// <summary>A streaming-event variant of <see cref="RagResponse"/>.</summary>
    public sealed record RagStreamEvent(
        RagStreamEventKind Kind,
        string? Token = null,
        IReadOnlyList<RetrievalResult>? Sources = null);

    public enum RagStreamEventKind { Sources, Token, Done, Error }

    /// <summary>
    /// The decorator seam over the RAG pipeline.
    /// </summary>
    public interface IRagPipeline
    {
        /// <summary>Answer <paramref name="question"/> in one shot, returning the grounded answer plus citations.</summary>
        Task<RagResponse> AskAsync(string question, CancellationToken ct = default);

        /// <summary>
        /// Answer <paramref name="question"/> as a stream of
        /// <see cref="RagStreamEvent"/>s: <c>Sources</c> first, then token events,
        /// then <c>Done</c>.
        /// </summary>
        IAsyncEnumerable<RagStreamEvent> AskStreamingAsync(string question, CancellationToken ct = default);
    }
}

namespace Quarry.Generation.Citations
{
    /// <summary>One claim in the answer paired with its supporting source.</summary>
    public sealed record Citation(
        string ClaimText,
        int SourceIndex,
        string ChunkId,
        string DocumentId,
        string Title,
        string Confidence);

    /// <summary>The grounded answer plus its extracted citations.</summary>
    public sealed record GroundedAnswer(
        string Answer,
        IReadOnlyList<Citation> Citations,
        double FaithfulnessScore);

    /// <summary>
    /// Extracts <c>[Source N]</c> markers from a generated answer and maps each
    /// back to the corresponding chunk in the supplied source list.
    /// </summary>
    public sealed partial class CitationPipeline
    {
        [GeneratedRegex(@"\[Source (\d+)\]")]
        private static partial Regex CitationMarker();

        public static IReadOnlyList<Citation> ExtractCitations(
            string answer,
            IReadOnlyList<RetrievalResult> sources,
            string defaultConfidence = "SUPPORTED")
        {
            ArgumentNullException.ThrowIfNull(answer);
            ArgumentNullException.ThrowIfNull(sources);

            var citations = new List<Citation>();
            var matches = CitationMarker().Matches(answer);
            foreach (Match m in matches)
            {
                if (!int.TryParse(m.Groups[1].Value,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var idx))
                {
                    continue;
                }
                if (idx < 1 || idx > sources.Count)
                {
                    continue;
                }
                var src = sources[idx - 1];
                var claim = ExtractSurroundingSentence(answer, m.Index);
                citations.Add(new Citation(
                    ClaimText: claim,
                    SourceIndex: idx,
                    ChunkId: src.Chunk.ChunkId,
                    DocumentId: src.Chunk.DocumentId,
                    Title: src.Chunk.DocMetadata?.Title ?? (src.Chunk.Metadata != null && src.Chunk.Metadata.TryGetValue("title", out var title) ? title : string.Empty),
                    Confidence: defaultConfidence));
            }
            return citations;
        }

        private static string ExtractSurroundingSentence(string text, int markerIndex)
        {
            int start = markerIndex;
            while (start > 0 && text[start - 1] != '.' && text[start - 1] != '\n')
            {
                start--;
            }

            int end = markerIndex;
            while (end < text.Length && text[end] != '.' && text[end] != '\n')
            {
                end++;
            }

            return text[start..end].Trim();
        }
    }

    /// <summary>One row of the EU AI Act / GDPR audit log.</summary>
    public sealed record AuditEntry(
        DateTimeOffset TimestampUtc,
        string Query,
        IReadOnlyList<string> RetrievedChunkIds,
        string Response,
        IReadOnlyList<Citation> Citations,
        double FaithfulnessScore,
        string? UserId);

    /// <summary>
    /// EU AI Act-aware audit logger.
    /// </summary>
    public sealed class AuditLogger
    {
        private readonly Func<AuditEntry, Task> _sink;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

        public AuditLogger(Func<AuditEntry, Task> sink)
        {
            ArgumentNullException.ThrowIfNull(sink);
            _sink = sink;
        }

        public Task LogAsync(
            string query,
            IReadOnlyList<RetrievalResult> retrievedSources,
            string response,
            IReadOnlyList<Citation> citations,
            double faithfulnessScore,
            string? userId = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(query);
            ArgumentNullException.ThrowIfNull(retrievedSources);
            ArgumentNullException.ThrowIfNull(response);
            ArgumentNullException.ThrowIfNull(citations);

            var entry = new AuditEntry(
                TimestampUtc: DateTimeOffset.UtcNow,
                Query: query,
                RetrievedChunkIds: [.. retrievedSources.Select(s => s.Chunk.ChunkId)],
                Response: response,
                Citations: citations,
                FaithfulnessScore: faithfulnessScore,
                UserId: userId);
            return _sink(entry);
        }

        /// <summary>Convenience: append-only JSONL file sink.</summary>
        public static Func<AuditEntry, Task> JsonlFileSink(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            var lockObj = new Lock();
            return entry =>
            {
                var line = JsonSerializer.Serialize(entry, JsonOptions);
                lock (lockObj)
                {
                    File.AppendAllText(path, line + Environment.NewLine);
                }
                return Task.CompletedTask;
            };
        }
    }
}
