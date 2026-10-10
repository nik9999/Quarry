using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Quarry.GigaChat.Models;

namespace Quarry.GigaChat.Client;

/// <summary>
/// Client interface for interacting with Sber GigaChat API endpoints.
/// </summary>
public interface IGigaChatClient : IDisposable
{
    /// <summary>
    /// Gets the client configuration options.
    /// </summary>
    GigaChatClientOptions Options { get; }

    /// <summary>
    /// Acquires or retrieves a valid cached OAuth bearer access token.
    /// </summary>
    /// <param name="forceRefresh">Whether to force a new token request regardless of expiration cache.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A valid GigaChat access token.</returns>
    Task<string> GetAccessTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a chat completion request to GigaChat.
    /// </summary>
    /// <param name="request">Completion request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The completion response.</returns>
    Task<GigaChatCompletionResponse> CreateChatCompletionAsync(GigaChatCompletionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a streaming chat completion request to GigaChat.
    /// </summary>
    /// <param name="request">Completion request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Asynchronous stream of response chunks.</returns>
    IAsyncEnumerable<GigaChatStreamChunk> CreateChatCompletionStreamAsync(GigaChatCompletionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes vector embeddings for input texts.
    /// </summary>
    /// <param name="request">Embedding request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The embeddings response.</returns>
    Task<GigaChatEmbeddingResponse> CreateEmbeddingsAsync(GigaChatEmbeddingRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the list of available models.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of models.</returns>
    Task<GigaChatModelsResponse> GetModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves information about a specific model.
    /// </summary>
    /// <param name="modelId">Model identifier (e.g. "GigaChat", "GigaChat-Pro").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Model details.</returns>
    Task<GigaChatModelInfo> GetModelAsync(string modelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts tokens for input texts without making a completion request.
    /// </summary>
    /// <param name="input">List of input strings.</param>
    /// <param name="model">Model name (defaults to client's default model).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of token count results.</returns>
    Task<List<GigaChatTokensCountItem>> CountTokensAsync(IEnumerable<string> input, string? model = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves token balance for prepaid accounts.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Balance information.</returns>
    Task<GigaChatBalanceResponse> GetBalanceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a file (image, document, audio) to GigaChat files storage.
    /// </summary>
    /// <param name="fileStream">File content stream.</param>
    /// <param name="fileName">File name with extension.</param>
    /// <param name="purpose">Intended purpose ("general" or "assistant").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Information about the uploaded file.</returns>
    Task<GigaChatFile> UploadFileAsync(Stream fileStream, string fileName, string purpose = "general", CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the list of uploaded files.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of files.</returns>
    Task<GigaChatFilesResponse> GetFilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves metadata of an uploaded file.
    /// </summary>
    /// <param name="fileId">File identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>File metadata.</returns>
    Task<GigaChatFile> GetFileInfoAsync(string fileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads binary content of an uploaded file.
    /// </summary>
    /// <param name="fileId">File identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Stream containing the file data.</returns>
    Task<Stream> DownloadFileContentAsync(string fileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a file from GigaChat files storage.
    /// </summary>
    /// <param name="fileId">File identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deletion confirmation.</returns>
    Task<GigaChatDeletedFileResponse> DeleteFileAsync(string fileId, CancellationToken cancellationToken = default);
}
