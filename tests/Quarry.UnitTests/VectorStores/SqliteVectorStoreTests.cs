using Quarry.Core.Documents;
using Quarry.Core.Filtering;
using Quarry.Retrieval.VectorStores;
using Xunit;

namespace Quarry.Retrieval.Tests.VectorStores;

public class SqliteVectorStoreTests
{
    private static SqliteVectorStore CreateInMemoryStore(string collection = "test_chunks", int dimensions = 4)
    {
        var options = new SqliteVectorStoreOptions
        {
            ConnectionString = "Data Source=:memory:",
            CollectionName = collection,
            Dimensions = dimensions,
            AutoEnsureCollection = true
        };

        return new SqliteVectorStore(options);
    }

    [Fact]
    public async Task EnsureCollectionExistsAsync_Idempotent_CanBeCalledMultipleTimes()
    {
        using var store = CreateInMemoryStore();

        await store.EnsureCollectionExistsAsync();
        await store.EnsureCollectionExistsAsync(); // Should not throw
    }

    [Fact]
    public async Task UpsertAndSearch_BasicKnn_ReturnsRankedResults()
    {
        using var store = CreateInMemoryStore();

        var chunk1 = new DocumentChunk("c-1", "doc-1", 0, "Текст один");
        var chunk2 = new DocumentChunk("c-2", "doc-1", 1, "Текст два");
        var chunk3 = new DocumentChunk("c-3", "doc-2", 0, "Текст три");

        // Векторы 4 измерений (нормализованные)
        float[] v1 = [1.0f, 0.0f, 0.0f, 0.0f];
        float[] v2 = [0.0f, 1.0f, 0.0f, 0.0f];
        float[] v3 = [0.7071f, 0.7071f, 0.0f, 0.0f];

        var embeddedList = new List<EmbeddedChunk>
        {
            new(chunk1, v1, "model-test"),
            new(chunk2, v2, "model-test"),
            new(chunk3, v3, "model-test")
        };

        await store.UpsertAsync(embeddedList);

        // Поиск вектора, совпадающего с v1
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];
        var results = await store.SearchAsync(query, topK: 2);

        Assert.Equal(2, results.Count);
        // ТОП-1 должен быть c-1 со сходством ~1.0
        Assert.Equal("c-1", results[0].ChunkId);
        Assert.Equal(1.0, results[0].Score, precision: 3);
        Assert.Equal("Текст один", results[0].Text);

        // ТОП-2 должен быть c-3 со сходством ~0.707
        Assert.Equal("c-3", results[1].ChunkId);
        Assert.True(results[1].Score > 0.7);
    }

    [Fact]
    public async Task SearchAsync_WithMetadataFilter_ReturnsOnlyMatchingChunks()
    {
        using var store = CreateInMemoryStore();

        var chunkA = new DocumentChunk("c-a", "doc-1", 0, "Чанк A", new Dictionary<string, string> { ["tag"] = "alpha" });
        var chunkB = new DocumentChunk("c-b", "doc-2", 0, "Чанк B", new Dictionary<string, string> { ["tag"] = "beta" });

        float[] vA = [1.0f, 0.0f, 0.0f, 0.0f];
        float[] vB = [0.9f, 0.1f, 0.0f, 0.0f];

        await store.UpsertAsync(new[]
        {
            new EmbeddedChunk(chunkA, vA, "model"),
            new EmbeddedChunk(chunkB, vB, "model")
        });

        // Запрос ближе к chunkA, но мы фильтруем по tag = beta
        float[] query = [1.0f, 0.0f, 0.0f, 0.0f];
        var filter = new MetadataFilter("tag", "beta");

        var results = await store.SearchAsync(query, topK: 5, filter: filter);

        Assert.Single(results);
        Assert.Equal("c-b", results[0].ChunkId);
        Assert.Equal("Чанк B", results[0].Text);
    }

    [Fact]
    public async Task UpsertAsync_DimensionMismatch_ThrowsArgumentException()
    {
        using var store = CreateInMemoryStore(dimensions: 4);

        var chunk = new DocumentChunk("c-1", "doc-1", 0, "текст");
        float[] wrongDimVector = [1.0f, 2.0f]; // 2 dimensions instead of 4

        var embedded = new EmbeddedChunk(chunk, wrongDimVector, "model");

        await Assert.ThrowsAsync<ArgumentException>(() => store.UpsertAsync(new[] { embedded }));
    }

    [Fact]
    public async Task SearchAsync_DimensionMismatch_ThrowsArgumentException()
    {
        using var store = CreateInMemoryStore(dimensions: 4);

        float[] wrongQuery = [1.0f, 2.0f, 3.0f]; // 3 instead of 4

        await Assert.ThrowsAsync<ArgumentException>(() => store.SearchAsync(wrongQuery, topK: 1));
    }

    [Fact]
    public async Task UpsertAsync_UpdateExistingChunk_ReplacesTextAndEmbedding()
    {
        using var store = CreateInMemoryStore();

        var initialChunk = new DocumentChunk("c-1", "doc-1", 0, "Начальный текст");
        float[] v1 = [1.0f, 0.0f, 0.0f, 0.0f];
        await store.UpsertAsync(new[] { new EmbeddedChunk(initialChunk, v1, "model") });

        // Обновляем чанк c-1
        var updatedChunk = new DocumentChunk("c-1", "doc-1", 0, "Обновленный текст");
        float[] v2 = [0.0f, 1.0f, 0.0f, 0.0f];
        await store.UpsertAsync(new[] { new EmbeddedChunk(updatedChunk, v2, "model") });

        // Поиск по новому вектору v2
        var results = await store.SearchAsync(v2, topK: 1);

        Assert.Single(results);
        Assert.Equal("c-1", results[0].ChunkId);
        Assert.Equal("Обновленный текст", results[0].Text);
        Assert.Equal(1.0, results[0].Score, precision: 3);
    }

    [Fact]
    public async Task DeleteAsync_RemovesChunksFromBothTables()
    {
        using var store = CreateInMemoryStore();

        var chunk1 = new DocumentChunk("c-1", "doc-1", 0, "Первый");
        var chunk2 = new DocumentChunk("c-2", "doc-2", 0, "Второй");

        float[] v = [1.0f, 0.0f, 0.0f, 0.0f];
        await store.UpsertAsync(new[]
        {
            new EmbeddedChunk(chunk1, v, "model"),
            new EmbeddedChunk(chunk2, v, "model")
        });

        // Удаляем c-1
        await store.DeleteAsync(["c-1"]);

        var results = await store.SearchAsync(v, topK: 5);

        Assert.Single(results);
        Assert.Equal("c-2", results[0].ChunkId);
    }

    [Fact]
    public async Task FilePersistence_DataSurvivesStoreReopen()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"quarry_test_{Guid.NewGuid():N}.db");
        try
        {
            var options = new SqliteVectorStoreOptions
            {
                ConnectionString = $"Data Source={tempDb}",
                CollectionName = "persisted_chunks",
                Dimensions = 4
            };

            // 1. Первая сессия: создание и запись
            using (var store1 = new SqliteVectorStore(options))
            {
                await store1.EnsureCollectionExistsAsync();
                var chunk = new DocumentChunk("c-p", "doc-p", 0, "Сохраненный текст");
                float[] vec = [0.0f, 0.0f, 1.0f, 0.0f];
                await store1.UpsertAsync(new[] { new EmbeddedChunk(chunk, vec, "model") });
            }

            // 2. Вторая сессия: открытие существующего файла и чтение
            using (var store2 = new SqliteVectorStore(options))
            {
                float[] query = [0.0f, 0.0f, 1.0f, 0.0f];
                var results = await store2.SearchAsync(query, topK: 1);

                Assert.Single(results);
                Assert.Equal("c-p", results[0].ChunkId);
                Assert.Equal("Сохраненный текст", results[0].Text);
                Assert.Equal(1.0, results[0].Score, precision: 3);
            }
        }
        finally
        {
            if (File.Exists(tempDb))
            {
                try { File.Delete(tempDb); } catch { /* ignore */ }
            }
        }
    }

    [Fact]
    public async Task DisposedStore_ThrowsObjectDisposedException()
    {
        var store = CreateInMemoryStore();
        store.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.EnsureCollectionExistsAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.SearchAsync(new float[4], 1));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.DeleteAsync(["id"]));
    }
}
