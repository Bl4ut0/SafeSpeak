using System.Collections.Concurrent;
using System.Diagnostics;
using SafeSpeak.Core.AI;
using SafeSpeak.Core.Audio;
using SafeSpeak.Core.Models;
using SafeSpeak.Core.Moderation;

namespace SafeSpeak.Core.Tests;

/// <summary>
/// Synthetic load and stress tests validating SafeSpeak's stability, bounded memory,
/// rate limiting, queue drop-tail behavior, stale message eviction, and chatter-to-chatter
/// @reply filtering under heavy multi-threaded chat load.
/// </summary>
public sealed class SyntheticChatLoadStressTests
{
    private sealed class FastCleanClassifier : IIntentClassifier
    {
        private int _classifyCount;
        public string ModelName => "Synthetic fast mock classifier";
        public bool IsModelLoaded => true;
        public int ClassifyCount => Volatile.Read(ref _classifyCount);

        public Task<IntentClassificationResult> ClassifyAsync(
            string text,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _classifyCount);
            return Task.FromResult(new IntentClassificationResult
            {
                IsToxic = false,
                ToxicityScore = 0.05,
                ModelUsed = "FastCleanClassifier"
            });
        }

        public void Dispose() { }
    }

    [Fact]
    public async Task HighConcurrency_100Chatters_1000MessagesBurst_RemainsStableAndDeterministic()
    {
        // Simulate a live stream burst: 100 concurrent chatters sending 10 messages each (1,000 total)
        using var classifier = new FastCleanClassifier();
        var config = new ModerationConfig
        {
            UserCooldownSeconds = 0,
            MessageRateLimitEnabled = true,
            MessageRateWindow = MessageRateWindow.TenSeconds,
            PerUserMessageLimit = 3, // Each chatter is capped at 3 messages / 10s
            StreamMessageLimit = 2000,
            AudienceMode = AudienceMode.All
        };
        using var pipeline = new ModerationPipeline(config, intentClassifier: classifier);

        const int userCount = 100;
        const int messagesPerUser = 10;
        var decisions = new ConcurrentBag<ModerationDecision>();

        var chatTasks = Enumerable.Range(1, userCount).Select(userId =>
        {
            string author = $"chatter_{userId}";
            return Task.Run(async () =>
            {
                for (int msgIndex = 1; msgIndex <= messagesPerUser; msgIndex++)
                {
                    var chat = new ChatMessage
                    {
                        Author = author,
                        AuthorDisplayName = $"Chatter {userId}",
                        RawText = $"Message #{msgIndex} from chatter {userId} talking about the gameplay!",
                        TimestampUtc = DateTimeOffset.UtcNow
                    };
                    var decision = await pipeline.ProcessMessageAsync(chat);
                    decisions.Add(decision);
                }
            });
        });

        await Task.WhenAll(chatTasks);

        // Verification: Exactly 1,000 messages processed without exceptions or deadlocks
        Assert.Equal(userCount * messagesPerUser, decisions.Count);

        // Per-user rate limit test: each of the 100 users had 3 accepted messages and 7 throttled messages
        var groupedByAuthor = decisions.GroupBy(d => d.Message.Author).ToList();
        Assert.Equal(userCount, groupedByAuthor.Count);

        foreach (var userGroup in groupedByAuthor)
        {
            int passedCount = userGroup.Count(d => d.Passed);
            int throttledCount = userGroup.Count(d => !d.Passed && d.ReasonCode == ModerationReasonCode.UserCooldown);

            Assert.Equal(3, passedCount);
            Assert.Equal(7, throttledCount);
        }
    }

    [Fact]
    public async Task RateLimiter_UnderSustainedFlood_EnforcesStreamLimit()
    {
        // 500 distinct viewers post simultaneously into a stream limit of 50
        using var classifier = new FastCleanClassifier();
        var config = new ModerationConfig
        {
            UserCooldownSeconds = 0,
            MessageRateLimitEnabled = true,
            MessageRateWindow = MessageRateWindow.TenSeconds,
            PerUserMessageLimit = 10,
            StreamMessageLimit = 50,
            AudienceMode = AudienceMode.All
        };
        using var pipeline = new ModerationPipeline(config, intentClassifier: classifier);

        const int totalFloodMessages = 500;
        var decisions = new ConcurrentBag<ModerationDecision>();

        await Parallel.ForEachAsync(
            Enumerable.Range(1, totalFloodMessages),
            new ParallelOptions { MaxDegreeOfParallelism = 24 },
            async (id, ct) =>
            {
                var msg = new ChatMessage
                {
                    Author = $"flood_user_{id}",
                    AuthorDisplayName = $"Flooder {id}",
                    RawText = $"Flood spam attempt {id}",
                    TimestampUtc = DateTimeOffset.UtcNow
                };
                var decision = await pipeline.ProcessMessageAsync(msg, ct);
                decisions.Add(decision);
            });

        Assert.Equal(totalFloodMessages, decisions.Count);
        int passed = decisions.Count(d => d.Passed);
        int streamThrottled = decisions.Count(d => !d.Passed && d.ReasonCode == ModerationReasonCode.SpamPattern);

        // Exactly 50 messages allowed; the remaining 450 rejected at the rate limit gate
        Assert.Equal(50, passed);
        Assert.Equal(450, streamThrottled);
    }

    [Fact]
    public void TtsQueue_CapacityDropTail_NeverExceedsCapacityUnderFlood()
    {
        var mockTts = new MockTtsEngine();
        using var router = new MockAudioRouter();
        const int capacity = 50;
        var queue = new TtsQueue(mockTts, router, capacity: capacity);
        queue.ArmAutomatic();

        const int floodCount = 300;
        int enqueuedCount = 0;
        int rejectedCount = 0;

        for (int i = 0; i < floodCount; i++)
        {
            var decision = new ModerationDecision
            {
                Message = new ChatMessage { Author = $"user_{i}", RawText = $"Message {i}" },
                Disposition = ModerationDisposition.Approved,
                SpokenText = $"Message {i}"
            };

            bool enqueued = queue.Enqueue(decision);
            if (enqueued)
            {
                enqueuedCount++;
            }
            else
            {
                rejectedCount++;
            }

            // At every step, queue count must never exceed capacity
            Assert.True(queue.Count <= capacity);
        }

        Assert.Equal(floodCount, enqueuedCount + rejectedCount);
        Assert.True(rejectedCount > 0, "Queue must drop-tail when flooded beyond capacity.");
        Assert.True(queue.Count <= capacity);

        // Emergency Disarm clears everything instantly
        queue.Disarm();
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task TtsQueue_StaleMessages_AutomaticallyEvictedUnderBacklog()
    {
        var mockTts = new MockTtsEngine();
        using var router = new MockAudioRouter();
        var queue = new TtsQueue(mockTts, router, capacity: 50)
        {
            MaxQueueAgeSeconds = 2 // 2-second staleness cutoff
        };
        queue.ArmAutomatic();

        // Enqueue 3 stale messages (timestamped 10 seconds ago)
        for (int i = 0; i < 3; i++)
        {
            queue.Enqueue(new ModerationDecision
            {
                Message = new ChatMessage
                {
                    Author = $"old_user_{i}",
                    RawText = $"Old message {i}",
                    TimestampUtc = DateTimeOffset.UtcNow.AddSeconds(-10)
                },
                Disposition = ModerationDisposition.Approved,
                SpokenText = $"Old message {i}"
            });
        }

        // Enqueue 2 fresh messages (timestamped now)
        for (int i = 0; i < 2; i++)
        {
            queue.Enqueue(new ModerationDecision
            {
                Message = new ChatMessage
                {
                    Author = $"fresh_user_{i}",
                    RawText = $"Fresh message {i}",
                    TimestampUtc = DateTimeOffset.UtcNow
                },
                Disposition = ModerationDisposition.Approved,
                SpokenText = $"Fresh message {i}"
            });
        }

        // Wait for queue loop to process items
        var sw = Stopwatch.StartNew();
        while (queue.Count > 0 && sw.ElapsedMilliseconds < 2000)
        {
            await Task.Delay(20);
        }

        // Only the 2 fresh messages should have been spoken; the 3 stale messages were evicted
        Assert.Equal(2, mockTts.SpeakCount);
    }

    [Fact]
    public void TtsQueue_AdaptiveGap_CollapsesUnderBacklog()
    {
        const int maxGap = 1000;

        // Low backlog: 0 or 1 item -> normal spacing
        Assert.Equal(1000, TtsQueue.CalculateEffectiveInterMessageGap(maxGap, adaptive: true, pendingMessageCount: 0));
        Assert.Equal(1000, TtsQueue.CalculateEffectiveInterMessageGap(maxGap, adaptive: true, pendingMessageCount: 1));

        // Moderate backlog: 2 to 4 items -> 50% spacing (500 ms)
        Assert.Equal(500, TtsQueue.CalculateEffectiveInterMessageGap(maxGap, adaptive: true, pendingMessageCount: 2));
        Assert.Equal(500, TtsQueue.CalculateEffectiveInterMessageGap(maxGap, adaptive: true, pendingMessageCount: 4));

        // Higher backlog: 5 to 9 items -> 75% reduction (250 ms)
        Assert.Equal(250, TtsQueue.CalculateEffectiveInterMessageGap(maxGap, adaptive: true, pendingMessageCount: 5));
        Assert.Equal(250, TtsQueue.CalculateEffectiveInterMessageGap(maxGap, adaptive: true, pendingMessageCount: 9));

        // Critical backlog: 10+ items -> 0 ms (collapse spacing entirely to drain ASAP)
        Assert.Equal(0, TtsQueue.CalculateEffectiveInterMessageGap(maxGap, adaptive: true, pendingMessageCount: 10));
        Assert.Equal(0, TtsQueue.CalculateEffectiveInterMessageGap(maxGap, adaptive: true, pendingMessageCount: 50));

        // When adaptive spacing is disabled, gap stays fixed
        Assert.Equal(1000, TtsQueue.CalculateEffectiveInterMessageGap(maxGap, adaptive: false, pendingMessageCount: 15));
    }

    [Fact]
    public async Task ModerationPipeline_ChatReplies_FiltersViewerRepliesAndAllowsStreamerMentions()
    {
        using var classifier = new FastCleanClassifier();
        var config = new ModerationConfig
        {
            UserCooldownSeconds = 0,
            IgnoreChatReplies = true,
            StreamerUsername = "StreamerGuy",
            AudienceMode = AudienceMode.All
        };
        using var pipeline = new ModerationPipeline(config, intentClassifier: classifier);

        // 1. Chatter-to-chatter replies -> REJECTED
        var reply1 = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "viewer1",
            RawText = "@alice hello there!"
        });
        Assert.False(reply1.Passed);
        Assert.Equal(ModerationReasonCode.ChatReply, reply1.ReasonCode);
        Assert.Equal(string.Empty, reply1.SpokenText);

        var replyWithColon = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "viewer2",
            RawText = "@bob: check your dms"
        });
        Assert.False(replyWithColon.Passed);
        Assert.Equal(ModerationReasonCode.ChatReply, replyWithColon.ReasonCode);

        var replyWithPunctuation = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "viewer3",
            RawText = ".@charlie I totally agree"
        });
        Assert.False(replyWithPunctuation.Passed);
        Assert.Equal(ModerationReasonCode.ChatReply, replyWithPunctuation.ReasonCode);

        var replyWithParenthesis = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "viewer4",
            RawText = "(@dan) what happened?"
        });
        Assert.False(replyWithParenthesis.Passed);
        Assert.Equal(ModerationReasonCode.ChatReply, replyWithParenthesis.ReasonCode);

        // 2. Direct message to Streamer (matching StreamerUsername case-insensitively) -> APPROVED
        var streamerMention = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "viewer5",
            RawText = "@StreamerGuy love your stream!"
        });
        Assert.True(streamerMention.Passed);
        Assert.NotEmpty(streamerMention.SpokenText);

        var streamerMentionLower = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "viewer6",
            RawText = "@streamerguy great shot!"
        });
        Assert.True(streamerMentionLower.Passed);
        Assert.NotEmpty(streamerMentionLower.SpokenText);

        // 3. Mid-sentence mention (not a reply) -> APPROVED
        var midMention = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "viewer7",
            RawText = "Hey everyone tell @bob he is awesome"
        });
        Assert.True(midMention.Passed);

        // 4. Normal message without mentions -> APPROVED
        var normalMsg = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "viewer8",
            RawText = "Let's win this match!"
        });
        Assert.True(normalMsg.Passed);

        // 5. When IgnoreChatReplies is disabled -> chatter replies ARE allowed
        config.IgnoreChatReplies = false;
        var replyWhenDisabled = await pipeline.ProcessMessageAsync(new ChatMessage
        {
            Author = "viewer9",
            RawText = "@alice I can hear you now"
        });
        Assert.True(replyWhenDisabled.Passed);
    }
}
