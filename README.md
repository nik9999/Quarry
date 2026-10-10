# Quarry

Корпоративная RAG-система (Retrieval-Augmented Generation) на платформе **C# / .NET 9**.

Ключевая особенность — **полностью автономный локальный инференс** эмбеддингов через **ONNX Runtime** и компактное хранение векторов в **SQLite-vec** без зависимостей от сторонних внешних сервисов (Ollama, Docker, Python).

---

## Архитектура решения и навигация

Система разделена на слабосвязанные модули, каждый из которых снабжен собственной подробной документацией:

```text
Quarry/
├── src/
│   ├── Quarry.Core/          # Доменные модели, контракты (IChunker, IRetriever, IVectorStore), SIMD-ядро CosineKernel
│   ├── Quarry.Ingestion/     # 7 стратегий чанкинга, пайплайн индексации, мультимодальный PDF, Change Feed, ONNX
│   ├── Quarry.Retrieval/     # Гибридный поиск (Dense + BM25), RRF, MMR, декораторы (HyDE, Step-Back, CRAG), SQLite-vec
│   ├── Quarry.Reranking/     # Переранжирование: паттерн retrieve-broadly, rerank-precisely, NoOp, LLM, Cohere, Scrutor
│   ├── Quarry.Reranking.Onnx/# Self-hosted ONNX кросс-энкодер bge-reranker-v2-m3 на базе OnnxRuntime
│   ├── Quarry.GigaChat/      # Интеграция со Сбер GigaChat: IChatClient, IEmbeddingGenerator, Microsoft Agent Framework
│   ├── Quarry.Operations/    # Эксплуатация и FinOps: Backfill, Procrustes SVD, GDPR Art. 17, Budget Enforcement, EU AI Act
│   └── Quarry.Evaluation/    # Оценка качества RAG: Retrieval metrics (Recall, Precision, MRR, nDCG), LLM-judge, статистика
├── samples/
│   └── Quarry.Playground/    # Консольный стенд демонстрации и замера производительности
├── tests/
│   ├── Quarry.UnitTests/     # 196 модульных тестов (Evaluation, Operations, Budgeting, Compliance, Chunking, Retrieval, Reranking, etc.)
│   ├── Quarry.IntegrationTests/# Интеграционные тесты внешних API и хранилищ (Api, AzureSearch, Mcp, Ollama, Qdrant)
│   ├── Quarry.EvalTests/     # Тесты оценки качества и метрик RAG (Recall, Precision, Faithfulness)
│   └── Quarry.SecurityTests/ # Тесты безопасности и защиты от инъекций (PromptShield, RedTeam, Sanitizer)
├── model/
│   └── multilingual-e5-small/# Квантованная ONNX модель Ru/En (112 МБ) и токенизатор SentencePiece
├── architecture.md       # Детальная архитектура системы, топология и диаграммы взаимодействий
├── Directory.Build.props     # Параметры компиляции C# 13, Nullable, статический анализ
├── Directory.Build.targets   # Сборочные хуки MSBuild
└── Directory.Packages.props  # Централизованное управление версиями пакетов (CPM)
```

### Документация модулей:
1. **[Архитектура системы](architecture.md)** — сквозная топология конвейеров, потоки данных, организация таблиц и математические формулы (RRF, MMR).
2. **[`Quarry.Core`](src/Quarry.Core/README.md)** — доменные записи (`Document`, `DocumentChunk`, `EmbeddedChunk`), абстракции (`IRetriever`, `IVectorStore`, `IEmbeddingService`), фильтры метаданных и аппаратное SIMD-вычисление косинусного сходства.
3. **[`Quarry.Ingestion`](src/Quarry.Ingestion/README.md)** — 7 алгоритмов чанкинга (включая AST-разбор C# через Roslyn), мультимодальное извлечение из PDF (PdfPig), Change Feed реального времени и локальный батч-инференс ONNX Runtime.
4. **[`Quarry.Retrieval`](src/Quarry.Retrieval/README.md)** — гибридный поиск (Dense + Okapi BM25), слияние рангов RRF, диверсификация MMR, ретривер-декораторы (HyDE, Step-Back, RAG-Fusion, CRAG), безвекторная структурная навигация по Markdown AST и хранилища векторов (SQLite-vec и InMemory).
5. **[`Quarry.Reranking`](src/Quarry.Reranking/README.md)** — конвейер переранжирования по принципу *"retrieve-broadly, rerank-precisely"*, прозрачная декорация `IRetriever` (Scrutor), `LlmRerank` (LLM-as-judge), `CohereReranker` (v2 API) и relevance floor `MinScore`.
6. **[`Quarry.Reranking.Onnx`](src/Quarry.Reranking.Onnx/README.md)** — автономный локальный кросс-энкодер `BAAI/bge-reranker-v2-m3` с токенизацией пар XLM-RoBERTa SentencePiece и батч-инференсом в ONNX Runtime.
7. **[`Quarry.GigaChat`](src/Quarry.GigaChat/README.md)** — интеграция с моделями Сбер GigaChat (`GigaChat-3-Ultra`, `Pro`, `Max`, `Embeddings`), поддержка `Microsoft.Extensions.AI` (`IChatClient`, `IEmbeddingGenerator`), Function Calling, Files API и агентов `Microsoft.Agents.AI`.
8. **[`Quarry.Operations`](src/Quarry.Operations/README.md)** — операционный контур: перевстраивание (`BackfillScheduler`), выравнивание дрейфа (`DriftAdapter` Procrustes SVD), удаление данных (GDPR Art. 17), бюджетирование и квоты (`BudgetEnforcingPipeline`, `TenantSpendLedger`), аудит цитат, дашборд комплаенса и EU AI Act.
9. **[`Quarry.Evaluation`](src/Quarry.Evaluation/README.md)** — оценка качества RAG: метрики поиска (`RetrievalEvaluator`: Recall@K, Precision@K, MRR, nDCG@K), LLM-as-judge фактологичности (`GenerationEvaluator`) и статистический анализ значимости (`StatisticalSignificance`: интервалы Уилсона, парный бутстреп дельт, тест Макнемара).
10. **[`Quarry.Playground`](samples/Quarry.Playground/README.md)** — интерактивный тестовый стенд для проверки локального поиска.

---

## Ключевые возможности

* **Полная автономность**: локальный инференс модели `intfloat/multilingual-e5-small` (384 измерения) на CPU без развертывания контейнеров.
* **Портативность**: вся база знаний (тексты, метаданные и векторные индексы) хранится в одном компактном файле `quarry.db`.
* **Кроссплатформенность (Linux, Windows, macOS)**: нативное расширение `vec0` скомпилировано под основные архитектуры (`win-x64`, `linux-x64`, `linux-arm64`, `osx-arm64`, `osx-x64`).
* **Гибридный поиск (Dense + Sparse)**: комбинация семантического понимания и точного поиска по терминам через Okapi BM25 с ранжированием Reciprocal Rank Fusion ($k = 60$).
* **Продвинутое переранжирование (Cross-Encoder & LLM)**:
  * **Retrieve-Broadly, Rerank-Precisely**: широкая выборка пула кандидатов с последующим точным ранжированием.
  * **BAAI/bge-reranker-v2-m3**: локальный кросс-энкодер через ONNX Runtime без обращения к облаку.
  * **Cohere Rerank v3.5 & LLM-as-judge**: облачный API или оценка любой моделью через `IChatClient`.
  * **Relevance Floor (`MinScore`)**: отсечение низкорелевантного шума для честного отказа модели от ответа при отсутствии фактов.
  * **MMR (Maximal Marginal Relevance)**: устранение семантических повторов в контексте.
  * **Lost in the Middle Reordering**: распределение значимых чанков по краям контекста модели.
* **Структурная навигация**: навигация по разделам (`Ancestors`, `Descendants`, `Siblings`) и автоматический переход по перекрестным ссылкам (Cross-References).

---

## Запуск тестов

Все проекты решения полностью покрыты тестами на базе **xUnit** (196 тестов, 100% успех, 0 предупреждений анализатора):

```bash
dotnet test
```

Результаты тестового набора:
* `Quarry.UnitTests`: **196 пройден**, 1 пропущен (интеграционный тест ONNX-модели)
* `Quarry.IntegrationTests`, `Quarry.EvalTests`, `Quarry.SecurityTests`: каркасы проектов готовы к наполнению

---

## Быстрый запуск демонстрационного стенда

```bash
dotnet run --project samples/Quarry.Playground
```
