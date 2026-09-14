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

    [Theory]
    [InlineData("𝕻𝖎𝖈𝖐𝖑𝖊 ✨", "Pickle")]
    [InlineData("𝖝𝖞", "xy")]
    [InlineData("🖤Danis🖤", "Danis")]
    [InlineData("Meraj_hh🇦🇫", "Meraj hh")]
    [InlineData("O’Connor", "O'Connor")]
    [InlineData("😶‍🌫️", "")]
    public void CleanDisplayNameForSpeech_ReturnsOnlySpeakableNameContent(
        string input,
        string expected)
    {
        Assert.Equal(expected, UnicodeNormalizer.CleanDisplayNameForSpeech(input));
    }

    [Theory]
    [InlineData("😂", true)]
    [InlineData("🔥", true)]
    [InlineData("❤️", true)]
    [InlineData("👍🏽", true)]
    [InlineData("👨‍👩‍👧‍👦", true)]
    [InlineData("1️⃣", true)]
    [InlineData("🇺🇸", true)]
    [InlineData("hello", false)]
    [InlineData("123", false)]
    [InlineData("!", false)]
    public void IsEmoji_CorrectlyClassifiesGraphemeElements(string element, bool expected)
    {
        Assert.Equal(expected, UnicodeNormalizer.IsEmoji(element));
    }

    [Fact]
    public void CollapseRepeatedEmojis_CollapsesSpammedRepeatedEmojisToOne()
    {
        string input = "W play! 😂😂😂😂😂 that was insane 🔥🔥🔥";
        string result = UnicodeNormalizer.CollapseRepeatedEmojis(input, maxRepeated: 1, maxTotal: 3);

        Assert.Equal("W play! 😂 that was insane 🔥", result);
    }

    [Fact]
    public void CollapseRepeatedEmojis_CollapsesSpacedRepeatedEmojis()
    {
        string input = "🔥 🔥 🔥 🔥 LET'S GO";
        string result = UnicodeNormalizer.CollapseRepeatedEmojis(input, maxRepeated: 1, maxTotal: 3);

        Assert.Equal("🔥 LET'S GO", result);
    }

    [Fact]
    public void CollapseRepeatedEmojis_SupportsMaxRepeatedTwo()
    {
        string input = "😂😂😂😂";
        string result = UnicodeNormalizer.CollapseRepeatedEmojis(input, maxRepeated: 2, maxTotal: 5);

        Assert.Equal("😂😂", result);
    }

    [Fact]
    public void CollapseRepeatedEmojis_CapsTotalEmojisPerMessage()
    {
        // 6 different emojis, capped at 3
        string input = "🎉 🎈 🎂 🎁 🥳 🎊";
        string result = UnicodeNormalizer.CollapseRepeatedEmojis(input, maxRepeated: 1, maxTotal: 3);

        Assert.Equal("🎉 🎈 🎂", result);
    }

    [Fact]
    public void CollapseRepeatedEmojis_ZeroTotalOmitsAllEmojis()
    {
        string input = "great stream 😂 thanks 🔥 for hosting";
        string result = UnicodeNormalizer.CollapseRepeatedEmojis(input, maxRepeated: 1, maxTotal: 0);

        Assert.Equal("great stream thanks for hosting", result);
    }

    [Fact]
    public void CleanForSpeech_CollapsesEmojiSpamAndCleansWhitespace()
    {
        string input = "Check this https://safe.link 😂😂😂😂😂 🔥🔥🔥🔥🔥 awesome!";
        string result = UnicodeNormalizer.CleanForSpeech(input, stripUrls: true, maxRepeatedEmojis: 1, maxTotalEmojis: 2);

        Assert.Contains("[link removed]", result);
        Assert.Contains("😂", result);
        Assert.Contains("🔥", result);
        Assert.DoesNotContain("😂😂", result);
        Assert.DoesNotContain("🔥🔥", result);
    }
}
