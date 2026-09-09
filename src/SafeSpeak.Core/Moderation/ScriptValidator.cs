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

        // 6. CJK (Chinese, Japanese, Korean)
        // Including CJK Symbols & Punctuation (0x3000-0x303F), Hiragana (0x3040-0x309F),
        // Katakana (0x30A0-0x30FF), Bopomofo (0x3100-0x312F), Hangul Compatibility (0x3130-0x318F),
        // Kanbun (0x3190-0x319F), Bopomofo Ext (0x31A0-0x31BF), CJK Strokes (0x31C0-0x31EF),
        // Katakana Phonetic (0x31F0-0x31FF), Enclosed CJK (0x3200-0x32FF),
        // CJK Compatibility / Squared Words (0x3300-0x33FF) -> includes ㌕ (0x3315) and ㌖ (0x3316),
        // CJK Ext A (0x3400-0x4DBF), CJK Unified Ideographs (0x4E00-0x9FFF),
        // Hangul Syllables (0xAC00-0xD7AF), CJK Compatibility Ideographs/Forms (0xF900-0xFAFF, 0xFE30-0xFE4F),
        // Halfwidth Katakana (0xFF65-0xFF9F), Fullwidth CJK / Ideographic (0xFF01-0xFFEE),
        // Hangul Jamo (0x1100-0x11FF, 0xA960-0xA97F, 0xD7B0-0xD7FF), CJK Radicals (0x2E80-0x2FFF),
        // Enclosed Ideographic Supplement (0x1F200-0x1F2FF), CJK Ext B..I (0x20000-0x323AF)
        if (codePoint is >= 0x3000 and <= 0x9FFF or
            >= 0xAC00 and <= 0xD7AF or
            >= 0x1100 and <= 0x11FF or
            >= 0x2E80 and <= 0x2FFF or
            >= 0xA960 and <= 0xA97F or
            >= 0xD7B0 and <= 0xD7FF or
            >= 0xF900 and <= 0xFAFF or
            >= 0xFE30 and <= 0xFE4F or
            >= 0xFF00 and <= 0xFFEF or
            >= 0x1F200 and <= 0x1F2FF or
            >= 0x20000 and <= 0x323AF)
        {
            return ScriptType.Cjk;
        }

        // 7. Cyrillic
        if (codePoint is >= 0x0400 and <= 0x052F or
            >= 0x2DE0 and <= 0x2DFF or
            >= 0xA640 and <= 0xA69F or
            >= 0x1C80 and <= 0x1C8F)
        {
            return ScriptType.Cyrillic;
        }

        // 8. Greek
        if (codePoint is >= 0x0370 and <= 0x03FF or
            >= 0x1F00 and <= 0x1FFF)
        {
            return ScriptType.Greek;
        }

        // 9. Arabic
        if (codePoint is >= 0x0600 and <= 0x06FF or
            >= 0x0750 and <= 0x077F or
            >= 0x08A0 and <= 0x08FF or
            >= 0xFB50 and <= 0xFDFF or
            >= 0xFE70 and <= 0xFEFF or
            >= 0x10E60 and <= 0x10E7F)
        {
            return ScriptType.Arabic;
        }

        // 10. Hebrew
        if (codePoint is >= 0x0590 and <= 0x05FF or
            >= 0xFB1D and <= 0xFB4F)
        {
            return ScriptType.Hebrew;
        }

        // 11. Devanagari
        if (codePoint is >= 0x0900 and <= 0x097F or
            >= 0xA8E0 and <= 0xA8FF)
        {
            return ScriptType.Devanagari;
        }

        // 12. Other Non-Latin Scripts (Indic, Southeast Asian, African, Middle Eastern, etc.)
        if (codePoint is >= 0x0980 and <= 0x0DFF or // Bengali, Gurmukhi, Gujarati, Oriya, Tamil, Telugu, Kannada, Malayalam, Sinhala
            >= 0x0E00 and <= 0x0E7F or // Thai
            >= 0x0E80 and <= 0x0EFF or // Lao
            >= 0x0F00 and <= 0x0FFF or // Tibetan
            >= 0x1000 and <= 0x109F or >= 0xA9E0 and <= 0xA9FF or // Myanmar
            >= 0x10A0 and <= 0x10FF or >= 0x2D00 and <= 0x2D2F or // Georgian
            >= 0x0530 and <= 0x058F or // Armenian
            >= 0x1200 and <= 0x139F or >= 0x2D80 and <= 0x2DDF or >= 0xAB00 and <= 0xAB2F or // Ethiopic
            >= 0x13A0 and <= 0x13FF or >= 0xAB70 and <= 0xABBF or // Cherokee
            >= 0x1400 and <= 0x167F or // Canadian Aboriginal
            >= 0x1680 and <= 0x16FF or // Ogham / Runic
            >= 0x1700 and <= 0x177F or // Philippine scripts
            >= 0x1780 and <= 0x17FF or >= 0x19E0 and <= 0x19FF or // Khmer
            >= 0x1800 and <= 0x18AF or // Mongolian
            >= 0x0700 and <= 0x074F or // Syriac
            >= 0x0780 and <= 0x07BF or // Thaana
            >= 0x07C0 and <= 0x07FF)   // NKo
        {
            return ScriptType.OtherNonLatin;
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

        if (codePoint is >= 0x20A0 and <= 0x20CF)
        {
            return ScriptType.Common;
        }

        // 14. Emojis and Pictographic Symbols:
        if (codePoint is >= 0x2190 and <= 0x21FF or // Arrows
            >= 0x2200 and <= 0x22FF or // Mathematical Operators
            >= 0x2300 and <= 0x23FF or // Miscellaneous Technical
            >= 0x25A0 and <= 0x25FF or // Geometric Shapes
            >= 0x2600 and <= 0x27BF or // Misc Symbols & Dingbats
            >= 0x2900 and <= 0x2BFF or // Supplemental Arrows & Misc Symbols
            >= 0xFE00 and <= 0xFE0F or // Variation Selectors
            >= 0x1F000 and <= 0x1F02F or // Mahjong
            >= 0x1F0A0 and <= 0x1F0FF or // Playing Cards
            >= 0x1F1E6 and <= 0x1F1FF or // Regional Indicator Symbols (Flags)
            >= 0x1F300 and <= 0x1F5FF or // Misc Symbols and Pictographs
            >= 0x1F600 and <= 0x1F64F or // Emoticons
            >= 0x1F680 and <= 0x1F6FF or // Transport and Map
            >= 0x1F900 and <= 0x1F9FF or // Supplemental Symbols and Pictographs
            >= 0x1FA70 and <= 0x1FAFF or // Symbols and Pictographs Extended-A
            >= 0xE0100 and <= 0xE01EF)   // Variation Selectors Supplement
        {
            return ScriptType.EmojiOrSymbol;
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
