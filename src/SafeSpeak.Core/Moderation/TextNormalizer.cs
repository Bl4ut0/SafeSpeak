using System.Globalization;
using System.Text;

namespace SafeSpeak.Core.Moderation;

public static class TextNormalizer
{
    private static readonly IReadOnlyDictionary<int, char> ConfusableMap = new Dictionary<int, char>
    {
        // Cyrillic lookalikes
        [0x0430] = 'a',
        [0x0410] = 'a',
        [0x0435] = 'e',
        [0x0415] = 'e',
        [0x043E] = 'o',
        [0x041E] = 'o',
        [0x0440] = 'p',
        [0x0420] = 'p',
        [0x0441] = 'c',
        [0x0421] = 'c',
        [0x0445] = 'x',
        [0x0425] = 'x',
        [0x0443] = 'y',
        [0x0423] = 'y',
        [0x0456] = 'i',
        [0x0406] = 'i',
        [0x0458] = 'j',
        [0x0408] = 'j',
        [0x043A] = 'k',
        [0x041A] = 'k',
        [0x043C] = 'm',
        [0x041C] = 'm',
        [0x0442] = 't',
        [0x0422] = 't',
        [0x0432] = 'b',
        [0x0412] = 'b',
        [0x043D] = 'h',
        [0x041D] = 'h',
        // Greek lookalikes
        [0x03B1] = 'a',
        [0x0391] = 'a',
        [0x03B5] = 'e',
        [0x0395] = 'e',
        [0x03BF] = 'o',
        [0x039F] = 'o',
        [0x03C1] = 'p',
        [0x03A1] = 'p',
        [0x03C7] = 'x',
        [0x03A7] = 'x',
        [0x03B9] = 'i',
        [0x0399] = 'i',
        [0x03BA] = 'k',
        [0x039A] = 'k',
        [0x03BC] = 'm',
        [0x039C] = 'm',
        [0x03C4] = 't',
        [0x03A4] = 't',
        [0x03C5] = 'y',
        [0x03A5] = 'y',
    };

    private static readonly IReadOnlyDictionary<char, char> LeetMap = new Dictionary<char, char>
    {
        ['0'] = 'o',
        ['1'] = 'i',
        ['3'] = 'e',
        ['4'] = 'a',
        ['5'] = 's',
        ['7'] = 't',
        ['8'] = 'b',
        ['9'] = 'g',
    };

    public static TextNormalizationResult Normalize(string? value, int maximumRepeatedCharacters = 2)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new(string.Empty, string.Empty, string.Empty, new HashSet<UnicodeScript>(), false);
        }

        string compatibilityNormalized = value.Normalize(NormalizationForm.FormKC);
        var cleanBuilder = new StringBuilder(compatibilityNormalized.Length);
        var scripts = new HashSet<UnicodeScript>();
        bool hadInvisible = false;
        bool pendingSpace = false;

        foreach (Rune rune in compatibilityNormalized.EnumerateRunes())
        {
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Format or UnicodeCategory.Control or UnicodeCategory.Surrogate)
            {
                if (Rune.IsWhiteSpace(rune))
                {
                    pendingSpace = cleanBuilder.Length > 0;
                }
                else
                {
                    hadInvisible = true;
                }

                continue;
            }

            if (Rune.IsWhiteSpace(rune) || category is UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            {
                pendingSpace = cleanBuilder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                cleanBuilder.Append(' ');
                pendingSpace = false;
            }

            cleanBuilder.Append(rune.ToString());
            if (Rune.IsLetter(rune))
            {
                scripts.Add(GetScript(rune.Value));
            }
        }

        string clean = CollapseRepeatedCharacters(cleanBuilder.ToString().Trim(), maximumRepeatedCharacters);
        string comparison = BuildComparison(clean);
        string compact = new(comparison.Where(char.IsLetterOrDigit).ToArray());

        return new(clean, comparison, compact, scripts, hadInvisible);
    }

    private static string BuildComparison(string value)
    {
        string decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (Rune rune in decomposed.EnumerateRunes())
        {
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            if (ConfusableMap.TryGetValue(rune.Value, out char mapped))
            {
                builder.Append(mapped);
                continue;
            }

            string lowered = rune.ToString().ToLowerInvariant();
            foreach (char character in lowered)
            {
                builder.Append(LeetMap.TryGetValue(character, out char replacement) ? replacement : character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string CollapseRepeatedCharacters(string value, int maximumRepeatedCharacters)
    {
        if (value.Length == 0 || maximumRepeatedCharacters < 1)
        {
            return value;
        }

        var result = new StringBuilder(value.Length);
        Rune? previous = null;
        int count = 0;

        foreach (Rune rune in value.EnumerateRunes())
        {
            if (previous is Rune prior && prior == rune)
            {
                count++;
            }
            else
            {
                previous = rune;
                count = 1;
            }

            if (count <= maximumRepeatedCharacters)
            {
                result.Append(rune.ToString());
            }
        }

        return result.ToString();
    }

    private static UnicodeScript GetScript(int codePoint) => codePoint switch
    {
        >= 0x0041 and <= 0x024F or >= 0x1E00 and <= 0x1EFF => UnicodeScript.Latin,
        >= 0x0370 and <= 0x03FF or >= 0x1F00 and <= 0x1FFF => UnicodeScript.Greek,
        >= 0x0400 and <= 0x052F => UnicodeScript.Cyrillic,
        >= 0x0590 and <= 0x05FF => UnicodeScript.Hebrew,
        >= 0x0600 and <= 0x06FF or >= 0x0750 and <= 0x077F => UnicodeScript.Arabic,
        >= 0x0900 and <= 0x097F => UnicodeScript.Devanagari,
        >= 0x0E00 and <= 0x0E7F => UnicodeScript.Thai,
        >= 0x3040 and <= 0x309F => UnicodeScript.Hiragana,
        >= 0x30A0 and <= 0x30FF => UnicodeScript.Katakana,
        >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF => UnicodeScript.Han,
        >= 0xAC00 and <= 0xD7AF => UnicodeScript.Hangul,
        _ => UnicodeScript.Other,
    };
}
