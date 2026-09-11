using System.Globalization;
using System.Text;
using SafeSpeak.Core.Models;

namespace SafeSpeak.Core.Connectors.TikTok;

internal sealed class TikTokEventDecoder
{
    private const int MaximumRememberedEvents = 4096;
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Queue<string> _seenOrder = new();

    // Retain IDs across automatic reconnects; room ID forms part of each key.
    public LivestreamEvent? Decode(string method, ReadOnlyMemory<byte> payload, string roomId,
        ulong envelopeId, bool history, DateTimeOffset receivedAt)
    {
        if (history) return null;
        try
        {
            var data = new TikTokProtobuf(payload);
            var common = data.Message(1);
            ulong id = envelopeId != 0 ? envelopeId : common.Number(2);
            ulong created = common.Number(4);
            // Providers sometimes include history without the envelope flag.
            if (created > 0 && created < (ulong)Math.Max(0, receivedAt.AddMinutes(-2).ToUnixTimeSeconds())) return null;
            LivestreamEventType? type = method switch
            {
                "WebcastChatMessage" => LivestreamEventType.Chat,
                "WebcastGiftMessage" => LivestreamEventType.Gift,
                "WebcastLikeMessage" => LivestreamEventType.Like,
                "WebcastMemberMessage" when data.Number(10) == 1 => LivestreamEventType.Join,
                "WebcastSocialMessage" when data.Number(4) == 1 => LivestreamEventType.Follow,
                "WebcastSocialMessage" when data.Number(4) == 3 => LivestreamEventType.Share,
                "WebcastSubNotifyMessage" => LivestreamEventType.Subscribe,
                _ => null
            };
            if (type is null) return null;
            var user = data.Message(type == LivestreamEventType.Gift ? 7 : type == LivestreamEventType.Like ? 5 : 2);
            string author = user.Text(38, 256);
            if (string.IsNullOrWhiteSpace(author)) author = user.Text(46, 256);
            if (string.IsNullOrWhiteSpace(author) && user.Number(1) != 0) author = user.Number(1).ToString(CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(author)) return null;
            string displayName = user.Text(3, 1024);
            string text = type == LivestreamEventType.Chat ? data.Text(3) : "";
            if (type == LivestreamEventType.Chat && string.IsNullOrWhiteSpace(text)) return null;
            var identity = data.Message(type == LivestreamEventType.Chat ? 18 : type == LivestreamEventType.Gift ? 32 : 0);
            var attr = user.Message(32);
            bool moderator = identity.Number(5) == 1 || attr.Number(2) == 1 || attr.Number(3) == 1;
            bool subscriber = identity.Number(2) == 1 || user.Message(63).Number(2) == 1 || user.Number(1090) == 1 || type == LivestreamEventType.Subscribe;
            bool follower = identity.Number(4) == 1 || user.Number(1029) == 1 || type == LivestreamEventType.Follow;
            string giftName = "";
            int giftCount = 1;
            int diamonds = 0;
            string key = id == 0 ? "" : $"{roomId}:{method}:{id}";
            if (type == LivestreamEventType.Gift)
            {
                var gift = data.Message(15);
                // Combo counts are cumulative. Announce the completed streak once.
                if (gift.Number(11) == 1)
                {
                    if (data.Number(9) != 1) return null;
                    if (data.Number(11) != 0) key = $"{roomId}:gift:{author}:{data.Number(2)}:{data.Number(11)}";
                }
                giftName = gift.Text(16, 1024);
                if (string.IsNullOrWhiteSpace(giftName)) giftName = "TikTok";
                giftCount = (int)Math.Clamp(data.Number(5), 1UL, 100000UL);
                diamonds = (int)Math.Min(gift.Number(12), int.MaxValue);
            }
            if (key.Length > 0 && !_seen.Add(key)) return null;
            if (key.Length > 0)
            {
                _seenOrder.Enqueue(key);
                while (_seenOrder.Count > MaximumRememberedEvents) _seen.Remove(_seenOrder.Dequeue());
            }
            return new LivestreamEvent
            {
                Platform = "TikTok LIVE", Type = type.Value, Author = author,
                AuthorDisplayName = string.IsNullOrWhiteSpace(displayName) ? author : displayName,
                Text = text, GiftName = giftName, GiftCount = giftCount, DiamondCount = diamonds,
                IsModerator = moderator, IsSubscriber = subscriber,
                AuthorTier = moderator ? AuthorTier.Moderator : subscriber ? AuthorTier.Subscriber : follower ? AuthorTier.Follower : AuthorTier.Viewer,
                TimestampUtc = receivedAt
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or DecoderFallbackException or OverflowException)
        {
            return null; // Malformed or unknown provider data never reaches speech.
        }
    }
}
