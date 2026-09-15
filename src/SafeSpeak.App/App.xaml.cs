using System;
using System.IO;
using System.Windows;
using SafeSpeak.App.ViewModels;
using SafeSpeak.App.Views;
using SafeSpeak.Core.Accessibility;
using SafeSpeak.Core.Diagnostics;
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
            AppLogger.LogFatalCrash("AppDomain.UnhandledException", $"CRASH DETECTED: Fatal unhandled AppDomain exception: {ex?.Message}", ex);
            PerformanceTracker.LogSnapshot("Crash.AppDomain");
            AppLogger.FlushAll(TimeSpan.FromSeconds(1));
            LogException("AppDomain.UnhandledException", ex);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            AppLogger.LogFatalCrash("DispatcherUnhandledException", $"CRASH DETECTED: Fatal unhandled Dispatcher exception: {args.Exception?.Message}", args.Exception);
            PerformanceTracker.LogSnapshot("Crash.Dispatcher");
            AppLogger.FlushAll(TimeSpan.FromSeconds(1));
            LogException("DispatcherUnhandledException", args.Exception);
            MessageBox.Show($"SafeSpeak Error:\n{args.Exception?.Message}", "SafeSpeak Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            AppLogger.LogError("TaskScheduler.UnobservedTaskException", $"Unobserved background task exception: {args.Exception?.Message}", args.Exception);
            PerformanceTracker.LogSnapshot("Fault.UnobservedTask");
            AppLogger.FlushAll(TimeSpan.FromSeconds(1));
            LogException("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        SessionEnding += (s, args) =>
        {
            AppLogger.LogInformation("Lifecycle", $"Windows session ending detected (Reason={args.ReasonSessionEnding}). SafeSpeak will shut down.");
            PerformanceTracker.LogSnapshot("SessionEnding");
            AppLogger.FlushAll(TimeSpan.FromSeconds(1));
        };

        try
        {
            string version = typeof(App).Assembly.GetName().Version?.ToString() ?? "Unknown";
            string os = Environment.OSVersion.ToString();
            int pid = Environment.ProcessId;
            AppLogger.LogInformation("Lifecycle", $"SafeSpeak session started. Version={version}, OS={os}, ProcessId={pid}, Machine={Environment.MachineName}");
            PerformanceTracker.Instance.Start();
            PerformanceTracker.LogSnapshot("Startup");
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
                if (!settings.HasCompletedOnboarding)
                {
                    AppLogger.LogInformation("App", "Incomplete onboarding detected on startup. Starting over with clean initial settings.");
                    settings.ResetIncompleteOnboarding();
                    settings.TrySave(out _);
                }

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
                        try { tempAnnouncer.StopSpeaking(); } catch { }
                        try { tempAnnouncer.Dispose(); } catch { }

                        var mainWindow = new MainWindow();
                        MainWindow = mainWindow;
                        ShutdownMode = ShutdownMode.OnMainWindowClose;

                        if (mainWindow.DataContext is MainViewModel vm)
                        {
                            mainWindow.FocusNarrator?.SuppressNextFocusAnnouncement();
                            string confirmationReminder = settings.IsAwaitingAccessibilityConfirmation
                                ? " Reader and Theme will be confirmed the next time SafeSpeak launches."
                                : string.Empty;
                            vm.AnnounceState(
                                $"Setup complete. SafeSpeak is ready.{confirmationReminder} It remains disarmed until you choose Arm SafeSpeak.",
                                interrupt: true);
                        }

                        mainWindow.Show();
                        wizard?.Close();
                    });

                wizard = new AccessibilitySetupDialog(setupVm);
                wizard.Closed += (_, _) =>
                {
                    try { tempAnnouncer.StopSpeaking(); } catch { }
                    try { tempAnnouncer.Dispose(); } catch { }
                    if (MainWindow == wizard)
                    {
                        AppLogger.LogInformation("Lifecycle", "Setup wizard closed as primary window. Initiating shutdown.");
                        AppLogger.FlushAll(TimeSpan.FromSeconds(1));
                        Shutdown(0);
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(1500).ConfigureAwait(false);
                            try { Environment.Exit(0); } catch { }
                        });
                    }
                };
                MainWindow = wizard;
                wizard.Show();
            }
            else if (settings.ShouldPromptSetupUpdate)
            {
                AppLogger.LogInformation("App", "Settings update detected. Launching SetupUpdatePromptDialog...");
                var promptAnnouncer = new ScreenReaderAnnouncer();
                promptAnnouncer.SpeechRate = settings.ReaderSpeechRate;
                promptAnnouncer.SpeechVolume = settings.ReaderSpeechVolume;
                promptAnnouncer.SelectAudioEndpoint(settings.SelectedGuidanceAudioEndpointId);
                SetupUpdatePromptDialog? promptDialog = null;

                promptDialog = new SetupUpdatePromptDialog(
                    settings,
                    promptAnnouncer,
                    onAccepted: () =>
                    {
                        AppLogger.LogInformation("App", "User accepted setup update. Launching AccessibilitySetupDialog...");
                        try { promptAnnouncer.StopSpeaking(); } catch { }
                        try { promptAnnouncer.Dispose(); } catch { }

                        var setupAnnouncer = new ScreenReaderAnnouncer();
                        setupAnnouncer.SpeechRate = settings.ReaderSpeechRate;
                        setupAnnouncer.SpeechVolume = settings.ReaderSpeechVolume;
                        setupAnnouncer.SelectAudioEndpoint(settings.SelectedGuidanceAudioEndpointId);
                        AccessibilitySetupDialog? wizard = null;

                        var setupVm = new AccessibilitySetupViewModel(
                            settings,
                            setupAnnouncer,
                            onCompleted: () =>
                            {
                                AppLogger.LogInformation("App", "AccessibilitySetupDialog completed. Showing MainWindow...");
                                try { setupAnnouncer.StopSpeaking(); } catch { }
                                try { setupAnnouncer.Dispose(); } catch { }

                                var mainWindow = new MainWindow();
                                MainWindow = mainWindow;
                                ShutdownMode = ShutdownMode.OnMainWindowClose;

                                if (mainWindow.DataContext is MainViewModel vm)
                                {
                                    mainWindow.FocusNarrator?.SuppressNextFocusAnnouncement();
                                    vm.AnnounceState("Accessibility settings updated successfully.", interrupt: true);
                                }

                                mainWindow.Show();
                                wizard?.Close();
                            },
                            changeExistingProfile: true);

                        wizard = new AccessibilitySetupDialog(setupVm);
                        wizard.Closed += (_, _) =>
                        {
                            try { setupAnnouncer.StopSpeaking(); } catch { }
                            try { setupAnnouncer.Dispose(); } catch { }
                            if (MainWindow == wizard)
                            {
                                Shutdown(0);
                                _ = Task.Run(async () =>
                                {
                                    await Task.Delay(1500).ConfigureAwait(false);
                                    try { Environment.Exit(0); } catch { }
                                });
                            }
                        };
                        MainWindow = wizard;
                        wizard.Show();
                        promptDialog?.Close();
                    },
                    onDeclined: () =>
                    {
                        AppLogger.LogInformation("App", "User declined setup update. Initializing MainWindow...");
                        try { promptAnnouncer.StopSpeaking(); } catch { }
                        try { promptAnnouncer.Dispose(); } catch { }

                        var mainWindow = new MainWindow();
                        MainWindow = mainWindow;
                        ShutdownMode = ShutdownMode.OnMainWindowClose;
                        mainWindow.Show();
                        promptDialog?.Close();
                    });

                promptDialog.Closed += (_, _) =>
                {
                    try { promptAnnouncer.StopSpeaking(); } catch { }
                    try { promptAnnouncer.Dispose(); } catch { }
                    if (MainWindow == promptDialog)
                    {
                        AppLogger.LogInformation("Lifecycle", "Setup update prompt closed as primary window. Initiating shutdown.");
                        AppLogger.FlushAll(TimeSpan.FromSeconds(1));
                        Shutdown(0);
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(1500).ConfigureAwait(false);
                            try { Environment.Exit(0); } catch { }
                        });
                    }
                };
                MainWindow = promptDialog;
                promptDialog.Show();
            }
            else
            {
                AppLogger.LogInformation("App", "Initializing MainWindow...");
                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
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
        PerformanceTracker.LogSnapshot("Shutdown");
        PerformanceTracker.Instance.Stop();
        AppLogger.LogInformation("Lifecycle", $"SafeSpeak session terminated cleanly. (ExitCode={e.ApplicationExitCode})");
        AppLogger.FlushAll(TimeSpan.FromSeconds(2));
        base.OnExit(e);
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000).ConfigureAwait(false);
            try { Environment.Exit(e.ApplicationExitCode); } catch { }
        });
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
