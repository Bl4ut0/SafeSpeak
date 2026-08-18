using System.Windows;
using SafeSpeak.App.Accessibility;
using SafeSpeak.Core.Chat;
using SafeSpeak.Infrastructure.Control;
using SafeSpeak.Infrastructure.Settings;
using SafeSpeak.Infrastructure.TikFinity;

namespace SafeSpeak.App;

public partial class App : Application
{
    private readonly CancellationTokenSource _shutdown = new();
    private TikFinityWebSocketClient? _tikFinity;
    private StreamDeckCommandServer? _commandServer;
    private ISpokenGuidanceService? _spokenGuidance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _spokenGuidance = new WindowsSpokenGuidanceService();
            var settingsStore = new AppSettingsStore();
            AppSettings settings = await settingsStore.LoadAsync(_shutdown.Token);
            bool testFirstRun = e.Args.Contains("--test-first-run", StringComparer.OrdinalIgnoreCase);

            if (!settings.FirstRunComplete || testFirstRun)
            {
                var setupWindow = new AccessibilitySetupWindow(
                    settings.AccessibilityMode,
                    _spokenGuidance,
                    announcePrompt: true);
                if (setupWindow.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }

                settings = settings with
                {
                    FirstRunComplete = true,
                    AccessibilityMode = setupWindow.SelectedMode,
                    SpokenGuidanceEnabled = setupWindow.SpokenGuidanceEnabled,
                };
                if (!testFirstRun)
                {
                    await settingsStore.SaveAsync(settings, _shutdown.Token);
                }
            }

            var runtime = new SafeSpeakRuntime(settings.EnglishOnly);
            _tikFinity = new TikFinityWebSocketClient();
            _commandServer = new StreamDeckCommandServer(runtime);

            _tikFinity.ConnectionStateChanged += (_, state) =>
                runtime.SetConnected(state == ConnectionState.Connected);
            _tikFinity.MessageReceived += (_, message) =>
                _ = runtime.ProcessChatMessageAsync(message, _shutdown.Token).AsTask();

            var mainWindow = new MainWindow(runtime, settings, settingsStore, _spokenGuidance);
            MainWindow = mainWindow;
            mainWindow.Show();

            _ = _tikFinity.RunAsync(_shutdown.Token);
            _ = _commandServer.RunAsync(_shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            Shutdown();
        }
        catch (Exception exception)
        {
            _spokenGuidance?.Speak("SafeSpeak could not start. A visible error message is available.");
            MessageBox.Show(
                $"SafeSpeak could not start. {exception.Message}",
                "SafeSpeak startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _shutdown.Cancel();
        _commandServer?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _tikFinity?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _spokenGuidance?.Dispose();
        _shutdown.Dispose();
        base.OnExit(e);
    }
}
