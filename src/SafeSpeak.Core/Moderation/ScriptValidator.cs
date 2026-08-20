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
    EmojiOrSymbol,
    Common, // Numbers, punctuation, whitespace
    Unknown
}

/// <summary>
/// Detects character scripts, identifies mixed-script tokens (spoofing attacks), and enforces language script policies.
/// </summary>
public static class ScriptValidator
{
    public static ScriptType GetScriptType(char c)
    {
        if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsDigit(c) || char.IsSeparator(c) || char.IsSymbol(c))
        {
            return ScriptType.Common;
        }

        if (char.IsSurrogate(c))
        {
            return ScriptType.EmojiOrSymbol;
        }

        var cat = CharUnicodeInfo.GetUnicodeCategory(c);
        if (cat == UnicodeCategory.OtherSymbol || cat == UnicodeCategory.MathSymbol ||
            cat == UnicodeCategory.ModifierSymbol || cat == UnicodeCategory.Surrogate ||
            cat == UnicodeCategory.Format || cat == UnicodeCategory.CurrencySymbol)
        {
            return ScriptType.EmojiOrSymbol;
        }

        int codePoint = (int)c;

        if ((codePoint >= 0x0041 && codePoint <= 0x005A) ||
            (codePoint >= 0x0061 && codePoint <= 0x007A) ||
            (codePoint >= 0x00C0 && codePoint <= 0x024F))
        {
            return ScriptType.Latin;
        }

        if (codePoint >= 0x0400 && codePoint <= 0x04FF)
        {
            return ScriptType.Cyrillic;
        }

        if (codePoint >= 0x0370 && codePoint <= 0x03FF)
        {
            return ScriptType.Greek;
        }

        if (codePoint >= 0x0600 && codePoint <= 0x06FF)
        {
            return ScriptType.Arabic;
        }

        if (codePoint >= 0x0590 && codePoint <= 0x05FF)
        {
            return ScriptType.Hebrew;
        }

        if (codePoint >= 0x0900 && codePoint <= 0x097F)
        {
            return ScriptType.Devanagari;
        }

        if ((codePoint >= 0x4E00 && codePoint <= 0x9FFF) ||
            (codePoint >= 0x3040 && codePoint <= 0x309F) ||
            (codePoint >= 0x30A0 && codePoint <= 0x30FF) ||
            (codePoint >= 0xAC00 && codePoint <= 0xD7AF))
        {
            return ScriptType.Cjk;
        }

        // Symbols, Dingbats, Arrows, Box drawing (0x2000 - 0x2BFF)
        if (codePoint >= 0x2000 && codePoint <= 0x2BFF)
        {
            return ScriptType.EmojiOrSymbol;
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

        foreach (char c in word)
        {
            var script = GetScriptType(c);
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
                // Mixed script detected inside a single token
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

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
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
    /// Checks if the text consists primarily of Latin characters, numbers, and emojis (when English-only is enabled).
    /// Rejects foreign writing systems (Cyrillic, Greek, Arabic, Hebrew, Devanagari, CJK).
    /// </summary>
    public static bool IsLatinOrEmojiOnly(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;

        foreach (char c in text)
        {
            var script = GetScriptType(c);
            if (script is ScriptType.Cyrillic or
                ScriptType.Greek or
                ScriptType.Arabic or
                ScriptType.Hebrew or
                ScriptType.Devanagari or
                ScriptType.Cjk)
            {
                return false;
            }
        }

        return true;
    }
}
