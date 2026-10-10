namespace Quarry.Retrieval.Tests.Retrievers;

using Quarry.Core.Abstractions;
using Quarry.Core.Documents;
using Quarry.Retrieval;
using Quarry.Retrieval.Tests.Fakes;
using Quarry.Retrieval.VectorStores;
using Xunit;

public class RetrieversTests
{
    private static readonly float[] Vector3D = [1.0f, 0.0f, 0.0f];
    private static readonly float[] Vector2DA = [1.0f, 0.0f];
    private static readonly float[] Vector2DB = [0.5f, 0.5f];

    [Fact]
    public async Task DenseRetriever_QueriesEmbeddingsAndVectorStore()
    {
        var embeddings = new FakeEmbeddingService(dimensions: 3);
        var store = new InMemoryVectorStore();

        var chunk = new DocumentChunk("c-1", "d-1", 0, "Тестовый документ");
        await store.UpsertAsync([new EmbeddedChunk(chunk, Vector3D, "model")]);

        var retriever = new DenseRetriever(embeddings, store);
        var results = await retriever.RetrieveAsync("Тестовый запрос", 5);

        Assert.Single(results);
        Assert.Equal("c-1", results[0].Chunk.ChunkId);
        Assert.Equal("Тестовый запрос", embeddings.LastQuery);
    }

    [Fact]
    public async Task SparseRetriever_BM25_RanksByKeywordRelevance()
    {
        var retriever = new SparseRetriever();
        var chunks = new List<DocumentChunk>
        {
            new("c-1", "d-1", 0, "Векторный поиск и базы данных"),
            new("c-2", "d-1", 1, "Архитектура искусственного интеллекта и нейросети"),
            new("c-3", "d-1", 2, "Базы данных реляционные и векторный индекс в базах данных")
        };

        retriever.Index(chunks);

        var results = await retriever.RetrieveAsync("базы данных", 2);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Chunk.ChunkId == "c-3");
        Assert.True(results[0].Score >= results[1].Score);
    }

    [Fact]
    public async Task HybridRetriever_FusesDenseAndSparseResults()
    {
        var embeddings = new FakeEmbeddingService(dimensions: 2);
        var store = new InMemoryVectorStore();
        var chunk1 = new DocumentChunk("c-1", "d-1", 0, "Контекст один");
        var chunk2 = new DocumentChunk("c-2", "d-1", 1, "Контекст два");

        await store.UpsertAsync([
            new EmbeddedChunk(chunk1, Vector2DA, "model"),
            new EmbeddedChunk(chunk2, Vector2DB, "model")
        ]);

        var dense = new DenseRetriever(embeddings, store);
        var sparse = new SparseRetriever();
        sparse.Index([chunk1, chunk2]);

        var hybrid = new HybridRetriever(dense, sparse);
        var results = await hybrid.RetrieveAsync("Контекст", 2);

        Assert.Equal(2, results.Count);
        Assert.Equal("hybrid(dense+sparse-bm25)", hybrid.Strategy);
    }

    [Fact]
    public void RetrieverModeFactory_CreatesConfiguredRetriever()
    {
        var embeddings = new FakeEmbeddingService();
        var store = new InMemoryVectorStore();
        var dense = new DenseRetriever(embeddings, store);
        var sparse = new SparseRetriever();

        var retDense = RetrieverModeFactory.Create("dense", dense, sparse);
        var retSparse = RetrieverModeFactory.Create("sparse", dense, sparse);
        var retHybrid = RetrieverModeFactory.Create("hybrid", dense, sparse);

        Assert.Equal("dense", retDense.Strategy);
        Assert.Equal("sparse-bm25", retSparse.Strategy);
        Assert.Equal("hybrid(dense+sparse-bm25)", retHybrid.Strategy);

        Assert.Throws<ArgumentException>(() => RetrieverModeFactory.Create("unknown_mode", dense, sparse));
    }
}
