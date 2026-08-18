using System.Globalization;
using System.Text.Json;
using SafeSpeak.Core.Chat;

namespace SafeSpeak.Infrastructure.TikFinity;

public static class TikFinityEventParser
{
    public static bool TryParseChatMessage(
        ReadOnlyMemory<byte> payload,
        out ChatMessage? message,
        DateTimeOffset? receivedAt = null)
    {
        message = null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetString(root, "event", out string? eventName) ||
                !string.Equals(eventName, "chat", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            JsonElement data = root.TryGetProperty("data", out JsonElement dataElement) &&
                               dataElement.ValueKind == JsonValueKind.Object
                ? dataElement
                : root;

            if (!TryGetFirstString(data, ["comment", "text", "message"], out string? text) ||
                string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            JsonElement user = data.TryGetProperty("user", out JsonElement userElement) &&
                               userElement.ValueKind == JsonValueKind.Object
                ? userElement
                : data;

            _ = TryGetFirstString(user, ["uniqueId", "userId", "id"], out string? userId);
            _ = TryGetFirstString(user, ["nickname", "displayName", "uniqueId"], out string? displayName);
            _ = TryGetFirstString(data, ["msgId", "messageId", "id"], out string? messageId);

            userId = string.IsNullOrWhiteSpace(userId) ? "unknown" : userId;
            displayName = string.IsNullOrWhiteSpace(displayName) ? userId : displayName;
            messageId = string.IsNullOrWhiteSpace(messageId) ? Guid.NewGuid().ToString("N") : messageId;

            message = new(
                messageId,
                userId,
                displayName,
                text,
                ReadAudienceRole(data, user),
                ReadTimestamp(data) ?? receivedAt ?? DateTimeOffset.UtcNow);

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static AudienceRole ReadAudienceRole(JsonElement data, JsonElement user)
    {
        AudienceRole role = AudienceRole.Guest;

        if (GetBoolean(data, user, "isFollower", "followRole"))
        {
            role |= AudienceRole.Follower;
        }

        if (GetBoolean(data, user, "isSubscriber", "isSub"))
        {
            role |= AudienceRole.Subscriber;
        }

        if (GetBoolean(data, user, "isModerator", "isMod"))
        {
            role |= AudienceRole.Moderator;
        }

        return role;
    }

    private static bool GetBoolean(JsonElement primary, JsonElement secondary, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            if (TryGetBoolean(primary, propertyName, out bool value) ||
                TryGetBoolean(secondary, propertyName, out value))
            {
                return value;
            }
        }

        return false;
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement element)
    {
        if (!element.TryGetProperty("createTime", out JsonElement property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out long unixTime))
        {
            try
            {
                return unixTime > 10_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds(unixTime)
                    : DateTimeOffset.FromUnixTimeSeconds(unixTime);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        return property.ValueKind == JsonValueKind.String &&
               DateTimeOffset.TryParse(property.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
            ? parsed
            : null;
    }

    private static bool TryGetFirstString(JsonElement element, IEnumerable<string> propertyNames, out string? value)
    {
        foreach (string propertyName in propertyNames)
        {
            if (TryGetString(element, propertyName, out value))
            {
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return false;
        }

        value = property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null,
        };

        return value is not null;
    }

    private static bool TryGetBoolean(JsonElement element, string propertyName, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return false;
        }

        if (property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = property.GetBoolean();
            return true;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int number))
        {
            value = number != 0;
            return true;
        }

        return property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out value);
    }
}
