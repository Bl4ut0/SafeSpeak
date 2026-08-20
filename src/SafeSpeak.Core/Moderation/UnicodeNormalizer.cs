using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SafeSpeak.Core.Moderation;

/// <summary>
/// Handles invisible character stripping, Unicode NFKD normalization, repetition collapsing, and token deobfuscation.
/// </summary>
public static partial class UnicodeNormalizer
{
    // Regex for matching URLs
    [GeneratedRegex(@"(?:https?:\/\/|www\.)[^\s]+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlRegex();

    // Regex for collapsing 3+ identical consecutive letters down to 2
    [GeneratedRegex(@"(.)\1{2,}", RegexOptions.Compiled)]
    private static partial Regex ConsecutiveRepeatRegex();

    // Regex for collapsing single spaced letters like "f u c k" into "fuck"
    [GeneratedRegex(@"(?<=\b[a-zA-Z])\s+(?=[a-zA-Z]\b)", RegexOptions.Compiled)]
    private static partial Regex SpacedLettersRegex();

    // Regex for multiple whitespace characters
    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex MultipleWhitespaceRegex();

    private static readonly HashSet<char> InvisibleCharacters = new()
    {
        '\u200B', // Zero Width Space
        '\u200C', // Zero Width Non-Joiner
        '\u200D', // Zero Width Joiner
        '\u200E', // Left-to-Right Mark
        '\u200F', // Right-to-Left Mark
        '\u202A', // Left-to-Right Embedding
        '\u202B', // Right-to-Left Embedding
        '\u202C', // Pop Directional Formatting
        '\u202D', // Left-to-Right Override
        '\u202E', // Right-to-Left Override
        '\u2060', // Word Joiner
        '\u2061', // Function Application
        '\u2062', // Invisible Times
        '\u2063', // Invisible Separator
        '\u2064', // Invisible Plus
        '\uFEFF', // Zero Width No-Break Space (BOM)
        '\u00AD', // Soft Hyphen
        '\u034F', // Combining Grapheme Joiner
        '\u180E', // Mongolian Vowel Separator
        '\u2800'  // Braille Pattern Blank (often used for invisible spacing)
    };

    /// <summary>
    /// Strips zero-width and invisible bypass characters.
    /// </summary>
    public static string StripInvisibleCharacters(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (!InvisibleCharacters.Contains(c) && !char.IsControl(c))
            {
                sb.Append(c);
            }
            else if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
            {
                sb.Append(' ');
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Decomposes Unicode characters (NFKD) and removes combining diacritical marks (e.g., Zalgo text).
    /// </summary>
    public static string RemoveDiacritics(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        string normalizedString = text.Normalize(NormalizationForm.FormKD);
        var stringBuilder = new StringBuilder(capacity: normalizedString.Length);

        for (int i = 0; i < normalizedString.Length; i++)
        {
            char c = normalizedString[i];
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark &&
                unicodeCategory != UnicodeCategory.SpacingCombiningMark &&
                unicodeCategory != UnicodeCategory.EnclosingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Removes URLs and hyperlinks from text.
    /// </summary>
    public static string StripUrls(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return UrlRegex().Replace(input, "[link removed]");
    }

    /// <summary>
    /// Collapses consecutive repeated characters for moderation inspection or speech safety.
    /// </summary>
    public static string CollapseRepeats(string input, bool forInspection = false)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return ConsecutiveRepeatRegex().Replace(input, forInspection ? "$1" : "$1$1");
    }

    /// <summary>
    /// Collapses spaced characters (e.g. "f u c k" -> "fuck") for moderation inspection.
    /// </summary>
    public static string CollapseSpacedLetters(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return SpacedLettersRegex().Replace(input, "");
    }

    /// <summary>
    /// Performs full multi-step deobfuscation for security and moderation inspection.
    /// Result is strictly for checking against blocklists and patterns, not for TTS output.
    /// </summary>
    public static string NormalizeForInspection(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        // 1. Strip invisible / zero-width characters
        string stripped = StripInvisibleCharacters(input);

        // 2. Remove combining diacritics / Zalgo
        string noDiacritics = RemoveDiacritics(stripped);

        // 3. Homoglyph / Confusable mapping (Cyrillic, Greek, fullwidth, leetspeak)
        string homoglyphNormalized = HomoglyphMap.NormalizeString(noDiacritics);

        // 4. Lowercase
        string lower = homoglyphNormalized.ToLowerInvariant();

        // 5. Collapse spaced letters ("f u c k" -> "fuck")
        string collapsedSpaces = CollapseSpacedLetters(lower);

        // 6. Collapse excessive repeated characters ("fuuuuuck" -> "fuck", "kyyyyyys" -> "kys")
        string collapsedRepeats = CollapseRepeats(collapsedSpaces, forInspection: true);

        // 7. Clean up whitespace
        return MultipleWhitespaceRegex().Replace(collapsedRepeats, " ").Trim();
    }

    /// <summary>
    /// Cleans text for safe, intelligible TTS playback.
    /// Preserves original wording but removes invisible spam, URLs, and excessive whitespace.
    /// </summary>
    public static string CleanForSpeech(string input, bool stripUrls = true)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        string text = StripInvisibleCharacters(input);
        if (stripUrls)
        {
            text = StripUrls(text);
        }

        // Limit consecutive repeated characters to 2 so TTS doesn't stutter or crash
        text = CollapseRepeats(text);

        // Clean extra whitespace
        text = MultipleWhitespaceRegex().Replace(text, " ").Trim();

        return text;
    }
}
