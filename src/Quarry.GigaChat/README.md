# Quarry.GigaChat

Интеграция корпоративной платформы **Quarry** с нейросетевыми моделями **Сбер GigaChat** (`GigaChat`, `GigaChat-Plus`, `GigaChat-Pro`, `GigaChat-Max`, `GigaChat-3-Ultra`, `Embeddings`, `GigaEmbeddings-3B-2025-09`).

Проект спроектирован на базе стандартных абстракций современного .NET AI стека:
* **`Microsoft.Extensions.AI`**: реализация [`IChatClient`](file:///e:/Proba/Quarry/src/Quarry.GigaChat/ChatClient/GigaChatChatClient.cs) и [`IEmbeddingGenerator<string, Embedding<float>>`](file:///e:/Proba/Quarry/src/Quarry.GigaChat/Embeddings/GigaChatEmbeddingGenerator.cs)
* **`Microsoft.Agents.AI`**: фабричные хелперы и адаптеры [`ChatClientAgent`](file:///e:/Proba/Quarry/src/Quarry.GigaChat/GigaChatAgent.cs) для Microsoft Agent Framework
* **GigaChat REST API v1/v2**: полнофункциональный HTTP-клиент [`IGigaChatClient`](file:///e:/Proba/Quarry/src/Quarry.GigaChat/Client/IGigaChatClient.cs) (авторизация OAuth 2.0, SSE-стриминг, Function Calling, Files API, подсчет токенов, баланс).

---

## 🌟 Возможности и соответствие REST API GigaChat

| Раздел спецификации GigaChat REST API | Метод / Эндпоинт | Реализация в библиотеке |
| :--- | :--- | :--- |
| **Авторизация (OAuth 2.0)** | `POST /api/v2/oauth` | [`client.GetAccessTokenAsync()`](file:///e:/Proba/Quarry/src/Quarry.GigaChat/Client/GigaChatClient.cs), упреждающее кэширование, заголовки `Basic` и `RqUID`, поддержка областей `GIGACHAT_API_PERS`, `B2B`, `CORP`. |
| **Генерация ответов** | `POST /api/v1/chat/completions` | [`client.CreateChatCompletionAsync()`](file:///e:/Proba/Quarry/src/Quarry.GigaChat/Client/GigaChatClient.cs) и реализация [`IChatClient.GetResponseAsync()`](file:///e:/Proba/Quarry/src/Quarry.GigaChat/ChatClient/GigaChatChatClient.cs). Поддержка `temperature`, `top_p`, `max_tokens`, `repetition_penalty`, `profanity_check`, `n`, `flags`. |
| **Потоковая генерация (SSE)** | `POST /api/v1/chat/completions` (stream) | [`client.CreateChatCompletionStreamAsync()`](file:///e:/Proba/Quarry/src/Quarry.GigaChat/Client/GigaChatClient.cs) и [`IChatClient.GetStreamingResponseAsync()`](file:///e:/Proba/Quarry/src/Quarry.GigaChat/ChatClient/GigaChatChatClient.cs). Чтение `data:`, обработка `[DONE]`. |
| **Вызов функций (Tools)** | `functions`, `function_call` | Автоматическая генерация JSON Schema из `AIFunctionFactory.Create`, передача `function_call: "auto"`, обработка `finish_reason: "function_call"` и возврат ответа с `role: "function"`. |
| **Рассуждения (Reasoning)** | `reasoning_content` | Разбор цепочки рассуждений в сообщениях и потоковых чанках для моделей с рассуждениями. |
| **Векторные эмбеддинги** | `POST /api/v1/embeddings` | [`client.CreateEmbeddingsAsync()`](file:///e:/Proba/Quarry/src/Quarry.GigaChat/Client/GigaChatClient.cs) и реализация [`IEmbeddingGenerator<string, Embedding<float>>`](file:///e:/Proba/Quarry/src/Quarry.GigaChat/Embeddings/GigaChatEmbeddingGenerator.cs). |
| **Подсчет токенов** | `POST /api/v1/tokens/count` | [`client.CountTokensAsync(input, model)`](file:///e:/Proba/Quarry/src/Quarry.GigaChat/Client/GigaChatClient.cs). Возвращает число токенов и символов. |
| **Список и параметры моделей** | `GET /api/v1/models`, `GET /api/v1/models/{model}` | [`client.GetModelsAsync()`](file:///e:/Proba/Quarry/src/Quarry.GigaChat/Client/GigaChatClient.cs), [`client.GetModelAsync(modelId)`](file:///e:/Proba/Quarry/src/Quarry.GigaChat/Client/GigaChatClient.cs). |
| **Остаток баланса** | `GET /api/v1/balance` | [`client.GetBalanceAsync()`](file:///e:/Proba/Quarry/src/Quarry.GigaChat/Client/GigaChatClient.cs) (для предоплатных тарифов). |
| **Хранилище файлов (Files API)** | `POST /api/v1/files`<br>`GET /api/v1/files`<br>`GET /api/v1/files/{id}`<br>`GET /api/v1/files/{id}/content`<br>`POST /api/v1/files/{id}/delete` | [`client.UploadFileAsync()`](file:///e:/Proba/Quarry/src/Quarry.GigaChat/Client/GigaChatClient.cs)<br>[`client.GetFilesAsync()`](file:///e:/Proba/Quarry/src/Quarry.GigaChat/Client/GigaChatClient.cs)<br>[`client.GetFileInfoAsync(id)`](file:///e:/Proba/Quarry/src/Quarry.GigaChat/Client/GigaChatClient.cs)<br>[`client.DownloadFileContentAsync(id)`](file:///e:/Proba/Quarry/src/Quarry.GigaChat/Client/GigaChatClient.cs)<br>[`client.DeleteFileAsync(id)`](file:///e:/Proba/Quarry/src/Quarry.GigaChat/Client/GigaChatClient.cs). |
| **Вложения к сообщениям** | `attachments` | Маппинг идентификаторов файлов в `GigaChatMessage.Attachments`. |
| **Сертификаты безопасности** | Минцифры (Russian Trusted CA) | `IgnoreSslErrors = true` по умолчанию (можно передать собственный `HttpMessageHandler` или цепочку сертификатов). |
| **Microsoft Agent Framework** | `AIAgent`, `ChatClientAgent` | Методы расширения `client.AsAIAgent(...)`, фабрика `GigaChatAgent.Create(...)`. Полная совместимость с мультиагентными воркфлоу `Microsoft.Agents.AI.Workflows`. |
| **Dependency Injection** | `IServiceCollection` | `services.AddGigaChatClient(...)`, `services.AddGigaChatAgent(...)`. |

---

## 📂 Структура модуля

```text
Quarry.GigaChat/
├── ChatClient/
│   └── GigaChatChatClient.cs                   # Реализация IChatClient (Microsoft.Extensions.AI)
├── Client/
│   ├── GigaChatClient.cs                       # HTTP-клиент, OAuth 2.0, SSE-парсер, Files, Tokens, Balance
│   ├── GigaChatClientOptions.cs               # Настройки (AuthKey, SSL, модель, таймаут)
│   └── IGigaChatClient.cs                     # Интерфейс API-клиента со всеми методами REST API
├── Embeddings/
│   └── GigaChatEmbeddingGenerator.cs           # Реализация IEmbeddingGenerator<string, Embedding<float>>
├── Extensions/
│   ├── GigaChatClientExtensions.cs            # AsAIAgent(), AsIChatClient(), AsEmbeddingGenerator()
│   └── GigaChatServiceCollectionExtensions.cs # DI-регистрация в IServiceCollection
├── Models/
│   ├── GigaChatDefaults.cs                    # Константы моделей (GigaChat-3-Ultra, 2-Max, 2-Pro, Embeddings)
│   ├── GigaChatMessage.cs                     # Сообщения (system, user, assistant, function, attachments)
│   ├── GigaChatFunction.cs                    # Описание функций / инструментов
│   ├── GigaChatFunctionCall.cs                # Вызовы функций
│   ├── GigaChatCompletionRequest.cs           # Запрос на генерацию со всеми параметрами REST API
│   ├── GigaChatCompletionResponse.cs          # Ответ генерации
│   ├── GigaChatStreamChunk.cs                 # Потоковые чанки SSE и reasoning
│   ├── GigaChatModelInfo.cs                   # Модели для GET /models
│   ├── GigaChatTokensCountModels.cs           # Модели для POST /tokens/count
│   ├── GigaChatBalanceResponse.cs             # Модели для GET /balance
│   ├── GigaChatFileModels.cs                  # Модели для Files API
│   ├── GigaChatTokenResponse.cs               # Ответ OAuth2 токена
│   └── GigaChatEmbeddingModels.cs             # Запросы и ответы векторных представлений
├── GigaChatAgent.cs                           # Фабричные хелперы для создания ChatClientAgent
├── Quarry.GigaChat.csproj                     # Проект C# 13, .NET 9, Central Package Management
└── README.md                                  # Документация модуля
```

---

## 🔑 Способы авторизации

Поддерживаются **два взаимозаменяемых способа авторизации**:

### Способ 1: Authorization Key (единая строка Base64)
Строка в формате Base64, полученная в личном кабинете Сбер:
```csharp
using Quarry.GigaChat;
using Quarry.GigaChat.Client;

// Через конструктор клиента:
using var client = new GigaChatClient("ВАШ_АВТОРИЗАЦИОННЫЙ_ТОКЕН_BASE64");

// Через GigaChatClientOptions:
var options = GigaChatClientOptions.FromAuthKey("ВАШ_АВТОРИЗАЦИОННЫЙ_ТОКЕН_BASE64");

// В Dependency Injection:
services.AddGigaChatClient("ВАШ_АВТОРИЗАЦИОННЫЙ_ТОКЕН_BASE64");
```

### Способ 2: Client ID и Client Secret (раздельные реквизиты)
Идентификатор клиента и секрет из кабинета разработчика:
```csharp
using Quarry.GigaChat.Client;

// Через конструктор клиента:
using var client = new GigaChatClient("client-id", "client-secret");

// Через GigaChatClientOptions:
var options = GigaChatClientOptions.FromClientCredentials("client-id", "client-secret");

// В Dependency Injection:
services.AddGigaChatClient("client-id", "client-secret");
```

---

## 🚀 Примеры использования

### 1. Использование как `IChatClient` (Microsoft.Extensions.AI)
```csharp
using Microsoft.Extensions.AI;
using Quarry.GigaChat.Client;
using Quarry.GigaChat.Extensions;

using var client = new GigaChatClient(authKey);
IChatClient chatClient = client.AsIChatClient();

var response = await chatClient.GetResponseAsync("Объясни принцип работы RAG-систем");
Console.WriteLine(response.Text);
```

### 2. Потоковая генерация ответа
```csharp
await foreach (var update in chatClient.GetStreamingResponseAsync("Напиши сказку о роботе"))
{
    Console.Write(update.Text);
}
```

### 3. Вызов функций (Tools / Function Calling)
```csharp
[Description("Получает текущую погоду в указанном городе")]
static string GetWeather(string city) => $"В городе {city} сейчас ясно, +21°C";

var chatOptions = new ChatOptions
{
    Tools = [AIFunctionFactory.Create(GetWeather)]
};

var response = await chatClient.GetResponseAsync("Какая погода в Казани?", chatOptions);
Console.WriteLine(response.Text);
```

### 4. Векторные эмбеддинги (`IEmbeddingGenerator`)
```csharp
IEmbeddingGenerator<string, Embedding<float>> generator = client.AsEmbeddingGenerator();

var embeddings = await generator.GenerateAsync(["Текст для векторизации"]);
Console.WriteLine($"Размерность вектора: {embeddings[0].Vector.Length}");
```

### 5. Создание агента Microsoft Agent Framework
```csharp
using Microsoft.Agents.AI;
using Quarry.GigaChat;

ChatClientAgent agent = GigaChatAgent.Create(
    authKey: authKey,
    instructions: "Ты вежливый помощник службы поддержки Quarry.",
    name: "QuarrySupportAgent"
);

var reply = await agent.RunAsync("Как настроить гибридный поиск?");
Console.WriteLine(reply.Text);
```
