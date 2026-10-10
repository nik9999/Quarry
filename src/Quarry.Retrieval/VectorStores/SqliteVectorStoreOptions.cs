namespace Quarry.Retrieval.VectorStores;

/// <summary>
/// Параметры конфигурации для векторного хранилища SQLite-vec.
/// </summary>
public sealed class SqliteVectorStoreOptions
{
    /// <summary>
    /// Строка подключения к SQLite. По умолчанию "Data Source=quarry.db".
    /// </summary>
    public string ConnectionString { get; set; } = "Data Source=quarry.db";

    /// <summary>
    /// Имя коллекции / таблицы чанков. По умолчанию "chunks".
    /// </summary>
    public string CollectionName { get; set; } = "chunks";

    /// <summary>
    /// Размерность векторов (по умолчанию 384 для multilingual-e5-small).
    /// </summary>
    public int Dimensions { get; set; } = 384;

    /// <summary>
    /// Опциональный явный путь к динамической библиотеке расширения vec0 (.dll/.so/.dylib).
    /// Если не указан, библиотека ищется автоматически рядом со сборкой или в текущем каталоге.
    /// </summary>
    public string? ExtensionPath { get; set; }

    /// <summary>
    /// Автоматически проверять и создавать таблицы при выполнении первой операции.
    /// По умолчанию true.
    /// </summary>
    public bool AutoEnsureCollection { get; set; } = true;
}
