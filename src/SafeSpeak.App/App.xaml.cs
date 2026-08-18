using System.Windows;
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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var settingsStore = new AppSettingsStore();
            AppSettings settings = await settingsStore.LoadAsync(_shutdown.Token);

            if (!settings.FirstRunComplete)
            {
                var setupWindow = new AccessibilitySetupWindow(settings.AccessibilityMode);
                if (setupWindow.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }

                settings = settings with
                {
                    FirstRunComplete = true,
                    AccessibilityMode = setupWindow.SelectedMode,
                };
                await settingsStore.SaveAsync(settings, _shutdown.Token);
            }

            var runtime = new SafeSpeakRuntime(settings.EnglishOnly);
            _tikFinity = new TikFinityWebSocketClient();
            _commandServer = new StreamDeckCommandServer(runtime);

            _tikFinity.ConnectionStateChanged += (_, state) =>
                runtime.SetConnected(state == ConnectionState.Connected);
            _tikFinity.MessageReceived += (_, message) =>
                _ = runtime.ProcessChatMessageAsync(message, _shutdown.Token).AsTask();

            var mainWindow = new MainWindow(runtime, settings, settingsStore);
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
        _shutdown.Dispose();
        base.OnExit(e);
    }
}
