using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Quarry.GigaChat.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Quarry.GigaChat.Client;

/// <summary>
/// Client implementation for communicating with Sber GigaChat API.
/// </summary>
public sealed class GigaChatClient : IGigaChatClient
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    /// <summary>
    /// Gets the configuration options for this client.
    /// </summary>
    public GigaChatClientOptions Options { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="GigaChatClient"/> with the specified options and optional logger.
    /// </summary>
    /// <param name="options">Configuration options.</param>
    /// <param name="logger">Optional logger.</param>
    public GigaChatClient(GigaChatClientOptions options, ILogger<GigaChatClient>? logger = null)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? (ILogger)NullLogger.Instance;

        if (options.HttpClient is not null)
        {
            _httpClient = options.HttpClient;
            _disposeHttpClient = false;
        }
        else
        {
            var handler = options.CustomHttpMessageHandler ?? CreateDefaultHandler(options.IgnoreSslErrors);
            _httpClient = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = options.Timeout
            };
            _disposeHttpClient = true;
        }
    }

    /// <summary>
    /// Initializes a new instance of <see cref="GigaChatClient"/> with an authorization key.
    /// </summary>
    /// <param name="authKey">GigaChat authorization key.</param>
    /// <param name="ignoreSslErrors">Whether to bypass Russian CA certificate validation.</param>
    public GigaChatClient(string authKey, bool ignoreSslErrors = true)
        : this(new GigaChatClientOptions(authKey, ignoreSslErrors))
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="GigaChatClient"/> with Client ID and Client Secret.
    /// </summary>
    /// <param name="clientId">Client ID from Sber Developers portal.</param>
    /// <param name="clientSecret">Client Secret from Sber Developers portal.</param>
    /// <param name="ignoreSslErrors">Whether to bypass Russian CA certificate validation.</param>
    public GigaChatClient(string clientId, string clientSecret, bool ignoreSslErrors = true)
        : this(new GigaChatClientOptions(clientId, clientSecret, ignoreSslErrors))
    {
    }

    /// <summary>
    /// Creates a client using an Authorization Key and explicit scope.
    /// </summary>
    public static GigaChatClient CreateFromAuthKey(string authKey, string scope = GigaChatDefaults.ScopePers, bool ignoreSslErrors = true)
        => new(GigaChatClientOptions.FromAuthKey(authKey, scope, ignoreSslErrors));

    /// <summary>
    /// Creates a client using Client ID, Client Secret and explicit scope.
    /// </summary>
    public static GigaChatClient CreateFromClientCredentials(string clientId, string clientSecret, string scope = GigaChatDefaults.ScopePers, bool ignoreSslErrors = true)
        => new(GigaChatClientOptions.FromClientCredentials(clientId, clientSecret, scope, ignoreSslErrors));

    private static HttpClientHandler CreateDefaultHandler(bool ignoreSslErrors)
    {
        var handler = new HttpClientHandler();
        if (ignoreSslErrors)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        return handler;
    }

    /// <inheritdoc/>
    public async Task<string> GetAccessTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-3))
        {
            return _cachedToken;
        }

        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-3))
            {
                return _cachedToken;
            }

            _logger.LogDebug("Requesting fresh GigaChat OAuth token...");

            using var request = new HttpRequestMessage(HttpMethod.Post, Options.AuthUrl);
            var authHeader = Options.GetAuthorizationHeaderValue();
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authHeader);
            request.Headers.Add("RqUID", Guid.NewGuid().ToString());

            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("scope", Options.Scope)
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var contentString = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("GigaChat OAuth token request failed: {StatusCode} {Content}", response.StatusCode, contentString);
                throw new HttpRequestException($"Failed to obtain GigaChat access token. Status: {response.StatusCode}. Response: {contentString}");
            }

            var tokenResponse = JsonSerializer.Deserialize<GigaChatTokenResponse>(contentString, s_jsonOptions)
                ?? throw new InvalidOperationException("Failed to deserialize GigaChat token response.");

            _cachedToken = tokenResponse.AccessToken;
            _tokenExpiresAt = DateTimeOffset.FromUnixTimeMilliseconds(tokenResponse.ExpiresAt);

            _logger.LogDebug("GigaChat OAuth token acquired successfully. Expires at: {ExpiresAt}", _tokenExpiresAt);
            return _cachedToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<GigaChatCompletionResponse> CreateChatCompletionAsync(GigaChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var token = await GetAccessTokenAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var url = new Uri(Options.BaseUrl, "chat/completions");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var json = JsonSerializer.Serialize(request, s_jsonOptions);
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("GigaChat completion failed: {StatusCode} {Content}", response.StatusCode, responseContent);
            throw new HttpRequestException($"GigaChat API error ({response.StatusCode}): {responseContent}");
        }

        var completionResponse = JsonSerializer.Deserialize<GigaChatCompletionResponse>(responseContent, s_jsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize GigaChat completion response.");

        return completionResponse;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<GigaChatStreamChunk> CreateChatCompletionStreamAsync(
        GigaChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        request.Stream = true;
        var token = await GetAccessTokenAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var url = new Uri(Options.BaseUrl, "chat/completions");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var json = JsonSerializer.Serialize(request, s_jsonOptions);
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError("GigaChat streaming completion failed: {StatusCode} {Content}", response.StatusCode, err);
            throw new HttpRequestException($"GigaChat streaming error ({response.StatusCode}): {err}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var payload = line["data:".Length..].Trim();
                if (payload.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
                {
                    yield break;
                }

                if (string.IsNullOrWhiteSpace(payload))
                {
                    continue;
                }

                GigaChatStreamChunk? chunk = null;
                try
                {
                    chunk = JsonSerializer.Deserialize<GigaChatStreamChunk>(payload, s_jsonOptions);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse streaming chunk: {Payload}", payload);
                }

                if (chunk is not null)
                {
                    yield return chunk;
                }
            }
        }
    }

    /// <inheritdoc/>
    public async Task<GigaChatEmbeddingResponse> CreateEmbeddingsAsync(GigaChatEmbeddingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var token = await GetAccessTokenAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var url = new Uri(Options.BaseUrl, "embeddings");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var json = JsonSerializer.Serialize(request, s_jsonOptions);
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("GigaChat embeddings failed: {StatusCode} {Content}", response.StatusCode, responseContent);
            var detail = response.StatusCode == HttpStatusCode.PaymentRequired
                ? $"GigaChat embeddings error (402 Payment Required): тариф/пакет для эмбеддингов не оплачен или не подключен в личном кабинете Сбера. Ответ сервера: {responseContent}"
                : $"GigaChat embeddings error ({response.StatusCode}): {responseContent}";
            throw new HttpRequestException(detail, inner: null, statusCode: response.StatusCode);
        }

        var embeddingResponse = JsonSerializer.Deserialize<GigaChatEmbeddingResponse>(responseContent, s_jsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize GigaChat embeddings response.");

        return embeddingResponse;
    }

    /// <inheritdoc/>
    public async Task<GigaChatModelsResponse> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var url = new Uri(Options.BaseUrl, "models");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("GigaChat GetModels failed: {StatusCode} {Content}", response.StatusCode, responseContent);
            throw new HttpRequestException($"GigaChat models error ({response.StatusCode}): {responseContent}");
        }

        return JsonSerializer.Deserialize<GigaChatModelsResponse>(responseContent, s_jsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize GigaChat models response.");
    }

    /// <inheritdoc/>
    public async Task<GigaChatModelInfo> GetModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        var token = await GetAccessTokenAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var url = new Uri(Options.BaseUrl, $"models/{Uri.EscapeDataString(modelId)}");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("GigaChat GetModel failed: {StatusCode} {Content}", response.StatusCode, responseContent);
            throw new HttpRequestException($"GigaChat model '{modelId}' error ({response.StatusCode}): {responseContent}");
        }

        return JsonSerializer.Deserialize<GigaChatModelInfo>(responseContent, s_jsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize GigaChat model '{modelId}' response.");
    }

    /// <inheritdoc/>
    public async Task<List<GigaChatTokensCountItem>> CountTokensAsync(IEnumerable<string> input, string? model = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var requestPayload = new GigaChatTokensCountRequest(input, model ?? Options.DefaultModel);
        var token = await GetAccessTokenAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var url = new Uri(Options.BaseUrl, "tokens/count");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var json = JsonSerializer.Serialize(requestPayload, s_jsonOptions);
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("GigaChat CountTokens failed: {StatusCode} {Content}", response.StatusCode, responseContent);
            throw new HttpRequestException($"GigaChat tokens count error ({response.StatusCode}): {responseContent}");
        }

        return JsonSerializer.Deserialize<List<GigaChatTokensCountItem>>(responseContent, s_jsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize GigaChat tokens count response.");
    }

    /// <inheritdoc/>
    public async Task<GigaChatBalanceResponse> GetBalanceAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var url = new Uri(Options.BaseUrl, "balance");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("GigaChat GetBalance failed: {StatusCode} {Content}", response.StatusCode, responseContent);
            throw new HttpRequestException($"GigaChat balance error ({response.StatusCode}): {responseContent}");
        }

        return JsonSerializer.Deserialize<GigaChatBalanceResponse>(responseContent, s_jsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize GigaChat balance response.");
    }

    /// <inheritdoc/>
    public async Task<GigaChatFile> UploadFileAsync(Stream fileStream, string fileName, string purpose = "general", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var token = await GetAccessTokenAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var url = new Uri(Options.BaseUrl, "files");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var multipartContent = new MultipartFormDataContent();
        var streamContent = new StreamContent(fileStream);
        multipartContent.Add(streamContent, "file", fileName);
        multipartContent.Add(new StringContent(purpose), "purpose");

        httpRequest.Content = multipartContent;

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("GigaChat UploadFile failed: {StatusCode} {Content}", response.StatusCode, responseContent);
            throw new HttpRequestException($"GigaChat upload file error ({response.StatusCode}): {responseContent}");
        }

        return JsonSerializer.Deserialize<GigaChatFile>(responseContent, s_jsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize GigaChat uploaded file response.");
    }

    /// <inheritdoc/>
    public async Task<GigaChatFilesResponse> GetFilesAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var url = new Uri(Options.BaseUrl, "files");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("GigaChat GetFiles failed: {StatusCode} {Content}", response.StatusCode, responseContent);
            throw new HttpRequestException($"GigaChat list files error ({response.StatusCode}): {responseContent}");
        }

        return JsonSerializer.Deserialize<GigaChatFilesResponse>(responseContent, s_jsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize GigaChat files list response.");
    }

    /// <inheritdoc/>
    public async Task<GigaChatFile> GetFileInfoAsync(string fileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);

        var token = await GetAccessTokenAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var url = new Uri(Options.BaseUrl, $"files/{Uri.EscapeDataString(fileId)}");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("GigaChat GetFileInfo failed: {StatusCode} {Content}", response.StatusCode, responseContent);
            throw new HttpRequestException($"GigaChat file info error ({response.StatusCode}): {responseContent}");
        }

        return JsonSerializer.Deserialize<GigaChatFile>(responseContent, s_jsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize GigaChat file info for '{fileId}'.");
    }

    /// <inheritdoc/>
    public async Task<Stream> DownloadFileContentAsync(string fileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);

        var token = await GetAccessTokenAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var url = new Uri(Options.BaseUrl, $"files/{Uri.EscapeDataString(fileId)}/content");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError("GigaChat DownloadFileContent failed: {StatusCode} {Content}", response.StatusCode, err);
            throw new HttpRequestException($"GigaChat download file error ({response.StatusCode}): {err}");
        }

        return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<GigaChatDeletedFileResponse> DeleteFileAsync(string fileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);

        var token = await GetAccessTokenAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var url = new Uri(Options.BaseUrl, $"files/{Uri.EscapeDataString(fileId)}/delete");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("GigaChat DeleteFile failed: {StatusCode} {Content}", response.StatusCode, responseContent);
            throw new HttpRequestException($"GigaChat delete file error ({response.StatusCode}): {responseContent}");
        }

        return JsonSerializer.Deserialize<GigaChatDeletedFileResponse>(responseContent, s_jsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize GigaChat delete file response for '{fileId}'.");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _tokenLock.Dispose();
        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
