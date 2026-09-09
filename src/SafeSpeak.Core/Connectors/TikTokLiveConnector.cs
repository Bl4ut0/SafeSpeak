using System.Net.Http;
using System.Net.WebSockets;
using SafeSpeak.Core.Connectors.TikTok;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Connectors;

/// <summary>Experimental public LIVE connection. No external signer or relay.</summary>
public sealed class TikTokLiveConnector : ISourceConnector
{
    public static SourceConnectorDescriptor ConnectorDescriptor { get; } = new(
        "tiktok-direct", "TikTok Direct (test)", "TikTok LIVE directly by username",
        "Experimental direct connection to a public TikTok LIVE stream",
        SourceConnectorCapabilities.Chat | SourceConnectorCapabilities.Gifts | SourceConnectorCapabilities.Follows |
        SourceConnectorCapabilities.Shares | SourceConnectorCapabilities.Subscriptions | SourceConnectorCapabilities.Joins |
        SourceConnectorCapabilities.Likes);

    private readonly string _username;
    private readonly Func<ITikTokLiveSession> _sessionFactory;
    private readonly TimeSpan _retryDelay;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _disposeLock = new();
    private CancellationTokenSource? _lifetime;
    private Task _loop = Task.CompletedTask;
    private Task? _disposeTask;
    private int _disposed;
    private volatile ConnectionState _state;

    public TikTokLiveConnector(string username = "") : this(username, () => new TikTokLiveSession(), TimeSpan.FromSeconds(15)) { }
    internal TikTokLiveConnector(string username, Func<ITikTokLiveSession> sessionFactory, TimeSpan retryDelay)
    { _username = username; _sessionFactory = sessionFactory; _retryDelay = retryDelay; }

    public SourceConnectorDescriptor Descriptor => ConnectorDescriptor;
    public ConnectionState State => _state;
    public string EndpointDescription => TryNormalizeUsername(_username, out var name) ? $"TikTok LIVE @{name}" : "TikTok username is required";
    public event EventHandler<LivestreamEvent>? EventReceived;
    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    public static bool TryNormalizeUsername(string? value, out string username)
    {
        username = (value ?? "").Trim();
        if (username.StartsWith('@')) username = username[1..];
        if (username.Length is < 1 or > 24 || username.EndsWith('.') ||
            username.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '.'))
        { username = ""; return false; }
        return true;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (!_loop.IsCompleted) return;
            if (!TryNormalizeUsername(_username, out string username))
            { SetState(ConnectionState.Faulted, "Enter a TikTok username in Settings, then choose Save and connect."); return; }
            _lifetime?.Dispose();
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = _lifetime.Token;
            _loop = Task.Run(() => RunAsync(username, token), CancellationToken.None);
        }
        finally { _gate.Release(); }
    }

    private async Task RunAsync(string username, CancellationToken ct)
    {
        var decoder = new TikTokEventDecoder();
        int attempts = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                SetState(attempts == 0 ? ConnectionState.Connecting : ConnectionState.Reconnecting,
                    $"Connecting to @{username}. The creator must be LIVE.");
                try
                {
                    bool ended = await _sessionFactory().RunAsync(username, decoder,
                        () => { if (!ct.IsCancellationRequested) SetState(ConnectionState.Connected, $"Receiving LIVE events from @{username}."); },
                        e => { if (!ct.IsCancellationRequested) EventReceived?.Invoke(this, e); }, ct).ConfigureAwait(false);
                    if (ended) { SetState(ConnectionState.Disconnected, "The TikTok LIVE stream ended. Reconnect when the creator is LIVE again."); return; }
                    SetState(ConnectionState.Reconnecting, "TikTok closed the connection. SafeSpeak will retry.");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (TikTokConnectionException ex)
                {
                    SetState(ex.Retryable ? ConnectionState.Reconnecting : ConnectionState.Faulted, ex.Message);
                    if (!ex.Retryable) return;
                }
                catch (InvalidDataException)
                { SetState(ConnectionState.Faulted, "TikTok sent data this test connector cannot safely read. Try reconnecting later."); return; }
                catch (Exception ex) when (ex is HttpRequestException or WebSocketException or IOException or OperationCanceledException)
                { SetState(ConnectionState.Reconnecting, "TikTok direct connection failed or timed out. SafeSpeak will retry."); }
                catch (Exception)
                { SetState(ConnectionState.Faulted, "TikTok Direct could not continue. Try reconnecting."); return; }
                attempts++;
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(_retryDelay.TotalMilliseconds * Math.Min(attempts, 4), 60000)), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { if (ct.IsCancellationRequested) SetState(ConnectionState.Disconnected, "Disconnected."); }
    }

    public async Task DisconnectAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _lifetime?.Cancel();
            // All session I/O shares this token. Keep the task retained if a broken
            // transport misses the deadline, preventing overlapping receive loops.
            try { await _loop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (TimeoutException) { SetState(ConnectionState.Faulted, "TikTok connection is still stopping."); return; }
            _lifetime?.Dispose();
            _lifetime = null;
            SetState(ConnectionState.Disconnected, "Disconnected.");
        }
        finally { _gate.Release(); }
    }

    private void SetState(ConnectionState state, string message)
    {
        _state = state;
        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(state, message));
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            if (_disposeTask is null) { Volatile.Write(ref _disposed, 1); _disposeTask = DisconnectAsync(); }
            return new ValueTask(_disposeTask);
        }
    }
}
