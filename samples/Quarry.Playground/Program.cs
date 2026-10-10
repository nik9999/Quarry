using System.Diagnostics;
using Microsoft.ML.Tokenizers;
using Quarry.Core.Abstractions;
using Quarry.Core.Documents;
using Quarry.Core.Filtering;
using Quarry.Ingestion.Embeddings;
using Quarry.Ingestion.Embeddings.Onnx;
using Quarry.Retrieval.VectorStores;

namespace Quarry.Playground;

sealed class Program
{
    static async Task Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=======================================================================");
        Console.WriteLine("   Quarry: Демонстрация работы SQLite-vec и модели multilingual-e5     ");
        Console.WriteLine("   (Векторная БД + Семантический поиск + Фильтрация по метаданным)     ");
        Console.WriteLine("=======================================================================\n");

        // 1. Поиск и инициализация локальной ONNX модели
        var modelsDir = FindModelDirectory("model/multilingual-e5-small")
                     ?? Path.Combine(AppContext.BaseDirectory, "model", "multilingual-e5-small");

        var modelPath = Path.Combine(modelsDir, "model.onnx");
        var spPath = Path.Combine(modelsDir, "sentencepiece.bpe.model");

        if (!File.Exists(modelPath) || !File.Exists(spPath))
        {
            Console.WriteLine($"Ошибка: файлы модели не найдены по пути: {modelsDir}");
            return;
        }

        Console.WriteLine($"[1/5] Инициализация модели эмбеддингов: {modelsDir} ...");
        var sw = Stopwatch.StartNew();

        await using var spStream = File.OpenRead(spPath);
        var tokenizer = SentencePieceTokenizer.Create(spStream, addBeginningOfSentence: false, addEndOfSentence: false);

        using var onnxGenerator = new OnnxEmbeddingGenerator(
            modelPath: modelPath,
            tokenizer: tokenizer,
            modelId: "multilingual-e5-small",
            maxSequenceLength: 256,
            tokenMapper: TokenMappers.XlmRoberta);

        var embeddingService = new EmbeddingService(
            generator: onnxGenerator,
            embeddingModel: "multilingual-e5-small",
            dimensions: 384,
            prompt: EmbeddingPrompt.E5);

        sw.Stop();
        Console.WriteLine($"   Модель готова за {sw.ElapsedMilliseconds} мс. Размерность векторов: {embeddingService.Dimensions}");

        // 2. Инициализация хранилища SQLite-vec
        var dbPath = Path.Combine(AppContext.BaseDirectory, "quarry.db");
        // Очищаем предыдущую БД для чистоты демонстрации
        if (File.Exists(dbPath))
        {
            try { File.Delete(dbPath); } catch { /* ignore */ }
        }

        Console.WriteLine($"\n[2/5] Инициализация векторной базы данных SQLite-vec: {dbPath} ...");
        var storeOptions = new SqliteVectorStoreOptions
        {
            ConnectionString = $"Data Source={dbPath}",
            CollectionName = "knowledge_chunks",
            Dimensions = 384,
            AutoEnsureCollection = true
        };

        using var vectorStore = new SqliteVectorStore(storeOptions);
        await vectorStore.EnsureCollectionExistsAsync();
        Console.WriteLine($"   Коллекция '{vectorStore.CollectionName}' в SQLite-vec успешно создана.");

        // 3. Подготовка и векторизация тестовой базы знаний
        Console.WriteLine("\n[3/5] Подготовка и сохранение чанков с метаданными в SQLite-vec:");
        var documents = new List<DocumentChunk>
        {
            new("doc-ui", "kb-dev", 0,
                "Архитектура десктопных приложений на Avalonia UI с использованием шаблона MVVM и XAML-разметки.",
                new Dictionary<string, string> { ["category"] = "gui", ["language"] = "ru" }),

            new("doc-async", "kb-dev", 1,
                "Оптимизация асинхронного кода в C# и .NET: использование ValueTask, пул потоков и минимизация аллокаций памяти.",
                new Dictionary<string, string> { ["category"] = "csharp", ["language"] = "ru" }),

            new("doc-rag", "kb-ai", 0,
                "Retrieval-Augmented Generation (RAG) architecture combines vector search with LLMs to provide grounded answers.",
                new Dictionary<string, string> { ["category"] = "ai", ["language"] = "en" }),

            new("doc-di", "kb-dev", 2,
                "Внедрение зависимостей в ASP.NET Core: жизненный цикл сервисов IServiceCollection (Transient, Scoped, Singleton).",
                new Dictionary<string, string> { ["category"] = "csharp", ["language"] = "ru" }),

            new("doc-food", "kb-life", 0,
                "Традиционный рецепт наваристого украинского борща с говядиной, свеклой, капустой и чесноком.",
                new Dictionary<string, string> { ["category"] = "cooking", ["language"] = "ru" })
        };

        sw.Restart();
        var embeddedDocs = new List<EmbeddedChunk>();
        foreach (var doc in documents)
        {
            var embedded = await embeddingService.EmbedAsync(doc);
            embeddedDocs.Add(embedded);
        }
        await vectorStore.UpsertAsync(embeddedDocs);
        sw.Stop();
        Console.WriteLine($"   Векторизовано и сохранено {embeddedDocs.Count} документов в SQLite-vec за {sw.ElapsedMilliseconds} мс.");

        // 4. Тестирование семантического векторного поиска в SQLite-vec
        Console.WriteLine("\n[4/5] Тестирование векторного поиска (KNN через расширение sqlite-vec):");

        var testQueries = new[]
        {
            "Как разработать графический интерфейс для Windows и Linux на C#?",
            "Как устроена генерация с дополненной выборкой данных из базы знаний?",
            "Регистрация сервисов в DI контейнере и их жизненный цикл",
            "Рецепт борща с чесноком"
        };

        foreach (var query in testQueries)
        {
            Console.WriteLine($"\n-------------------------------------------------------");
            Console.WriteLine($"🔍 Вопрос: \"{query}\"");

            sw.Restart();
            var queryVector = await embeddingService.EmbedQueryAsync(query);
            var results = await vectorStore.SearchAsync(queryVector, topK: 3);
            sw.Stop();

            Console.WriteLine($"⏱ Векторизация и поиск в БД за {sw.ElapsedMilliseconds} мс. Результаты:");
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                var marker = i == 0 ? "👉 [ТОП-1]" : $"   [{i + 1}]   ";
                var cat = r.Chunk.Metadata != null && r.Chunk.Metadata.TryGetValue("category", out var c) ? c : "n/a";
                Console.WriteLine($"{marker} Score: {r.Score:F4} | Категория: {cat,-8} | [{r.ChunkId}]: \"{r.Text}\"");
            }
        }

        // 5. Тестирование Pre-filtering (фильтрации по метаданным) и удаления
        Console.WriteLine("\n=======================================================");
        Console.WriteLine("[5/5] Тестирование Pre-filtering по метаданным и удаления чанка:");

        var filterQuery = "Как ускорить работу программы и оптимизировать код?";
        var qv = await embeddingService.EmbedQueryAsync(filterQuery);

        Console.WriteLine($"\nЗапрос: \"{filterQuery}\"");
        Console.WriteLine("А) Поиск БЕЗ фильтра (topK=2):");
        var resNoFilter = await vectorStore.SearchAsync(qv, topK: 2);
        foreach (var r in resNoFilter)
        {
            Console.WriteLine($"   Score: {r.Score:F4} | [{r.ChunkId}]: \"{r.Text}\"");
        }

        Console.WriteLine("\nБ) Поиск С ПРЕ-ФИЛЬТРОМ [category = 'gui'] (должен найти только Avalonia, исключив C# async):");
        var filter = new MetadataFilter("category", "gui");
        var resFilter = await vectorStore.SearchAsync(qv, topK: 2, filter: filter);
        foreach (var r in resFilter)
        {
            Console.WriteLine($"   Score: {r.Score:F4} | [{r.ChunkId}]: \"{r.Text}\"");
        }

        Console.WriteLine("\nВ) Тестирование удаления: удаляем doc-food (борщ) из базы данных...");
        string[] chunksToDelete = ["doc-food"];
        await vectorStore.DeleteAsync(chunksToDelete);
        var foodQv = await embeddingService.EmbedQueryAsync("борщ");
        var foodRes = await vectorStore.SearchAsync(foodQv, topK: 1);
        Console.WriteLine($"   После удаления 'doc-food', ТОП-1 результат для запроса 'борщ': [{foodRes[0].ChunkId}] \"{foodRes[0].Text}\" (Score: {foodRes[0].Score:F4})");

        Console.WriteLine("\n=======================================================");
        Console.WriteLine("Все тесты SQLite-vec успешно завершены!");
        Console.WriteLine($"Размер файла базы данных {Path.GetFileName(dbPath)}: {new FileInfo(dbPath).Length / 1024.0:F1} КБ");
    }

    private static string? FindModelDirectory(string relativeSubPath)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, relativeSubPath);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        var cwdCandidate = Path.Combine(Directory.GetCurrentDirectory(), relativeSubPath);
        return Directory.Exists(cwdCandidate) ? cwdCandidate : null;
    }
}
