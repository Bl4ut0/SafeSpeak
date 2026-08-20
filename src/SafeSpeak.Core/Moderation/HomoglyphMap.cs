using System.Collections.Frozen;

namespace SafeSpeak.Core.Moderation;

/// <summary>
/// Comprehensive mapping of Unicode confusables, Cyrillic/Greek lookalikes, fullwidth chars, and leetspeak substitutions to ASCII Latin equivalents.
/// </summary>
public static class HomoglyphMap
{
    private static readonly Dictionary<char, char> RawMap = new()
    {
        // Cyrillic Lowercase
        { '\u0430', 'a' }, // а
        { '\u0432', 'b' }, // в
        { '\u0435', 'e' }, // е
        { '\u0451', 'e' }, // ё
        { '\u043A', 'k' }, // к
        { '\u043C', 'm' }, // м
        { '\u043D', 'h' }, // н
        { '\u043E', 'o' }, // о
        { '\u0440', 'p' }, // р
        { '\u0441', 'c' }, // с
        { '\u0442', 't' }, // т
        { '\u0443', 'y' }, // у
        { '\u0445', 'x' }, // х
        { '\u0456', 'i' }, // і
        { '\u0458', 'j' }, // ј
        { '\u0455', 's' }, // ѕ

        // Cyrillic Uppercase
        { '\u0410', 'A' }, // А
        { '\u0412', 'B' }, // В
        { '\u0415', 'E' }, // Е
        { '\u0401', 'E' }, // Ё
        { '\u041A', 'K' }, // К
        { '\u041C', 'M' }, // М
        { '\u041D', 'H' }, // Н
        { '\u041E', 'O' }, // О
        { '\u0420', 'P' }, // Р
        { '\u0421', 'C' }, // С
        { '\u0422', 'T' }, // Т
        { '\u0423', 'Y' }, // У
        { '\u0425', 'X' }, // Х
        { '\u0406', 'I' }, // І
        { '\u0408', 'J' }, // Ј
        { '\u0405', 'S' }, // Ѕ

        // Greek Lowercase
        { '\u03B1', 'a' }, // α
        { '\u03B2', 'b' }, // β
        { '\u03B3', 'y' }, // γ
        { '\u03B5', 'e' }, // ε
        { '\u03B7', 'n' }, // η
        { '\u03B9', 'i' }, // ι
        { '\u03BA', 'k' }, // κ
        { '\u03BD', 'v' }, // ν
        { '\u03BF', 'o' }, // ο
        { '\u03C1', 'p' }, // ρ
        { '\u03C2', 's' }, // ς
        { '\u03C3', 's' }, // σ
        { '\u03C4', 't' }, // τ
        { '\u03C5', 'u' }, // υ
        { '\u03C7', 'x' }, // χ
        { '\u03C9', 'w' }, // ω

        // Greek Uppercase
        { '\u0391', 'A' }, // Α
        { '\u0392', 'B' }, // Β
        { '\u0395', 'E' }, // Ε
        { '\u0397', 'H' }, // Η
        { '\u0399', 'I' }, // Ι
        { '\u039A', 'K' }, // Κ
        { '\u039C', 'M' }, // Μ
        { '\u039D', 'N' }, // Ν
        { '\u039F', 'O' }, // Ο
        { '\u03A1', 'P' }, // Ρ
        { '\u03A4', 'T' }, // Τ
        { '\u03A5', 'Y' }, // Υ
        { '\u03A7', 'X' }, // Χ
        { '\u03A9', 'O' }, // Ω

        // Common Obfuscation Symbols / Leetspeak
        { '@', 'a' },
        { '$', 's' },
        { '0', 'o' },
        { '1', 'i' },
        { '3', 'e' },
        { '4', 'a' },
        { '5', 's' },
        { '7', 't' },
        { '8', 'b' },
        { '!', 'i' },
        { '+', 't' },
        { '|', 'l' }
    };

    static HomoglyphMap()
    {
        // Add Fullwidth ASCII (0xFF01 to 0xFF5E)
        for (int i = 0xFF01; i <= 0xFF5E; i++)
        {
            char fullwidth = (char)i;
            char ascii = (char)(i - 0xFEE0);
            RawMap[fullwidth] = ascii;
        }

        // Add Enclosed / Circled Lowercase (U+24D0 to U+24E9 -> 'a'..'z')
        for (int i = 0; i < 26; i++)
        {
            RawMap[(char)(0x24D0 + i)] = (char)('a' + i);
            RawMap[(char)(0x24B6 + i)] = (char)('A' + i);
        }

        Map = RawMap.ToFrozenDictionary();
    }

    public static readonly FrozenDictionary<char, char> Map;

    /// <summary>
    /// Translates a character to its ASCII Latin homoglyph if one exists; otherwise returns the character unchanged.
    /// </summary>
    public static char NormalizeChar(char c)
    {
        return Map.TryGetValue(c, out var normalized) ? normalized : c;
    }

    /// <summary>
    /// Replaces all known homoglyphs and confusable characters with standard ASCII equivalents.
    /// </summary>
    public static string NormalizeString(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        var span = input.AsSpan();
        var buffer = new char[span.Length];
        for (int i = 0; i < span.Length; i++)
        {
            buffer[i] = NormalizeChar(span[i]);
        }
        return new string(buffer);
    }
}
