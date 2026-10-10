namespace Quarry.Ingestion.Multimodal;

/// <summary>
/// Генератор описаний (кэпшенов) для изображений с поддержкой подключения Vision LLM.
/// </summary>
public interface IImageCaptioner
{
    /// <summary>
    /// Асинхронно генерирует текстовое описание изображения.
    /// </summary>
    Task<string> CaptionAsync(
        ReadOnlyMemory<byte> imageBytes,
        string mimeType,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Стандартный генератор описаний изображений с поддержкой делегата внешней Vision-модели или эвристической сводки.
/// </summary>
public sealed class DefaultImageCaptioner : IImageCaptioner
{
    private readonly Func<ReadOnlyMemory<byte>, string, CancellationToken, Task<string>>? _customCaptioner;

    public DefaultImageCaptioner(Func<ReadOnlyMemory<byte>, string, CancellationToken, Task<string>>? customCaptioner = null)
    {
        _customCaptioner = customCaptioner;
    }

    public async Task<string> CaptionAsync(
        ReadOnlyMemory<byte> imageBytes,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        if (_customCaptioner is not null)
        {
            return await _customCaptioner(imageBytes, mimeType, cancellationToken).ConfigureAwait(false);
        }

        var description = $"Изображение ({mimeType}, {imageBytes.Length} байт)";
        return await Task.FromResult(description).ConfigureAwait(false);
    }
}
