using System.Globalization;

namespace SafeSpeak.Core.Moderation;

public enum ScriptType
{
    Latin,
    Cyrillic,
    Greek,
    Arabic,
    Hebrew,
    Devanagari,
    Cjk,
    OtherNonLatin,
    EmojiOrSymbol,
    Common, // Numbers, punctuation, whitespace, standard currency/math
    Unknown
}

/// <summary>
/// Detects character scripts, identifies mixed-script tokens (spoofing attacks), and enforces language script policies.
/// </summary>
public static class ScriptValidator
{
    private static readonly char[] WordDelimiters =
    [
        ' ', '\t', '\r', '\n', '-', '_', '.', ',', ';', ':', '/', '\\',
        '!', '?', '(', ')', '[', ']', '{', '}', '"', '\''
    ];

    private static readonly (int Min, int Max, ScriptType Type)[] ScriptRanges =
    [
        // 6. CJK
        (0x3000, 0x9FFF, ScriptType.Cjk),
        (0xAC00, 0xD7AF, ScriptType.Cjk),
        (0x1100, 0x11FF, ScriptType.Cjk),
        (0x2E80, 0x2FFF, ScriptType.Cjk),
        (0xA960, 0xA97F, ScriptType.Cjk),
        (0xD7B0, 0xD7FF, ScriptType.Cjk),
        (0xF900, 0xFAFF, ScriptType.Cjk),
        (0xFE30, 0xFE4F, ScriptType.Cjk),
        (0xFF00, 0xFFEF, ScriptType.Cjk),
        (0x1F200, 0x1F2FF, ScriptType.Cjk),
        (0x20000, 0x323AF, ScriptType.Cjk),

        // 7. Cyrillic
        (0x0400, 0x052F, ScriptType.Cyrillic),
        (0x2DE0, 0x2DFF, ScriptType.Cyrillic),
        (0xA640, 0xA69F, ScriptType.Cyrillic),
        (0x1C80, 0x1C8F, ScriptType.Cyrillic),

        // 8. Greek
        (0x0370, 0x03FF, ScriptType.Greek),
        (0x1F00, 0x1FFF, ScriptType.Greek),

        // 9. Arabic
        (0x0600, 0x06FF, ScriptType.Arabic),
        (0x0750, 0x077F, ScriptType.Arabic),
        (0x08A0, 0x08FF, ScriptType.Arabic),
        (0xFB50, 0xFDFF, ScriptType.Arabic),
        (0xFE70, 0xFEFF, ScriptType.Arabic),
        (0x10E60, 0x10E7F, ScriptType.Arabic),

        // 10. Hebrew
        (0x0590, 0x05FF, ScriptType.Hebrew),
        (0xFB1D, 0xFB4F, ScriptType.Hebrew),

        // 11. Devanagari
        (0x0900, 0x097F, ScriptType.Devanagari),
        (0xA8E0, 0xA8FF, ScriptType.Devanagari),

        // 12. Other Non-Latin Scripts
        (0x0980, 0x0DFF, ScriptType.OtherNonLatin),
        (0x0E00, 0x0E7F, ScriptType.OtherNonLatin),
        (0x0E80, 0x0EFF, ScriptType.OtherNonLatin),
        (0x0F00, 0x0FFF, ScriptType.OtherNonLatin),
        (0x1000, 0x109F, ScriptType.OtherNonLatin),
        (0xA9E0, 0xA9FF, ScriptType.OtherNonLatin),
        (0x10A0, 0x10FF, ScriptType.OtherNonLatin),
        (0x2D00, 0x2D2F, ScriptType.OtherNonLatin),
        (0x0530, 0x058F, ScriptType.OtherNonLatin),
        (0x1200, 0x139F, ScriptType.OtherNonLatin),
        (0x2D80, 0x2DDF, ScriptType.OtherNonLatin),
        (0xAB00, 0xAB2F, ScriptType.OtherNonLatin),
        (0x13A0, 0x13FF, ScriptType.OtherNonLatin),
        (0xAB70, 0xABBF, ScriptType.OtherNonLatin),
        (0x1400, 0x167F, ScriptType.OtherNonLatin),
        (0x1680, 0x16FF, ScriptType.OtherNonLatin),
        (0x1700, 0x177F, ScriptType.OtherNonLatin),
        (0x1780, 0x17FF, ScriptType.OtherNonLatin),
        (0x19E0, 0x19FF, ScriptType.OtherNonLatin),
        (0x1800, 0x18AF, ScriptType.OtherNonLatin),
        (0x0700, 0x074F, ScriptType.OtherNonLatin),
        (0x0780, 0x07BF, ScriptType.OtherNonLatin),
        (0x07C0, 0x07FF, ScriptType.OtherNonLatin),

        // 13. Currency
        (0x20A0, 0x20CF, ScriptType.Common),

        // 14. Emojis and Pictographic Symbols
        (0x2190, 0x21FF, ScriptType.EmojiOrSymbol),
        (0x2200, 0x22FF, ScriptType.EmojiOrSymbol),
        (0x2300, 0x23FF, ScriptType.EmojiOrSymbol),
        (0x25A0, 0x25FF, ScriptType.EmojiOrSymbol),
        (0x2600, 0x27BF, ScriptType.EmojiOrSymbol),
        (0x2900, 0x2BFF, ScriptType.EmojiOrSymbol),
        (0xFE00, 0xFE0F, ScriptType.EmojiOrSymbol),
        (0x1F000, 0x1F02F, ScriptType.EmojiOrSymbol),
        (0x1F0A0, 0x1F0FF, ScriptType.EmojiOrSymbol),
        (0x1F1E6, 0x1F1FF, ScriptType.EmojiOrSymbol),
        (0x1F300, 0x1F5FF, ScriptType.EmojiOrSymbol),
        (0x1F600, 0x1F64F, ScriptType.EmojiOrSymbol),
        (0x1F680, 0x1F6FF, ScriptType.EmojiOrSymbol),
        (0x1F900, 0x1F9FF, ScriptType.EmojiOrSymbol),
        (0x1FA70, 0x1FAFF, ScriptType.EmojiOrSymbol),
        (0xE0000, 0xE007F, ScriptType.EmojiOrSymbol),
        (0xE0100, 0xE01EF, ScriptType.EmojiOrSymbol),
    ];

    public static ScriptType GetScriptType(char c) => GetScriptType((int)c);

    public static ScriptType GetScriptType(int codePoint)
    {
        if (codePoint is < 0 or > 0x10FFFF || codePoint is >= 0xD800 and <= 0xDFFF)
        {
            return ScriptType.Unknown;
        }

        // 1. Basic ASCII controls and whitespace
        if (codePoint is >= 0x0000 and <= 0x0020 or 0x007F)
        {
            return ScriptType.Common;
        }

        // 2. Basic Latin Alphabet (English A-Z, a-z)
        if (codePoint is >= 0x0041 and <= 0x005A or >= 0x0061 and <= 0x007A)
        {
            return ScriptType.Latin;
        }

        // 3. ASCII Numbers and Standard ASCII Punctuation/Symbols
        if (codePoint is >= 0x0021 and <= 0x0040 or >= 0x005B and <= 0x0060 or >= 0x007B and <= 0x007E)
        {
            return ScriptType.Common;
        }

        // 4. Extended Latin (Latin-1 Supplement, Latin Extended A/B/C/D/E, IPA, Latin Additional)
        if (codePoint is >= 0x00C0 and <= 0x02AF or
            >= 0x1E00 and <= 0x1EFF or
            >= 0x2C60 and <= 0x2C7F or
            >= 0xA720 and <= 0xA7FF or
            >= 0xAB30 and <= 0xAB6F)
        {
            return ScriptType.Latin;
        }

        // 5. Latin-1 common punctuation and symbols (e.g. ¡, ¿, «, », ©, ®, °, ±, £, ¥, §, etc.)
        if (codePoint is >= 0x00A0 and <= 0x00BF)
        {
            return ScriptType.Common;
        }

        // U+200D is the one formatting character allowed because it joins emoji
        // into a single displayed sequence (for example, family emoji).
        if (codePoint == 0x200D)
        {
            return ScriptType.EmojiOrSymbol;
        }

        // 13. General punctuation and currency. Formatting controls within the
        // punctuation block are deliberately rejected; they can alter visual order
        // and are not ordinary punctuation.
        if (codePoint is >= 0x2000 and <= 0x206F)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory((char)codePoint);
            return category == UnicodeCategory.Format
                ? ScriptType.Unknown
                : ScriptType.Common;
        }

        foreach (var range in ScriptRanges)
        {
            if (codePoint >= range.Min && codePoint <= range.Max)
            {
                return range.Type;
            }
        }

        // 15. Standard Unicode categories as fallback
        if (codePoint <= 0xFFFF)
        {
            char c = (char)codePoint;
            if (char.IsWhiteSpace(c) || char.IsDigit(c)) return ScriptType.Common;
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or
                UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or
                UnicodeCategory.OtherLetter)
            {
                return ScriptType.OtherNonLatin;
            }
            if (cat is UnicodeCategory.DecimalDigitNumber or UnicodeCategory.SpaceSeparator or
                UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or
                UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation or
                UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation or
                UnicodeCategory.OtherPunctuation or UnicodeCategory.DashPunctuation or
                UnicodeCategory.ConnectorPunctuation)
            {
                return ScriptType.Common;
            }
            if (cat is UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol or UnicodeCategory.ModifierSymbol)
            {
                return ScriptType.Common;
            }
        }

        return ScriptType.Unknown;
    }

    /// <summary>
    /// Checks if a single word/token contains mixed writing systems (e.g. Cyrillic + Latin in the same word).
    /// </summary>
    public static bool HasMixedScriptsInWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word) || word.Length <= 1) return false;

        ScriptType primaryScript = ScriptType.Common;

        for (int i = 0; i < word.Length; i++)
        {
            int codePoint;
            if (char.IsHighSurrogate(word[i]) && i + 1 < word.Length && char.IsLowSurrogate(word[i + 1]))
            {
                codePoint = char.ConvertToUtf32(word, i);
                i++;
            }
            else
            {
                codePoint = word[i];
            }

            var script = GetScriptType(codePoint);
            if (script == ScriptType.Common || script == ScriptType.EmojiOrSymbol)
            {
                continue;
            }

            if (primaryScript == ScriptType.Common)
            {
                primaryScript = script;
            }
            else if (script != primaryScript)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if any word in the text mixes multiple scripts (e.g. Cyrillic spoofing).
    /// </summary>
    public static bool ContainsMixedScriptWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var words = text.Split(WordDelimiters, StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            if (HasMixedScriptsInWord(word))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if the text consists strictly of Latin characters, numbers, common punctuation/symbols, and emojis.
    /// Rejects non-Latin writing systems (CJK, Cyrillic, Greek, Arabic, Hebrew, Devanagari, Thai, etc.).
    /// </summary>
    public static bool IsLatinOrEmojiOnly(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;

        for (int i = 0; i < text.Length; i++)
        {
            int codePoint;
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codePoint = char.ConvertToUtf32(text, i);
                i++;
            }
            else
            {
                codePoint = text[i];
            }

            var script = GetScriptType(codePoint);
            if (script is not (ScriptType.Latin or ScriptType.Common or ScriptType.EmojiOrSymbol))
            {
                return false;
            }
        }

        return true;
    }
}
