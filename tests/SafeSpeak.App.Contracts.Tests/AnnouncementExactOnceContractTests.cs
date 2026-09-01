namespace SafeSpeak.App.Contracts.Tests;

public sealed class AnnouncementExactOnceContractTests
{
    [Fact]
    public void InitialPageAnnouncement_IsReentryGuardedAndHasOneLoadedCallSite()
    {
        string viewModel = Source(
            "src", "SafeSpeak.App", "ViewModels",
            "AccessibilitySetupViewModel.cs");
        string dialog = Source(
            "src", "SafeSpeak.App", "Views",
            "AccessibilitySetupDialog.xaml.cs");
        string method = Method(viewModel, "public void AnnounceInitialPrompt()");

        Assert.Equal(1, Count(dialog, "_viewModel.AnnounceInitialPrompt"));
        Assert.Contains("_initialPromptAnnounced", viewModel);
        Assert.Contains("if (_initialPromptAnnounced) return;", method);
        Assert.Contains("_initialPromptAnnounced = true;", method);
        Assert.True(
            method.IndexOf("_initialPromptAnnounced = true;", StringComparison.Ordinal) <
            method.IndexOf("AnnounceCurrentPage();", StringComparison.Ordinal),
            "The re-entry flag must be set before the announcement is dispatched.");
        Assert.Equal(1, Count(method, "AnnounceCurrentPage();"));
    }

    [Fact]
    public void MeaningfulPageNavigation_AnnouncesOnceAndSamePageNavigationIsANoOp()
    {
        string viewModel = Source(
            "src", "SafeSpeak.App", "ViewModels",
            "AccessibilitySetupViewModel.cs");
        string navigate = Method(
            viewModel,
            "private void NavigateTo(AccessibilitySetupPage page)");
        string pageChanged = Method(
            viewModel,
            "partial void OnCurrentPageChanged(AccessibilitySetupPage value)");

        Assert.Contains("if (CurrentPage == page) return;", navigate);
        Assert.Equal(1, Count(navigate, "CurrentPage = page;"));
        Assert.Equal(1, Count(navigate, "FocusRequested?.Invoke"));
        Assert.Equal(1, Count(navigate, "AnnounceCurrentPage();"));
        Assert.DoesNotContain("AnnounceCurrentPage", pageChanged);
    }

    [Fact]
    public void SaveFailure_IsOneInterruptingAnnouncementWithoutPageReplay()
    {
        string viewModel = Source(
            "src", "SafeSpeak.App", "ViewModels",
            "AccessibilitySetupViewModel.cs");
        string failure = Method(viewModel, "private void ReportSaveFailure(string? error)");

        Assert.Equal(1, Count(failure, "_announcer.Announce("));
        Assert.Equal(1, Count(failure, "interrupt: true"));
        Assert.Equal(1, Count(failure, "FocusRequested?.Invoke"));
        Assert.DoesNotContain("AnnounceCurrentPage", failure);
        Assert.DoesNotContain("NavigateTo(", failure);
    }

    [Fact]
    public void FocusNarration_DeduplicatesAndDoesNotMirrorIntoExternalLiveRegion()
    {
        string narrator = Source(
            "src", "SafeSpeak.App", "Accessibility",
            "IntegratedFocusNarrator.cs");
        string announcer = Source(
            "src", "SafeSpeak.Core", "Accessibility",
            "ScreenReaderAnnouncer.cs");
        string focusHandler = Method(
            narrator,
            "private void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)");
        string announceFocus = Method(
            announcer,
            "public void AnnounceFocus(string text)");
        string announce = Method(
            announcer,
            "public void Announce(string text, bool interrupt = false)");

        Assert.Contains("string.Equals(announcement, _lastAnnouncement", focusHandler);
        Assert.Contains("TimeSpan.FromMilliseconds(250)", focusHandler);
        Assert.Equal(1, Count(focusHandler, "_announcer.AnnounceFocus(announcement)"));
        Assert.DoesNotContain("_announcer.Announce(", focusHandler);

        Assert.DoesNotContain("AnnouncementRequested?.Invoke", announceFocus);
        Assert.Equal(1, Count(announce, "AnnouncementRequested?.Invoke"));
    }

    [Fact]
    public void VoiceSelection_AnnouncesOptionButNeverSynthesizesAFullPreview()
    {
        string main = Source(
            "src", "SafeSpeak.App", "ViewModels", "MainViewModel.cs");
        string selection = Method(
            main,
            "partial void OnSelectedVoiceChanged(string value)");

        Assert.Equal(1, Count(selection, "AnnounceOptionSelection("));
        Assert.DoesNotContain("_voicePreviewOutput.Speak", selection);
        Assert.DoesNotContain("TestSelectedVoice", selection);
        Assert.DoesNotContain("Synthesize", selection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sample", selection, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HostileRawChat_CannotReachAutomationNamesOrAnnouncementCalls()
    {
        string xaml = Source("src", "SafeSpeak.App", "MainWindow.xaml");
        string main = Source(
            "src", "SafeSpeak.App", "ViewModels", "MainViewModel.cs");
        string incoming = Method(
            main,
            "private async Task HandleIncomingMessageAsync(");

        Assert.Contains(
            "AutomationProperties.Name\" Value=\"{Binding AccessibleSummary}\"",
            xaml);
        Assert.DoesNotContain("{Binding Message.RawText}", xaml);
        Assert.DoesNotContain("{Binding RawText}", xaml);
        Assert.DoesNotContain("{Binding AuthorDisplayName}", xaml);
        Assert.DoesNotContain("{Binding ReasonDescription}", xaml);

        Assert.DoesNotContain("message.RawText", incoming);
        Assert.DoesNotContain("message.AuthorDisplayName", incoming);
        Assert.DoesNotContain("decision.Message", incoming);
        Assert.DoesNotContain("decision.ReasonDescription", incoming);
        Assert.Contains("LiveFeed.Insert(0, decision)", incoming);
        Assert.Contains("_ttsQueue.Enqueue(decision, bypassPause)", incoming);
        Assert.DoesNotContain("SpeakPrivateNoticeAsync", incoming);
    }

    private static string Source(params string[] segments) =>
        File.ReadAllText(RepositoryFile(segments));

    private static string Method(string source, string signature)
    {
        int signatureStart = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(signatureStart >= 0, $"Method signature not found: {signature}");
        int bodyStart = source.IndexOf('{', signatureStart);
        Assert.True(bodyStart >= 0, $"Method body not found: {signature}");

        int depth = 0;
        for (int index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{') depth++;
            if (source[index] != '}') continue;
            depth--;
            if (depth == 0) return source[signatureStart..(index + 1)];
        }

        throw new InvalidOperationException($"Unterminated method body: {signature}");
    }

    private static int Count(string text, string value)
    {
        int count = 0;
        int offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string RepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "SafeSpeak.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(
            new[] { directory!.FullName }.Concat(segments).ToArray());
    }
}
