using SafeSpeak.Core.Moderation;

namespace SafeSpeak.Core.Tests.Moderation;

public sealed class TextNormalizerTests
{
    [Fact]
    public void RemovesInvisibleCharactersAndCollapsesWhitespace()
    {
        TextNormalizationResult result = TextNormalizer.Normalize("  hello\u200B    world  ");

        Assert.Equal("hello world", result.CleanText);
        Assert.True(result.HadInvisibleCharacters);
    }

    [Fact]
    public void ProducesCompactComparisonForSpacingAndLeetspeak()
    {
        TextNormalizationResult result = TextNormalizer.Normalize("b 4 d w 0 r d");

        Assert.Equal("badword", result.CompactComparisonText);
    }

    [Fact]
    public void MapsCommonUnicodeConfusablesForComparison()
    {
        TextNormalizationResult result = TextNormalizer.Normalize("p\u0430yp\u0430l");

        Assert.Equal("paypal", result.CompactComparisonText);
        Assert.Contains(UnicodeScript.Latin, result.Scripts);
        Assert.Contains(UnicodeScript.Cyrillic, result.Scripts);
        Assert.True(result.HasMixedScripts);
    }

    [Fact]
    public void LimitsRepeatedCharactersInSpeakableText()
    {
        TextNormalizationResult result = TextNormalizer.Normalize("heyyyyyyyy", maximumRepeatedCharacters: 2);

        Assert.Equal("heyy", result.CleanText);
    }
}
