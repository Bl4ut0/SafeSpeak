using SafeSpeak.Core.Moderation;

namespace SafeSpeak.Core.Tests;

public class UnicodeNormalizerTests
{
    [Fact]
    public void StripInvisibleCharacters_RemovesZeroWidthSpaceAndJoiners()
    {
        // "f\u200Bu\u200Cc\u200Dk" contains zero-width space, zero-width non-joiner, zero-width joiner
        string input = "f\u200Bu\u200Cc\u200Dk";
        string result = UnicodeNormalizer.StripInvisibleCharacters(input);

        Assert.Equal("fuck", result);
    }

    [Fact]
    public void RemoveDiacritics_StripsCombiningDiacriticalMarksAndZalgo()
    {
        // Zalgo / heavy combining accents
        string input = "h̵̡e̸l̶l̴o̸";
        string result = UnicodeNormalizer.RemoveDiacritics(input);

        Assert.Equal("hello", result);
    }

    [Fact]
    public void StripUrls_ReplacesHttpAndWwwLinks()
    {
        string input = "Check this out https://malicious-site.com/steal-info now!";
        string result = UnicodeNormalizer.StripUrls(input);

        Assert.Contains("[link removed]", result);
        Assert.DoesNotContain("https://", result);
    }

    [Fact]
    public void CollapseRepeats_LimitsExcessiveConsecutiveLetters()
    {
        string input = "fuuuuuuuck";
        string result = UnicodeNormalizer.CollapseRepeats(input);

        Assert.Equal("fuuck", result);
    }

    [Fact]
    public void CollapseSpacedLetters_JoinsSeparatedSingleLetters()
    {
        string input = "f u c k this";
        string result = UnicodeNormalizer.CollapseSpacedLetters(input);

        Assert.Equal("fuck this", result);
    }

    [Fact]
    public void NormalizeForInspection_HandlesComplexCombinedEvasion()
    {
        // Zero-width space + Cyrillic 'а' + spacing + uppercase
        string input = "F\u200B \u0430 \u200CC K";
        string result = UnicodeNormalizer.NormalizeForInspection(input);

        Assert.Equal("fack", result);
    }
}
