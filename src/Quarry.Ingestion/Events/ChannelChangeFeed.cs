namespace Quarry.Ingestion.Events;

using System.Runtime.CompilerServices;
using System.Threading.Channels;

/// <summary>
/// Потокобезопасная реализация Change Feed в оперативной памяти на базе System.Threading.Channels.
/// </summary>
public sealed class ChannelChangeFeed : IChangeFeed
{
    private readonly Channel<DocumentChanged> _channel;

    public ChannelChangeFeed(int capacity = 1000)
    {
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };
        _channel = Channel.CreateBounded<DocumentChanged>(options);
    }

    public async Task PublishChangeAsync(DocumentChanged change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        await _channel.Writer.WriteAsync(change, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<DocumentChanged> ReadChangesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out var item))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
        }
    }
}
