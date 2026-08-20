using SafeSpeak.Core.Moderation;

namespace SafeSpeak.Core.Tests;

public class HomoglyphTests
{
    [Theory]
    [InlineData('\u0430', 'a')] // Cyrillic а
    [InlineData('\u0435', 'e')] // Cyrillic е
    [InlineData('\u043E', 'o')] // Cyrillic о
    [InlineData('\u0440', 'p')] // Cyrillic р
    [InlineData('\u0441', 'c')] // Cyrillic с
    [InlineData('\u0443', 'y')] // Cyrillic у
    [InlineData('\u0445', 'x')] // Cyrillic х
    [InlineData('\u03B1', 'a')] // Greek α
    [InlineData('@', 'a')]      // Leetspeak @
    [InlineData('$', 's')]      // Leetspeak $
    [InlineData('0', 'o')]      // Leetspeak 0
    [InlineData('1', 'i')]      // Leetspeak 1
    [InlineData('3', 'e')]      // Leetspeak 3
    [InlineData('7', 't')]      // Leetspeak 7
    public void NormalizeChar_MapsConfusablesToAscii(char input, char expected)
    {
        char actual = HomoglyphMap.NormalizeChar(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NormalizeString_TranslatesMixedCyrillicWord()
    {
        // Word containing Cyrillic 'а', 'е', 'о'
        string input = "b\u0430d w\u043Erk\u0435r";
        string result = HomoglyphMap.NormalizeString(input);

        Assert.Equal("bad worker", result);
    }

    [Fact]
    public void NormalizeString_HandlesFullwidthCharacters()
    {
        // Fullwidth "ABC" -> "ABC"
        string input = "\uFF21\uFF22\uFF23";
        string result = HomoglyphMap.NormalizeString(input);

        Assert.Equal("ABC", result);
    }
}
