namespace SafeSpeak.App.Contracts.Tests;

public sealed class ApplicationShutdownContractTests
{
    [Fact]
    public void MainWindow_ClosingEnforcesNoCancellationAndSilencesSpeech()
    {
        string source = Source("src", "SafeSpeak.App", "MainWindow.xaml.cs");
        string method = Method(
            source,
            "private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)");

        Assert.Contains("e.Cancel = false;", method);
        Assert.Contains("vm.StopAllSpeechForShutdown", method);
        Assert.Contains("app.Windows", method);
        Assert.Contains("window.Close", method);
    }

    [Fact]
    public void MainWindow_ClosedTriggersShutdownAndWatchdog()
    {
        string source = Source("src", "SafeSpeak.App", "MainWindow.xaml.cs");

        Assert.Contains("Application.Current?.Shutdown(0);", source);
        Assert.Contains("Environment.Exit(0);", source);
        Assert.Contains("vm.StopAllSpeechForShutdown", source);
    }

    [Fact]
    public void App_ConfiguresShutdownModeOnMainWindowClose()
    {
        string source = Source("src", "SafeSpeak.App", "App.xaml.cs");

        Assert.Contains("ShutdownMode = ShutdownMode.OnMainWindowClose;", source);
        Assert.Contains("Environment.Exit(e.ApplicationExitCode);", source);
    }

    [Fact]
    public void AccessibilitySetupViewModel_DisposesWithoutBlockingUIThread()
    {
        string source = Source(
            "src", "SafeSpeak.App", "ViewModels", "AccessibilitySetupViewModel.cs");
        string method = Method(source, "public void Dispose()");

        Assert.DoesNotContain(".GetAwaiter().GetResult()", method);
        Assert.DoesNotContain(".Wait()", method);
        Assert.DoesNotContain(".Result", method);
        Assert.Contains("_previewOutput.Stop()", method);
        Assert.Contains("_previewAudioRouter.Stop()", method);
        Assert.Contains("_previewOutput.DisposeAsync()", method);
    }

    [Fact]
    public void MainViewModel_BeginDisposeSilencesSpeechImmediately()
    {
        string source = Source(
            "src", "SafeSpeak.App", "ViewModels", "MainViewModel.cs");
        string method = Method(source, "private Task BeginDispose()");

        Assert.Contains("StopAllSpeechForShutdown()", method);
        Assert.Contains("_announcer.Dispose", method);
        Assert.Contains("public void StopAllSpeechForShutdown()", source);
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
