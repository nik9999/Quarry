# Quarry.Core

Ядро доменной модели, абстракций и математического аппарата RAG-системы **Quarry**.

Библиотека `Quarry.Core` не содержит тяжелых инфраструктурных зависимостей и определяет контракты, структуры данных и быстрые математические операции, используемые всеми остальными слоями системы (`Quarry.Ingestion`, `Quarry.Retrieval`, API и клиентским интерфейсом).

---

## Зависимости

* `Microsoft.Extensions.AI.Abstractions` — базовые интерфейсы Microsoft для AI.
* `System.Numerics.Vectors` — аппаратные векторные инструкции SIMD (встроено в .NET 9).

---

## Архитектура и компоненты

### 1. Доменные сущности (`Quarry.Core.Documents`)

* **`Document`** (`Document.cs`)  
  Неизменяемая запись (`record`), представляющая исходный документ до разбиения на фрагменты.
  * `string Id` — уникальный идентификатор документа.
  * `string Text` — полный текст документа.
  * `IReadOnlyDictionary<string, string>? Metadata` — произвольные метаданные (источник, дата, теги, автор).

* **`DocumentChunk`** (`DocumentChunk.cs`)  
  Фрагмент текста документа, полученный после чанкинга с поддержкой точного цитирования первоисточника.
  * `string ChunkId` — уникальный идентификатор фрагмента (по схеме `{documentId}#{index}`).
  * `string DocumentId` — идентификатор родительского документа.
  * `int Index` — порядковый номер фрагмента в документе.
  * `string Text` — текст фрагмента.
  * `IReadOnlyDictionary<string, string>? Metadata` — метаданные фрагмента.
  * `int StartCharOffset` — начальное смещение символа в исходном документе.
  * `int EndCharOffset` — конечное смещение символа в исходном документе.

* **`EmbeddedChunk`** (`EmbeddedChunk.cs`)  
  Чанк документа, объединенный с вычисленным плотным вектором эмбеддинга.
  * `DocumentChunk Chunk` — исходный фрагмент текста.
  * `ReadOnlyMemory<float> Vector` — непрерывный вектор признаков фиксированной длины.
  * `string EmbeddingModel` — название модели, сгенерировавшей данный вектор.
  * Вспомогательные свойства: `ChunkId`, `DocumentId`, `Text`.

* **`RetrievalResult`** (`RetrievalResult.cs`)  
  Результат поиска, содержащий найденный чанк и нормализованный показатель релевантности.
  * `DocumentChunk Chunk` — найденный фрагмент.
  * `double Score` — оценка соответствия запросу (значение в диапазоне $[0.0, 1.0]$).

---

### 2. Базовые абстракции (`Quarry.Core.Abstractions`)

* **`IChunker`** (`IChunker.cs`)  
  Базовый контракт стратегии разбиения (чанкинга) документов на фрагменты:
  * `string Strategy { get; }` — идентификатор алгоритма (например, `"fixed"`, `"sentence"`, `"recursive"`, `"token"`, `"semantic"`, `"code"`).
  * `IAsyncEnumerable<DocumentChunk> ChunkAsync(Document document, CancellationToken ct)` — асинхронный поток чанков с точным расчетом смещений символов.

* **`IEmbeddingService`** (`IEmbeddingService.cs`)  
  Главный доменный интерфейс для работы с векторными представлениями:
  * `string EmbeddingModel { get; }` — имя активной модели.
  * `int Dimensions { get; }` — размерность выходного вектора (например, 384).
  * `Task<EmbeddedChunk> EmbedAsync(DocumentChunk chunk, CancellationToken ct)` — векторизация одного фрагмента.
  * `Task<ReadOnlyMemory<float>> EmbedQueryAsync(string query, CancellationToken ct)` — векторизация пользовательского поискового запроса.
  * `IAsyncEnumerable<EmbeddedChunk> EmbedAsync(IAsyncEnumerable<DocumentChunk> chunks, CancellationToken ct)` — потоковая векторизация.

* **`IVectorStore`** (`IVectorStore.cs`)  
  Доменный порт хранилища векторных эмбеддингов и семантического поиска:
  * `string CollectionName { get; }` — название таблицы/коллекции.
  * `Task EnsureCollectionExistsAsync(CancellationToken ct)` — создание схемы и индексов.
  * `Task UpsertAsync(IEnumerable<EmbeddedChunk> chunks, CancellationToken ct)` — сохранение векторизованных чанков.
  * `Task<IReadOnlyList<RetrievalResult>> SearchAsync(ReadOnlyMemory<float> queryVector, int topK, MetadataFilter? filter, CancellationToken ct)` — KNN-поиск с опциональным пре-фильтром.
  * `Task DeleteAsync(IEnumerable<string> chunkIds, CancellationToken ct)` — удаление по списку идентификаторов.

* **`IRetriever`** (`IRetriever.cs`)  
  Базовый контракт поисковых механизмов и ретриверов:
  * `string Strategy { get; }` — идентификатор стратегии поиска (`"dense"`, `"sparse-bm25"`, `"hybrid"`, `"structural"`).
  * `Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(string query, int topK, CancellationToken ct)` — асинхронное получение наиболее релевантных чанков для запроса.

* **`EmbeddingPrompt`** (`EmbeddingPrompt.cs`) & **`EmbeddingTaskType`** (`EmbeddingTaskType.cs`)  
  Управляет схемой префиксов задач для асимметричных моделей (E5, BGE, Nomic).
  * `Apply(string text, EmbeddingTaskType taskType)` — применяет контекстный префикс (`passage: ` или `query: `).
  * Профили: `EmbeddingPrompt.None`, `EmbeddingPrompt.E5`, `EmbeddingPrompt.Bge`, `EmbeddingPrompt.Nomic`.

---

### 3. Фильтрация метаданных (`Quarry.Core.Filtering`)

* **`MetadataFilter`** (`MetadataFilter.cs`)  
  Фильтр предварительного отбора (Pre-filter) чанков по метаданным до вычисления дистанций.
  * `Dictionary<string, string> ExactMatches` — словарь точных совпадений ключ-значение.
  * Метод `bool Matches(IReadOnlyDictionary<string, string>? metadata)` — регистронезависимая проверка соответствия метаданных.

---

### 4. SIMD-математика (`Quarry.Core.Numerics`)

* **`CosineKernel`** (`CosineKernel.cs`)  
  Высокопроизводительный математический модуль вычисления косинусного сходства векторов:
  $$\text{CosineSimilarity}(a, b) = \frac{a \cdot b}{\|a\|_2 \cdot \|b\|_2}$$
  * Использует аппаратные SIMD-инструкции (`System.Numerics.Vector<float>`), автоматически задействуя AVX2 / AVX-512 / ARM NEON.
  * Корректно обрабатывает хвосты векторов произвольной размерности и гарантирует защиту от деления на ноль при нулевой длине вектора.

---

## Тестирование

Модуль полностью покрыт модульными тестами в проекте `tests/Quarry.Core.Tests`:
* **28 тестов**, 100% прохождение, 0 предупреждений компилятора и анализаторов Roslyn.
* Проверяются: SIMD-вычисления `CosineKernel`, неизменяемость записей моделей, смещения символов, профили промптов E5, контракты `IRetriever` и `IVectorStore`.
