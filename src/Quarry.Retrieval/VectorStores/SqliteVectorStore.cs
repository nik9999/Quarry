using System.Data;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quarry.Core.Abstractions;
using Quarry.Core.Documents;
using Quarry.Core.Filtering;

namespace Quarry.Retrieval.VectorStores;

/// <summary>
/// Реализация векторного хранилища <see cref="IVectorStore"/> на базе встроенной СУБД SQLite
/// и нативного расширения sqlite-vec (vec0).
/// Хранит реляционные метаданные и тексты в стандартной таблице, а векторы — в виртуальной таблице vec0.
/// </summary>
public sealed class SqliteVectorStore : IVectorStore, IDisposable, IAsyncDisposable
{
    private static readonly Regex ValidCollectionNameRegex = new(@"^[a-zA-Z0-9_]+$", RegexOptions.Compiled);

    private readonly SqliteVectorStoreOptions _options;
    private readonly ILogger<SqliteVectorStore> _logger;
    private readonly string _resolvedExtensionPath;
    private readonly bool _isInMemory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private SqliteConnection? _inMemoryConnection;
    private bool _isInitialized;
    private bool _disposed;

    public string CollectionName => _options.CollectionName;

    public SqliteVectorStore(
        SqliteVectorStoreOptions options,
        ILogger<SqliteVectorStore>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<SqliteVectorStore>.Instance;

        if (string.IsNullOrWhiteSpace(_options.CollectionName) ||
            !ValidCollectionNameRegex.IsMatch(_options.CollectionName))
        {
            throw new ArgumentException(
                $"Имя коллекции '{_options.CollectionName}' некорректно. Разрешены только символы a-z, A-Z, 0-9 и '_'.",
                nameof(options));
        }

        if (_options.Dimensions <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                _options.Dimensions,
                "Размерность векторов должна быть строго положительным числом.");
        }

        _isInMemory = _options.ConnectionString.Contains(":memory:", StringComparison.OrdinalIgnoreCase);
        _resolvedExtensionPath = ResolveExtensionPath(_options.ExtensionPath);

        _logger.LogDebug(
            "SqliteVectorStore создан. Коллекция: {Collection}, Размерность: {Dimensions}, Расширение: {ExtensionPath}",
            _options.CollectionName, _options.Dimensions, _resolvedExtensionPath);
    }

    /// <inheritdoc />
    public async Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (conn, shouldDispose) = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_isInMemory)
                {
                    // WAL режим для параллельного чтения и высокой производительности транзакций
                    await using var pragmaCmd = conn.CreateCommand();
                    pragmaCmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
                    await pragmaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                // 1. Создание реляционной таблицы метаданных и текстов
                var chunksTableSql = $"""
                    CREATE TABLE IF NOT EXISTS "{_options.CollectionName}" (
                        chunk_id TEXT PRIMARY KEY,
                        document_id TEXT NOT NULL,
                        chunk_index INTEGER NOT NULL,
                        text TEXT NOT NULL,
                        metadata TEXT,
                        model TEXT NOT NULL,
                        created_at TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS "idx_{_options.CollectionName}_doc_id" 
                        ON "{_options.CollectionName}"(document_id);
                    """;

                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = chunksTableSql;
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                // 2. Создание виртуальной векторной таблицы vec0
                var vecTableSql = $"""
                    CREATE VIRTUAL TABLE IF NOT EXISTS "vec_{_options.CollectionName}" USING vec0(
                        chunk_id TEXT PRIMARY KEY,
                        embedding FLOAT[{_options.Dimensions}] DISTANCE_METRIC=cosine
                    );
                    """;

                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = vecTableSql;
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                _isInitialized = true;
                _logger.LogInformation("Коллекция '{Collection}' инициализирована.", _options.CollectionName);
            }
            finally
            {
                if (shouldDispose)
                {
                    await conn.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpsertAsync(IEnumerable<EmbeddedChunk> chunks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var chunkList = chunks as IReadOnlyList<EmbeddedChunk> ?? chunks.ToList();
        if (chunkList.Count == 0) return;

        if (_options.AutoEnsureCollection && !_isInitialized)
        {
            await EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);
        }

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (conn, shouldDispose) = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                var insertChunkSql = $"""
                    INSERT OR REPLACE INTO "{_options.CollectionName}" 
                    (chunk_id, document_id, chunk_index, text, metadata, model, created_at)
                    VALUES (@chunkId, @documentId, @chunkIndex, @text, @metadata, @model, @createdAt);
                    """;

                var deleteVecSql = $"""
                    DELETE FROM "vec_{_options.CollectionName}" WHERE chunk_id = @chunkId;
                    """;

                var insertVecSql = $"""
                    INSERT INTO "vec_{_options.CollectionName}" (chunk_id, embedding)
                    VALUES (@chunkId, @embedding);
                    """;

                await using var chunkCmd = conn.CreateCommand();
                chunkCmd.Transaction = tx;
                chunkCmd.CommandText = insertChunkSql;

                var pChunkId = chunkCmd.Parameters.Add("@chunkId", SqliteType.Text);
                var pDocId = chunkCmd.Parameters.Add("@documentId", SqliteType.Text);
                var pIndex = chunkCmd.Parameters.Add("@chunkIndex", SqliteType.Integer);
                var pText = chunkCmd.Parameters.Add("@text", SqliteType.Text);
                var pMeta = chunkCmd.Parameters.Add("@metadata", SqliteType.Text);
                var pModel = chunkCmd.Parameters.Add("@model", SqliteType.Text);
                var pCreated = chunkCmd.Parameters.Add("@createdAt", SqliteType.Text);

                await using var deleteVecCmd = conn.CreateCommand();
                deleteVecCmd.Transaction = tx;
                deleteVecCmd.CommandText = deleteVecSql;
                var pDeleteVecId = deleteVecCmd.Parameters.Add("@chunkId", SqliteType.Text);

                await using var vecCmd = conn.CreateCommand();
                vecCmd.Transaction = tx;
                vecCmd.CommandText = insertVecSql;

                var pVecChunkId = vecCmd.Parameters.Add("@chunkId", SqliteType.Text);
                var pEmbedding = vecCmd.Parameters.Add("@embedding", SqliteType.Blob);

                var now = DateTimeOffset.UtcNow.ToString("O");

                foreach (var chunk in chunkList)
                {
                    if (chunk.Vector.Length != _options.Dimensions)
                    {
                        throw new ArgumentException(
                            $"Размерность вектора чанка '{chunk.ChunkId}' ({chunk.Vector.Length}) не совпадает с размерностью коллекции ({_options.Dimensions}).");
                    }

                    pChunkId.Value = chunk.ChunkId;
                    pDocId.Value = chunk.DocumentId;
                    pIndex.Value = chunk.Chunk.Index;
                    pText.Value = chunk.Text;
                    pMeta.Value = chunk.Chunk.Metadata is { Count: > 0 }
                        ? JsonSerializer.Serialize(chunk.Chunk.Metadata)
                        : DBNull.Value;
                    pModel.Value = chunk.EmbeddingModel;
                    pCreated.Value = now;

                    await chunkCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                    // Для виртуальной таблицы vec0 выполняем DELETE перед INSERT для идемпотентного upsert
                    pDeleteVecId.Value = chunk.ChunkId;
                    await deleteVecCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                    pVecChunkId.Value = chunk.ChunkId;
                    pEmbedding.Value = MemoryMarshal.AsBytes(chunk.Vector.Span).ToArray();

                    await vecCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("Вставлено/обновлено {Count} чанков в коллекцию '{Collection}'.", chunkList.Count, _options.CollectionName);
            }
            finally
            {
                if (shouldDispose)
                {
                    await conn.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RetrievalResult>> SearchAsync(
        ReadOnlyMemory<float> queryVector,
        int topK,
        MetadataFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (topK <= 0) return Array.Empty<RetrievalResult>();

        if (queryVector.Length != _options.Dimensions)
        {
            throw new ArgumentException(
                $"Размерность вектора запроса ({queryVector.Length}) не совпадает с размерностью коллекции ({_options.Dimensions}).",
                nameof(queryVector));
        }

        if (_options.AutoEnsureCollection && !_isInitialized)
        {
            await EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);
        }

        var (conn, shouldDispose) = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = conn.CreateCommand();

            var queryBytes = MemoryMarshal.AsBytes(queryVector.Span).ToArray();
            cmd.Parameters.Add("@queryVector", SqliteType.Blob).Value = queryBytes;
            cmd.Parameters.Add("@topK", SqliteType.Integer).Value = topK;

            string sql;
            if (filter == null || filter.ExactMatches.Count == 0)
            {
                sql = $"""
                    SELECT
                        c.chunk_id,
                        c.document_id,
                        c.chunk_index,
                        c.text,
                        c.metadata,
                        v.distance
                    FROM "vec_{_options.CollectionName}" v
                    JOIN "{_options.CollectionName}" c ON c.chunk_id = v.chunk_id
                    WHERE v.embedding MATCH @queryVector AND k = @topK
                    ORDER BY v.distance;
                    """;
            }
            else
            {
                // Построение Pre-filter с фильтрацией по метаданным (json_extract) и document_id
                var filterConditions = new List<string>();
                int paramIndex = 0;

                foreach (var (key, value) in filter.ExactMatches)
                {
                    var keyParam = $"@fk_{paramIndex}";
                    var valParam = $"@fv_{paramIndex}";
                    cmd.Parameters.Add(keyParam, SqliteType.Text).Value = key;
                    cmd.Parameters.Add(valParam, SqliteType.Text).Value = value;

                    if (string.Equals(key, "document_id", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(key, "documentId", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(key, "doc_id", StringComparison.OrdinalIgnoreCase))
                    {
                        filterConditions.Add($"(document_id = {valParam} OR json_extract(metadata, '$.' || {keyParam}) = {valParam})");
                    }
                    else
                    {
                        filterConditions.Add($"json_extract(metadata, '$.' || {keyParam}) = {valParam}");
                    }

                    paramIndex++;
                }

                var whereClause = string.Join(" AND ", filterConditions);

                sql = $"""
                    SELECT
                        c.chunk_id,
                        c.document_id,
                        c.chunk_index,
                        c.text,
                        c.metadata,
                        v.distance
                    FROM "vec_{_options.CollectionName}" v
                    JOIN "{_options.CollectionName}" c ON c.chunk_id = v.chunk_id
                    WHERE v.embedding MATCH @queryVector AND k = @topK
                      AND v.chunk_id IN (
                          SELECT chunk_id FROM "{_options.CollectionName}"
                          WHERE {whereClause}
                      )
                    ORDER BY v.distance;
                    """;
            }

            cmd.CommandText = sql;

            var results = new List<RetrievalResult>(topK);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var chunkId = reader.GetString(0);
                var documentId = reader.GetString(1);
                var chunkIndex = reader.GetInt32(2);
                var text = reader.GetString(3);

                Dictionary<string, string>? metadata = null;
                if (!reader.IsDBNull(4))
                {
                    var json = reader.GetString(4);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    }
                }

                var distance = reader.GetDouble(5);

                // Косинусное расстояние в sqlite-vec: distance = 1.0 - cosine_similarity.
                // Соответственно, оценка сходства: score = 1.0 - distance.
                var score = Math.Clamp(1.0 - distance, -1.0, 1.0);

                var chunk = new DocumentChunk(chunkId, documentId, chunkIndex, text, metadata);
                results.Add(new RetrievalResult(chunk, score));
            }

            return results;
        }
        finally
        {
            if (shouldDispose)
            {
                await conn.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(IEnumerable<string> chunkIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunkIds);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var idList = chunkIds as IReadOnlyList<string> ?? chunkIds.ToList();
        if (idList.Count == 0) return;

        if (_options.AutoEnsureCollection && !_isInitialized)
        {
            await EnsureCollectionExistsAsync(cancellationToken).ConfigureAwait(false);
        }

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (conn, shouldDispose) = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                await using var vecCmd = conn.CreateCommand();
                vecCmd.Transaction = tx;
                vecCmd.CommandText = $"DELETE FROM \"vec_{_options.CollectionName}\" WHERE chunk_id = @id;";
                var pVecId = vecCmd.Parameters.Add("@id", SqliteType.Text);

                await using var chunkCmd = conn.CreateCommand();
                chunkCmd.Transaction = tx;
                chunkCmd.CommandText = $"DELETE FROM \"{_options.CollectionName}\" WHERE chunk_id = @id;";
                var pChunkId = chunkCmd.Parameters.Add("@id", SqliteType.Text);

                foreach (var id in idList)
                {
                    pVecId.Value = id;
                    await vecCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                    pChunkId.Value = id;
                    await chunkCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("Удалено {Count} чанков из коллекции '{Collection}'.", idList.Count, _options.CollectionName);
            }
            finally
            {
                if (shouldDispose)
                {
                    await conn.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<(SqliteConnection Connection, bool ShouldDispose)> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_isInMemory)
        {
            if (_inMemoryConnection == null || _inMemoryConnection.State != ConnectionState.Open)
            {
                _inMemoryConnection = new SqliteConnection(_options.ConnectionString);
                await _inMemoryConnection.OpenAsync(cancellationToken).ConfigureAwait(false);
                _inMemoryConnection.EnableExtensions(true);
                _inMemoryConnection.LoadExtension(_resolvedExtensionPath);
            }

            return (_inMemoryConnection, false);
        }

        var conn = new SqliteConnection(_options.ConnectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        conn.EnableExtensions(true);
        conn.LoadExtension(_resolvedExtensionPath);
        return (conn, true);
    }

    private static string ResolveExtensionPath(string? customPath)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
        {
            return Path.GetFullPath(customPath);
        }

        // Определение RID (Runtime Identifier) текущей платформы
        string rid = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64")
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64")
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64")
                    : "unknown";

        string[] candidateNames = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ["vec0.dll", "vec0"]
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? ["vec0.so", "libvec0.so", "vec0"]
                : ["vec0.dylib", "libvec0.dylib", "vec0"];

        var baseDir = AppContext.BaseDirectory;
        var cwd = Directory.GetCurrentDirectory();

        // 1. Поиск в каталоге runtimes/{rid}/native/ рядом со сборкой (стандарт .NET для нативных библиотек)
        foreach (var extName in candidateNames)
        {
            var ridCandidate = Path.Combine(baseDir, "runtimes", rid, "native", extName);
            if (File.Exists(ridCandidate)) return Path.GetFullPath(ridCandidate);
        }

        // 2. Поиск непосредственно в BaseDirectory
        foreach (var extName in candidateNames)
        {
            var candidate = Path.Combine(baseDir, extName);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }

        // 3. Поиск в текущем рабочем каталоге (включая runtimes/{rid}/native/)
        foreach (var extName in candidateNames)
        {
            var ridCwd = Path.Combine(cwd, "runtimes", rid, "native", extName);
            if (File.Exists(ridCwd)) return Path.GetFullPath(ridCwd);

            var candidate = Path.Combine(cwd, extName);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }

        // 4. Поиск вверх по дереву каталогов от BaseDirectory (для локальной разработки и тестов)
        var dir = new DirectoryInfo(baseDir);
        while (dir != null)
        {
            foreach (var extName in candidateNames)
            {
                var ridCandidate = Path.Combine(dir.FullName, "runtimes", rid, "native", extName);
                if (File.Exists(ridCandidate)) return Path.GetFullPath(ridCandidate);

                var candidate = Path.Combine(dir.FullName, extName);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            dir = dir.Parent;
        }

        // 5. Системный вызов по имени библиотеки (для стандартных путей dlopen/LoadLibrary)
        return "vec0";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _inMemoryConnection?.Dispose();
        _inMemoryConnection = null;
        _writeLock.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_inMemoryConnection != null)
        {
            await _inMemoryConnection.DisposeAsync().ConfigureAwait(false);
            _inMemoryConnection = null;
        }

        _writeLock.Dispose();
    }
}
