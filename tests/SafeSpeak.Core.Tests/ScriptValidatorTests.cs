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
}
