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
    /// Checks if a grapheme cluster (text element) represents an emoji, pictogram, or symbol.
    /// </summary>
    public static bool IsEmoji(string textElement)
    {
        if (string.IsNullOrEmpty(textElement)) return false;

        // Fast check for common ASCII non-emojis
        if (textElement.Length == 1 && textElement[0] < 0x2000)
        {
            return false;
        }

        foreach (Rune rune in textElement.EnumerateRunes())
        {
            int cp = rune.Value;
            if (cp is (>= 0x1F300 and <= 0x1F5FF) or // Misc Symbols and Pictographs
                      (>= 0x1F600 and <= 0x1F64F) or // Emoticons
                      (>= 0x1F680 and <= 0x1F6FF) or // Transport and Map
                      (>= 0x1F900 and <= 0x1F9FF) or // Supplemental Symbols and Pictographs
                      (>= 0x1FA70 and <= 0x1FAFF) or // Symbols and Pictographs Extended-A
                      (>= 0x1F1E6 and <= 0x1F1FF) or // Regional Indicator Symbols (Flags)
                      (>= 0x2600 and <= 0x27BF) or   // Misc Symbols & Dingbats (❤️, ☀️, etc.)
                      (>= 0x2300 and <= 0x23FF) or   // Miscellaneous Technical (⌚, ⌛, etc.)
                      (>= 0x2B00 and <= 0x2BFF) or   // Miscellaneous Symbols and Arrows (⭐)
                      (>= 0x1F000 and <= 0x1F0FF) or // Mahjong, Cards, Alphanumerics
                      0x20E3)                         // Combining Enclosing Keycap (e.g. 1️⃣)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if a grapheme cluster (text element) represents a flag emoji or sequence
    /// (national flags, subdivision flags, rainbow/trans/pirate flags, etc.).
    /// </summary>
    public static bool IsFlag(string textElement)
    {
        if (string.IsNullOrEmpty(textElement)) return false;

        foreach (Rune rune in textElement.EnumerateRunes())
        {
            int cp = rune.Value;
            if (cp is (>= 0x1F1E6 and <= 0x1F1FF) or // Regional Indicator Symbols (country/territory flags)
                      (>= 0xE0000 and <= 0xE007F) or // Unicode Tags (subdivision flags like Scotland/England/Wales)
                      0x1F3F4 or                      // Waving Black Flag (base for pirate and subdivision flags)
                      0x1F3F3 or                      // Waving White Flag (base for rainbow and trans flags)
                      0x1F6A9 or                      // Triangular Flag on Post
                      0x1F38C or                      // Crossed Flags
                      0x1F3C1)                        // Chequered Flag
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Removes flag emojis and sequences from text while preserving all speakable words and other decorations.
    /// </summary>
    public static string StripFlags(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var sb = new StringBuilder(input.Length);
        var enumerator = StringInfo.GetTextElementEnumerator(input);
        while (enumerator.MoveNext())
        {
            string element = enumerator.GetTextElement();
            if (!IsFlag(element))
            {
                sb.Append(element);
            }
        }

        return MultipleWhitespaceRegex().Replace(sb.ToString(), " ").Trim();
    }

    /// <summary>
    /// Collapses consecutive repeated emojis down to maxRepeated and limits total emojis per message to maxTotal.
    /// Prevents repeated emoji spam (e.g. 😂😂😂😂😂 or 🔥 🔥 🔥) from causing TTS to read each emoji aloud.
    /// </summary>
    public static string CollapseRepeatedEmojis(
        string input,
        int maxRepeated = 1,
        int maxTotal = 3)
    {
        if (string.IsNullOrWhiteSpace(input)) return input ?? string.Empty;
        if (maxRepeated < 1) maxRepeated = 1;

        var result = new StringBuilder(input.Length);
        string? lastEmoji = null;
        int consecutiveEmojiCount = 0;
        int totalEmojiCount = 0;
        bool spacePending = false;

        var enumerator = StringInfo.GetTextElementEnumerator(input);
        while (enumerator.MoveNext())
        {
            string element = enumerator.GetTextElement();

            if (string.IsNullOrWhiteSpace(element))
            {
                if (result.Length > 0)
                {
                    spacePending = true;
                }
                continue;
            }

            if (IsEmoji(element))
            {
                if (string.Equals(lastEmoji, element, StringComparison.Ordinal))
                {
                    consecutiveEmojiCount++;
                }
                else
                {
                    lastEmoji = element;
                    consecutiveEmojiCount = 1;
                }

                if (consecutiveEmojiCount <= maxRepeated &&
                    (maxTotal < 0 || totalEmojiCount < maxTotal))
                {
                    if (spacePending && result.Length > 0 && result[^1] != ' ')
                    {
                        result.Append(' ');
                    }

                    result.Append(element);
                    totalEmojiCount++;
                    spacePending = false;
                }
            }
            else
            {
                // Non-emoji text resets consecutive emoji repeat tracking
                lastEmoji = null;
                consecutiveEmojiCount = 0;

                if (spacePending && result.Length > 0 && result[^1] != ' ')
                {
                    result.Append(' ');
                }

                result.Append(element);
                spacePending = false;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Cleans text for safe, intelligible TTS playback.
    /// Preserves original wording but removes invisible spam, URLs, and excessive whitespace,
    /// and collapses repeated emoji spam so TTS does not read out every emoji repeatedly.
    /// </summary>
    public static string CleanForSpeech(
        string input,
        bool stripUrls = true,
        int maxRepeatedEmojis = 1,
        int maxTotalEmojis = 3)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        string text = StripInvisibleCharacters(input);
        if (stripUrls)
        {
            text = StripUrls(text);
        }

        // Limit repeated emojis and total emojis per message
        text = CollapseRepeatedEmojis(text, maxRepeatedEmojis, maxTotalEmojis);

        // Limit consecutive repeated characters to 2 so TTS doesn't stutter or crash
        text = CollapseRepeats(text);

        // Clean extra whitespace
        text = MultipleWhitespaceRegex().Replace(text, " ").Trim();

        return text;
    }

    /// <summary>
    /// Produces a display name that an English TTS voice can pronounce reliably.
    /// Compatibility-styled Latin letters are converted to their ordinary forms,
    /// decorative symbols and emoji are removed, and visual separators become spaces.
    /// </summary>
    public static string CleanDisplayNameForSpeech(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        string withoutFlags = StripFlags(input);
        if (string.IsNullOrWhiteSpace(withoutFlags)) return string.Empty;

        string normalized = StripInvisibleCharacters(withoutFlags)
            .Normalize(NormalizationForm.FormKC);
        var result = new StringBuilder(normalized.Length);
        bool separatorPending = false;

        for (int index = 0; index < normalized.Length; index++)
        {
            int codePoint;
            string character;
            if (char.IsHighSurrogate(normalized[index]) &&
                index + 1 < normalized.Length &&
                char.IsLowSurrogate(normalized[index + 1]))
            {
                codePoint = char.ConvertToUtf32(normalized, index);
                character = normalized.Substring(index, 2);
                index++;
            }
            else
            {
                codePoint = normalized[index];
                character = normalized[index].ToString();
            }

            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(
                char.ConvertFromUtf32(codePoint),
                0);
            bool isSpeakable = ScriptValidator.GetScriptType(codePoint) == ScriptType.Latin ||
                category == UnicodeCategory.DecimalDigitNumber;
            if (isSpeakable)
            {
                if (separatorPending && result.Length > 0 && result[^1] != ' ')
                {
                    result.Append(' ');
                }

                result.Append(character);
                separatorPending = false;
                continue;
            }

            if (codePoint is '\'' or 0x2019 or '-')
            {
                if (result.Length > 0 && result[^1] != ' ')
                {
                    result.Append(codePoint == 0x2019 ? '\'' : (char)codePoint);
                }

                separatorPending = false;
                continue;
            }

            // Symbols, emoji, punctuation, and whitespace separate adjacent name
            // fragments so removing decoration cannot accidentally join words.
            separatorPending = result.Length > 0;
        }

        string cleaned = MultipleWhitespaceRegex()
            .Replace(result.ToString(), " ")
            .Trim(' ', '\'', '-');
        return CollapseRepeats(cleaned);
    }
}
