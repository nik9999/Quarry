namespace Quarry.Ingestion.Events;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quarry.Core.Abstractions;
using Quarry.Ingestion.Indexing;

/// <summary>
/// Фоновая служба (BackgroundService), подписывающаяся на поток Change Feed и автоматически выполняющая переиндексацию документов.
/// </summary>
public sealed class DocumentReingestService : BackgroundService
{
    private readonly IChangeFeed _changeFeed;
    private readonly IIndexingPipeline _pipeline;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<DocumentReingestService> _logger;

    public DocumentReingestService(
        IChangeFeed changeFeed,
        IIndexingPipeline pipeline,
        IVectorStore vectorStore,
        ILogger<DocumentReingestService>? logger = null)
    {
        _changeFeed = changeFeed ?? throw new ArgumentNullException(nameof(changeFeed));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _logger = logger ?? NullLogger<DocumentReingestService>.Instance;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Запущена фоновая служба синхронизации и переиндексации документов.");

        try
        {
            await foreach (var change in _changeFeed.ReadChangesAsync(stoppingToken).ConfigureAwait(false))
            {
                stoppingToken.ThrowIfCancellationRequested();

                try
                {
                    switch (change.ChangeType)
                    {
                        case DocumentChangeType.Created:
                        case DocumentChangeType.Updated:
                            if (change.Document is not null)
                            {
                                _logger.LogDebug("Обработка изменения документа {DocumentId} ({ChangeType})", change.DocumentId, change.ChangeType);
                                await _pipeline.IngestAsync([change.Document], stoppingToken).ConfigureAwait(false);
                            }
                            break;

                        case DocumentChangeType.Deleted:
                            _logger.LogDebug("Удаление документа {DocumentId} из векторного хранилища", change.DocumentId);
                            // Delete document chunks from vector store
                            await _vectorStore.DeleteAsync([change.DocumentId], stoppingToken).ConfigureAwait(false);
                            break;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при обработке изменения документа {DocumentId}", change.DocumentId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown
        }
        finally
        {
            _logger.LogInformation("Фоновая служба синхронизации и переиндексации документов остановлена.");
        }
    }
}
