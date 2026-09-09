using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Connectors;

/// <summary>Owns exactly one source and serializes switching, connect, and shutdown.</summary>
public sealed class SourceConnectorHost : ISourceConnector
{
    private ISourceConnector _current;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _disposeLock = new();
    private Task? _disposeTask;
    private int _disposed;
    private int _generation;

    public SourceConnectorHost(ISourceConnector source) { _current = source; Subscribe(source); }
    public int Generation => Volatile.Read(ref _generation);
    public SourceConnectorDescriptor Descriptor => _current.Descriptor;
    public ConnectionState State => _current.State;
    public string EndpointDescription => _current.EndpointDescription;
    public event EventHandler<LivestreamEvent>? EventReceived;
    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    public async Task ReplaceAsync(Func<ISourceConnector> create, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            Unsubscribe(_current);
            Interlocked.Increment(ref _generation);
            await _current.DisposeAsync().ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            _current = create();
            Subscribe(_current);
            StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(_current.State, "Source selected. SafeSpeak is disarmed."));
        }
        finally { _gate.Release(); }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            await _current.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task DisconnectAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { await _current.DisconnectAsync().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private void Subscribe(ISourceConnector source)
    { source.EventReceived += OnEvent; source.StateChanged += OnState; }
    private void Unsubscribe(ISourceConnector source)
    { source.EventReceived -= OnEvent; source.StateChanged -= OnState; }
    private void OnEvent(object? sender, LivestreamEvent value)
    { if (Volatile.Read(ref _disposed) == 0 && ReferenceEquals(sender, _current)) EventReceived?.Invoke(this, value); }
    private void OnState(object? sender, ConnectionStateChangedEventArgs value)
    { if (Volatile.Read(ref _disposed) == 0 && ReferenceEquals(sender, _current)) StateChanged?.Invoke(this, value); }

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            if (_disposeTask is null)
            {
                Volatile.Write(ref _disposed, 1);
                _shutdown.Cancel();
                _disposeTask = DisposeCoreAsync();
            }
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { Unsubscribe(_current); await _current.DisposeAsync().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
}
