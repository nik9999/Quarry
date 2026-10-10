using System;
using System.Net.Http;
using System.Text;
using Quarry.GigaChat.Models;

namespace Quarry.GigaChat.Client;

/// <summary>
/// Configuration options for connecting to GigaChat API.
/// Supports authentication either via Authorization Key or via Client ID and Client Secret.
/// </summary>
public sealed class GigaChatClientOptions
{
    /// <summary>
    /// GigaChat Authorization Key (Base64-encoded client ID and secret, provided in Sber profile).
    /// Used as an alternative to specifying <see cref="ClientId"/> and <see cref="ClientSecret"/> separately.
    /// </summary>
    public string? AuthKey { get; set; }

    /// <summary>
    /// Client ID from Sber Developers portal (used together with <see cref="ClientSecret"/>).
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Client Secret from Sber Developers portal (used together with <see cref="ClientId"/>).
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// API access scope (<see cref="GigaChatDefaults.ScopePers"/>, <see cref="GigaChatDefaults.ScopeB2B"/>, or <see cref="GigaChatDefaults.ScopeCorp"/>).
    /// Defaults to <see cref="GigaChatDefaults.ScopePers"/>.
    /// </summary>
    public string Scope { get; set; } = GigaChatDefaults.ScopePers;

    /// <summary>
    /// Default model to use for completions if none is specified in request or options.
    /// Defaults to "GigaChat".
    /// </summary>
    public string DefaultModel { get; set; } = GigaChatDefaults.ModelGigaChat;

    /// <summary>
    /// Default model to use for embeddings generation.
    /// Defaults to "Embeddings".
    /// </summary>
    public string DefaultEmbeddingModel { get; set; } = GigaChatDefaults.ModelEmbeddings;

    /// <summary>
    /// Base URL for GigaChat API. Defaults to "https://gigachat.devices.sberbank.ru/api/v1/".
    /// </summary>
    public Uri BaseUrl { get; set; } = new(GigaChatDefaults.DefaultBaseUrl);

    /// <summary>
    /// URL for OAuth token authentication. Defaults to "https://ngw.devices.sberbank.ru:9443/api/v2/oauth".
    /// </summary>
    public Uri AuthUrl { get; set; } = new(GigaChatDefaults.DefaultAuthUrl);

    /// <summary>
    /// Whether to ignore SSL validation errors for Russian Trusted Root CA certificates used by Sberbank endpoints.
    /// Defaults to true to ensure out-of-the-box compatibility on machines without Russian CA certificates.
    /// </summary>
    public bool IgnoreSslErrors { get; set; } = true;

    /// <summary>
    /// HTTP request timeout. Defaults to 120 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Optional custom <see cref="HttpMessageHandler"/> for advanced HTTP proxy or certificate configuration.
    /// </summary>
    public HttpMessageHandler? CustomHttpMessageHandler { get; set; }

    /// <summary>
    /// Optional pre-configured <see cref="HttpClient"/> instance to use.
    /// </summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>
    /// Initializes an empty options instance.
    /// </summary>
    public GigaChatClientOptions()
    {
    }

    /// <summary>
    /// Initializes options using an Authorization Key.
    /// </summary>
    /// <param name="authKey">Base64 Authorization Key from Sber portal.</param>
    /// <param name="ignoreSslErrors">Whether to ignore Russian CA SSL errors.</param>
    public GigaChatClientOptions(string authKey, bool ignoreSslErrors = true)
    {
        AuthKey = authKey;
        IgnoreSslErrors = ignoreSslErrors;
    }

    /// <summary>
    /// Initializes options using Client ID and Client Secret.
    /// </summary>
    /// <param name="clientId">Client ID from Sber Developers portal.</param>
    /// <param name="clientSecret">Client Secret from Sber Developers portal.</param>
    /// <param name="ignoreSslErrors">Whether to ignore Russian CA SSL errors.</param>
    public GigaChatClientOptions(string clientId, string clientSecret, bool ignoreSslErrors = true)
    {
        ClientId = clientId;
        ClientSecret = clientSecret;
        IgnoreSslErrors = ignoreSslErrors;
    }

    /// <summary>
    /// Creates options configured with an Authorization Key.
    /// </summary>
    public static GigaChatClientOptions FromAuthKey(string authKey, string scope = GigaChatDefaults.ScopePers, bool ignoreSslErrors = true)
        => new(authKey, ignoreSslErrors) { Scope = scope };

    /// <summary>
    /// Creates options configured with Client ID and Client Secret.
    /// </summary>
    public static GigaChatClientOptions FromClientCredentials(string clientId, string clientSecret, string scope = GigaChatDefaults.ScopePers, bool ignoreSslErrors = true)
        => new(clientId, clientSecret, ignoreSslErrors) { Scope = scope };

    /// <summary>
    /// Gets the basic authorization header value.
    /// Uses <see cref="AuthKey"/> if provided, otherwise constructs Base64(ClientId:ClientSecret).
    /// </summary>
    public string GetAuthorizationHeaderValue()
    {
        if (!string.IsNullOrWhiteSpace(AuthKey))
        {
            return AuthKey.Trim();
        }

        if (!string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret))
        {
            var raw = $"{ClientId.Trim()}:{ClientSecret.Trim()}";
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        }

        throw new InvalidOperationException(
            "GigaChat credentials are missing. Please provide either 'AuthKey' (Authorization Key) or both 'ClientId' and 'ClientSecret'.");
    }
}
