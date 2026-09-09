using SafeSpeak.Core.Moderation;

namespace SafeSpeak.Core.Tests;

public class ScriptValidatorTests
{
    [Fact]
    public void HasMixedScriptsInWord_DetectsCyrillicInjectedIntoLatinWord()
    {
        // "s\u0442reamer" has Latin 's', Cyrillic 'т' (U+0442), Latin 'reamer'
        string mixedWord = "s\u0442reamer";
        bool isMixed = ScriptValidator.HasMixedScriptsInWord(mixedWord);

        Assert.True(isMixed);
    }

    [Fact]
    public void HasMixedScriptsInWord_ReturnsFalseForPureLatinWord()
    {
        string pureLatin = "streamer";
        bool isMixed = ScriptValidator.HasMixedScriptsInWord(pureLatin);

        Assert.False(isMixed);
    }

    [Fact]
    public void IsLatinOrEmojiOnly_AllowsEnglishWithPunctuationAndEmojis()
    {
        string text = "Hello streamer! Keep going! ❤️ 🔥 100%";
        bool result = ScriptValidator.IsLatinOrEmojiOnly(text);

        Assert.True(result);
    }

    [Fact]
    public void IsLatinOrEmojiOnly_FlagsNonLatinScriptsWhenEnglishOnly()
    {
        string text = "Привет как дела";
        bool result = ScriptValidator.IsLatinOrEmojiOnly(text);

        Assert.False(result);
    }

    [Theory]
    [InlineData("Привет\tHello")]
    [InlineData("Привет-Hello")]
    [InlineData("Привет_Hello")]
    [InlineData("Привет/Hello")]
    public void ContainsMixedScriptWords_TreatsPunctuationAndWhitespaceAsBoundaries(
        string text)
    {
        Assert.False(ScriptValidator.ContainsMixedScriptWords(text));
    }

    [Fact]
    public void IsLatinOrEmojiOnly_RejectsCjkCompatibilitySquareCharacters()
    {
        string rawMessage = ".1, 2, 3 ㌕㌖㌖㌕㌖㌖㌕㌖㌖㌕㌖㌖1, 2, 3 ㌕㌖";
        Assert.False(ScriptValidator.IsLatinOrEmojiOnly(rawMessage));
        Assert.False(ScriptValidator.IsLatinOrEmojiOnly("㌕"));
        Assert.False(ScriptValidator.IsLatinOrEmojiOnly("㌖"));
    }

    [Theory]
    [InlineData("キログラム")] // Katakana
    [InlineData("こんにちは")] // Hiragana
    [InlineData("你好")] // Chinese Hanzi
    [InlineData("안녕하세요")] // Korean Hangul
    [InlineData("สวัสดี")] // Thai
    [InlineData("مرحبا")] // Arabic
    [InlineData("שלום")] // Hebrew
    [InlineData("नमस्ते")] // Devanagari
    [InlineData("வணக்கம்")] // Tamil
    public void IsLatinOrEmojiOnly_RejectsNonLatinWritingSystems(string nonLatinText)
    {
        Assert.False(ScriptValidator.IsLatinOrEmojiOnly(nonLatinText));
    }

    [Theory]
    [InlineData("¡Hola amigo! ¿Cómo estás? 100% bien")]
    [InlineData("Grüße aus München! C'est très bien, café")]
    [InlineData("Price is $10.99 or 15€ or £20 or ¥500")]
    [InlineData("🇦🇫 🇺🇸 ❤️ 🔥 ✨ ⭐ 👍")]
    public void IsLatinOrEmojiOnly_AllowsEnglishSpanishFrenchAccentsAndEmojis(string validText)
    {
        Assert.True(ScriptValidator.IsLatinOrEmojiOnly(validText));
    }

    [Fact]
    public void ContainsMixedScriptWords_DetectsCjkMixedWithLatin()
    {
        Assert.True(ScriptValidator.ContainsMixedScriptWords("streamer㌕test"));
        Assert.True(ScriptValidator.ContainsMixedScriptWords("f\u3042ck"));
    }

    [Theory]
    [InlineData("Ω")] // Ohm sign, compatibility-decomposes to Greek omega
    [InlineData("ℵ")] // Alef symbol, compatibility-decomposes to Hebrew alef
    [InlineData("\u2066")] // Left-to-right isolate formatting control
    public void IsLatinOrEmojiOnly_RejectsNonLatinCompatibilityAndFormattingCharacters(string text)
    {
        Assert.False(ScriptValidator.IsLatinOrEmojiOnly(text));
    }

    [Fact]
    public void IsLatinOrEmojiOnly_RejectsUnpairedSurrogate()
    {
        Assert.False(ScriptValidator.IsLatinOrEmojiOnly("\uD83D"));
    }

    [Fact]
    public void IsLatinOrEmojiOnly_AllowsJoinedEmojiSequence()
    {
        Assert.True(ScriptValidator.IsLatinOrEmojiOnly("👨‍👩‍👧‍👦"));
    }
}
