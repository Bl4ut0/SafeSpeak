using System;
using System.IO;
using System.Windows;
using SafeSpeak.App.ViewModels;
using SafeSpeak.App.Views;
using SafeSpeak.Core.Accessibility;
using SafeSpeak.Core.Logging;
using SafeSpeak.Core.Models;

namespace SafeSpeak.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnLastWindowClose;

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            AppLogger.LogError("App", $"Unhandled AppDomain exception: {ex?.Message}", ex);
            LogException("AppDomain.UnhandledException", ex);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            AppLogger.LogError("App", $"Unhandled Dispatcher exception: {args.Exception?.Message}", args.Exception);
            LogException("DispatcherUnhandledException", args.Exception);
            MessageBox.Show($"SafeSpeak Error:\n{args.Exception?.Message}", "SafeSpeak Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            AppLogger.LogInformation("App", "SafeSpeak application startup began.");
            var settings = AppSettings.Load();
            AppLogger.LogInformation("App", $"Settings loaded: HasCompletedOnboarding={settings.HasCompletedOnboarding}, IsAwaitingAccessibilityConfirmation={settings.IsAwaitingAccessibilityConfirmation}");
            ThemeManager.Apply(settings.EffectiveTheme);
            ThemeManager.ApplyTextScale(settings.InterfaceTextScalePercent);
            SystemParameters.StaticPropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(SystemParameters.HighContrast))
                    ThemeManager.RefreshForSystemSettings();
            };

            if (!settings.HasCompletedOnboarding ||
                settings.IsAwaitingAccessibilityConfirmation)
            {
                AppLogger.LogInformation("App", "Launching AccessibilitySetupDialog...");
                var tempAnnouncer = new ScreenReaderAnnouncer();
                tempAnnouncer.SpeechRate = settings.ReaderSpeechRate;
                tempAnnouncer.SpeechVolume = settings.ReaderSpeechVolume;
                tempAnnouncer.SelectAudioEndpoint(settings.SelectedGuidanceAudioEndpointId);
                AccessibilitySetupDialog? wizard = null;

                var setupVm = new AccessibilitySetupViewModel(
                    settings,
                    tempAnnouncer,
                    onCompleted: () =>
                    {
                        AppLogger.LogInformation("App", "AccessibilitySetupDialog completed. Showing MainWindow...");
                        var mainWindow = new MainWindow();
                        MainWindow = mainWindow;
                        mainWindow.Show();
                        wizard?.Close();
                    });

                wizard = new AccessibilitySetupDialog(setupVm);
                wizard.Closed += (_, _) => tempAnnouncer.Dispose();
                MainWindow = wizard;
                wizard.Show();
            }
            else
            {
                AppLogger.LogInformation("App", "Initializing MainWindow...");
                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                AppLogger.LogInformation("App", "Calling mainWindow.Show()...");
                mainWindow.Show();
                AppLogger.LogInformation("App", "mainWindow.Show() succeeded.");
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("App", $"Exception during startup: {ex.Message}", ex);
            LogException("OnStartup.Launch", ex);
            MessageBox.Show($"Failed to launch SafeSpeak:\n\n{ex.Message}\n\n{ex.InnerException?.Message}", "SafeSpeak Launch Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.LogInformation("App", $"SafeSpeak application exiting with code {e.ApplicationExitCode}.");
        base.OnExit(e);
    }

    private static void LogException(string source, Exception? ex)
    {
        string logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SafeSpeak",
            "Logs");
        string logPath = Path.Combine(logDirectory, "startup_error.log");
        string message = $"[{DateTime.Now}] {source}: {ex?.ToString()}\n\n";
        try
        {
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(logPath, message);
        }
        catch { }
    }
}
