namespace Quarry.Retrieval.Tests.Ranking;

using Quarry.Core.Abstractions;
using Quarry.Core.Documents;
using Quarry.Retrieval;
using Xunit;

public class RankingAndFiltersTests
{
    private static readonly float[] CandidateVec1 = [1.0f, 0.0f];
    private static readonly float[] CandidateVec2 = [0.99f, 0.01f];
    private static readonly float[] CandidateVec3 = [0.0f, 1.0f];

    private sealed class FakeRetriever : IRetriever
    {
        private readonly IReadOnlyList<RetrievalResult> _results;
        public string Strategy => "fake";

        public FakeRetriever(IReadOnlyList<RetrievalResult> results) => _results = results;

        public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(string query, int topK, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<RetrievalResult>>(_results.Take(topK).ToList());
        }
    }

    [Fact]
    public void RrfMerger_CombinesListsWithReciprocalRank()
    {
        var merger = new RrfMerger(k: 60);

        var listA = new List<RetrievalResult>
        {
            new(new DocumentChunk("c-1", "d-1", 0, "Doc 1"), 0.9),
            new(new DocumentChunk("c-2", "d-1", 1, "Doc 2"), 0.8)
        };

        var listB = new List<RetrievalResult>
        {
            new(new DocumentChunk("c-2", "d-1", 1, "Doc 2"), 0.85),
            new(new DocumentChunk("c-3", "d-1", 2, "Doc 3"), 0.7)
        };

        var fused = merger.Merge(listA, listB, topK: 3);

        Assert.Equal(3, fused.Count);
        // c-2 is present in both lists (rank 1 and rank 0) -> highest score
        Assert.Equal("c-2", fused[0].Chunk.ChunkId);
        var expectedScoreC2 = (1.0 / (60 + 2)) + (1.0 / (60 + 1));
        Assert.Equal(expectedScoreC2, fused[0].Score, precision: 5);
    }

    [Fact]
    public void MmrSelector_PenalizesSimilarCandidates()
    {
        var candidates = new List<MmrCandidate>
        {
            new(new DocumentChunk("c-1", "d-1", 0, "Первый"), CandidateVec1, 0.95),
            new(new DocumentChunk("c-2", "d-1", 1, "Почти копия первого"), CandidateVec2, 0.94),
            new(new DocumentChunk("c-3", "d-1", 2, "Разнообразный ортогональный"), CandidateVec3, 0.80)
        };

        var selected = MmrSelector.Select(candidates, k: 2, lambda: 0.5);

        Assert.Equal(2, selected.Count);
        Assert.Equal("c-1", selected[0].Chunk.ChunkId);
        // Due to diversity penalty on c-2, c-3 should be chosen instead of c-2!
        Assert.Equal("c-3", selected[1].Chunk.ChunkId);
    }

    [Fact]
    public async Task MinScoreFilter_DropsLowRelevanceResults()
    {
        var chunks = new List<RetrievalResult>
        {
            new(new DocumentChunk("c-high", "d-1", 0, "High relevance"), 0.85),
            new(new DocumentChunk("c-low", "d-1", 1, "Low relevance noise"), 0.25)
        };

        var inner = new FakeRetriever(chunks);
        var filter = new MinScoreFilter(inner, minScore: 0.50);

        var results = await filter.RetrieveAsync("test", 10);

        Assert.Single(results);
        Assert.Equal("c-high", results[0].Chunk.ChunkId);
    }

    [Fact]
    public void ContextOrdering_InterleavesRanksForLostInTheMiddle()
    {
        var ranked = new List<RetrievalResult>
        {
            new(new DocumentChunk("c-1", "d-1", 0, "Rank 1"), 0.9),
            new(new DocumentChunk("c-2", "d-1", 1, "Rank 2"), 0.8),
            new(new DocumentChunk("c-3", "d-1", 2, "Rank 3"), 0.7),
            new(new DocumentChunk("c-4", "d-1", 3, "Rank 4"), 0.6),
            new(new DocumentChunk("c-5", "d-1", 4, "Rank 5"), 0.5)
        };

        var interleaved = ContextOrdering.Interleave(ranked);

        Assert.Equal(5, interleaved.Count);
        // Expect order: [c-1, c-3, c-5, c-4, c-2]
        Assert.Equal("c-1", interleaved[0].Chunk.ChunkId);
        Assert.Equal("c-3", interleaved[1].Chunk.ChunkId);
        Assert.Equal("c-5", interleaved[2].Chunk.ChunkId);
        Assert.Equal("c-4", interleaved[3].Chunk.ChunkId);
        Assert.Equal("c-2", interleaved[4].Chunk.ChunkId);
    }
}
