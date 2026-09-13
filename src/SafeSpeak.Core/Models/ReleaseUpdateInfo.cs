using System.Text;

namespace SafeSpeak.Core.Models;

/// <summary>
/// Authoritative single source of truth for release update highlights and
/// the opening setup update prompt's narrator configuration.
/// </summary>
public static class ReleaseUpdateInfo
{
    /// <summary>
    /// Monotonically increasing setup guide version. When incremented, existing
    /// users are automatically prompted on application launch to review what's new.
    /// </summary>
    public const int CurrentGuideVersion = 2;

    /// <summary>
    /// Granular bullet points of user-facing changes and improvements in the current release.
    /// </summary>
    public static readonly IReadOnlyList<string> CurrentHighlights = new[]
    {
        "Streaming Platforms: Saved TikTok Direct username is now displayed and announced on Live and Settings.",
        "Navigation and Speech: Fixed tutorial audio sequencing, tab navigation flow, and relocated chat replies filtering to Settings.",
        "Safe and Non-Destructive: Your existing tokens, models, and custom words are preserved."
    };

    /// <summary>
    /// Title of the highlights section.
    /// </summary>
    public static string Title => "What's New in this Version:";

    /// <summary>
    /// Generates the complete spoken narrator announcement for the setup update prompt dialog.
    /// </summary>
    public static string GetAnnouncementText(IReadOnlyList<string>? highlights = null)
    {
        var items = highlights ?? CurrentHighlights;
        var sb = new StringBuilder();
        sb.Append("SafeSpeak settings and setup update. New settings and connectors are available. What's new in this version: ");
        foreach (var item in items)
        {
            sb.Append(item);
            if (!item.EndsWith('.')) sb.Append('.');
            sb.Append(' ');
        }
        sb.Append("Press Y to review the setup guide, press N to keep current settings and continue into SafeSpeak, or press R to repeat this announcement.");
        return sb.ToString();
    }

    /// <summary>
    /// Generates the accessibility HelpText for external screen readers (Windows Narrator, NVDA, JAWS).
    /// </summary>
    public static string GetHelpText(IReadOnlyList<string>? highlights = null)
    {
        var items = highlights ?? CurrentHighlights;
        var sb = new StringBuilder();
        sb.Append("SafeSpeak settings and setup options have updated. What's new in this version: ");
        foreach (var item in items)
        {
            sb.Append(item);
            if (!item.EndsWith('.')) sb.Append('.');
            sb.Append(' ');
        }
        sb.Append("Press Y to review the setup guide, press N to keep current settings, or press R to repeat the announcement.");
        return sb.ToString();
    }

    /// <summary>
    /// Formats the highlights with bullet characters for visual UI presentation.
    /// </summary>
    public static IReadOnlyList<string> GetFormattedBulletHighlights(IReadOnlyList<string>? highlights = null)
    {
        var items = highlights ?? CurrentHighlights;
        var result = new List<string>(items.Count);
        foreach (var item in items)
        {
            result.Add($"\u2022 {item}");
        }
        return result.AsReadOnly();
    }
}
