using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Connectors;

/// <summary>
/// Event simulator for testing moderation, audio routing, and queue behavior offline without live streams.
/// </summary>
public sealed class OfflineEventSimulator : ITikFinityConnector
{
    private ConnectionState _state = ConnectionState.Disconnected;
    private CancellationTokenSource? _simulationCts;

    public ConnectionState State => _state;
    public string EndpointUrl => "simulator://offline";

    public event EventHandler<ChatMessage>? MessageReceived;
    public event EventHandler<LivestreamEvent>? EventReceived;
    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _state = ConnectionState.Connected;
        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(ConnectionState.Connected, "Offline Simulator Active"));
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        _simulationCts?.Cancel();
        _state = ConnectionState.Disconnected;
        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(ConnectionState.Disconnected, "Simulator Stopped"));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Injects a single custom message immediately into the pipeline.
    /// </summary>
    public void InjectMessage(string text, string author = "test_user", AuthorTier tier = AuthorTier.Viewer)
    {
        var msg = new ChatMessage
        {
            Author = author,
            AuthorDisplayName = author,
            RawText = text,
            AuthorTier = tier,
            TimestampUtc = DateTimeOffset.UtcNow
        };

        MessageReceived?.Invoke(this, msg);
        EventReceived?.Invoke(this, new LivestreamEvent
        {
            Type = LivestreamEventType.Chat,
            Author = msg.Author,
            AuthorDisplayName = msg.AuthorDisplayName,
            Text = msg.RawText,
            AuthorTier = msg.AuthorTier,
            TimestampUtc = msg.TimestampUtc
        });
    }

    /// <summary>
    /// Starts an automated stream of realistic test messages based on a chosen test scenario.
    /// </summary>
    public async Task RunScenarioAsync(string scenarioName, int intervalMs = 1200, CancellationToken cancellationToken = default)
    {
        _simulationCts?.Cancel();
        _simulationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _simulationCts.Token;

        var messages = GetScenarioMessages(scenarioName);

        foreach (var (author, text, tier) in messages)
        {
            if (token.IsCancellationRequested) break;

            InjectMessage(text, author, tier);
            await Task.Delay(intervalMs, token);
        }
    }

    private static IEnumerable<(string author, string text, AuthorTier tier)> GetScenarioMessages(string scenario)
    {
        return scenario.ToLowerInvariant() switch
        {
            "attacks" or "evasion" => new List<(string, string, AuthorTier)>
            {
                ("hacker1", "Hey great stream! Check https://scam-link.xyz/free", AuthorTier.Viewer),
                ("troll_99", "f\u200Bu\u200Bc\u200Bk you streamer", AuthorTier.Viewer), // Zero-width space evasion
                ("cyrillic_bot", "You are a f\u0430ggot", AuthorTier.Viewer), // Cyrillic 'а' homoglyph
                ("spacer", "k y s right now", AuthorTier.Viewer), // Spaced letters
                ("leeter", "k1ll your$elf loser", AuthorTier.Viewer), // Leetspeak substitution
                ("mixed_script", "What a ѕtupid streamer", AuthorTier.Viewer), // Cyrillic 'ѕ'
                ("stretcher", "fuuuuuuuuuck off", AuthorTier.Viewer) // Character repetition
            },
            "toxicity" or "ai" => new List<(string, string, AuthorTier)>
            {
                ("clean_fan", "Love the gameplay today!", AuthorTier.Subscriber),
                ("subtle_bully", "Nobody in this chat actually likes you, you're a waste of space", AuthorTier.Viewer),
                ("gamer_guy", "GG that was a close round", AuthorTier.Follower),
                ("toxic_viewer", "You're so pathetic and disgusting at this game, go away and die", AuthorTier.Viewer),
                ("mod_alice", "Don't forget to like the stream everyone!", AuthorTier.Moderator)
            },
            _ => new List<(string, string, AuthorTier)> // Clean default stream
            {
                ("sarah_g", "Hello everyone! Hope you are having a wonderful day!", AuthorTier.Follower),
                ("alex_stream", "What game are we playing next?", AuthorTier.Subscriber),
                ("sammy99", "First time watching, awesome vibe here!", AuthorTier.Viewer),
                ("mod_mike", "Welcome in new followers!", AuthorTier.Moderator),
                ("jenny_k", "Can you show that combo again?", AuthorTier.Subscriber)
            }
        };
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _simulationCts?.Dispose();
    }
}
