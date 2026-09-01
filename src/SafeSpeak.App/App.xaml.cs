using System.IO;
using System.Windows;
using SafeSpeak.App.ViewModels;
using SafeSpeak.App.Views;
using SafeSpeak.Core.Accessibility;
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
            LogException("AppDomain.UnhandledException", args.ExceptionObject as Exception);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            LogException("DispatcherUnhandledException", args.Exception);
            MessageBox.Show($"SafeSpeak Error:\n{args.Exception?.Message}", "SafeSpeak Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            var settings = AppSettings.Load();
            ThemeManager.Apply(settings.EffectiveTheme);
            SystemParameters.StaticPropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(SystemParameters.HighContrast))
                    ThemeManager.RefreshForSystemSettings();
            };

            if (!settings.HasCompletedOnboarding)
            {
                var tempAnnouncer = new ScreenReaderAnnouncer();
                AccessibilitySetupDialog? wizard = null;

                var setupVm = new AccessibilitySetupViewModel(
                    settings,
                    tempAnnouncer,
                    onCompleted: () =>
                    {
                        var mainWindow = new MainWindow();
                        MainWindow = mainWindow;
                        mainWindow.Show();
                        wizard?.Close();
                    },
                    onRestartRequired: Shutdown);

                wizard = new AccessibilitySetupDialog(setupVm);
                wizard.Closed += (_, _) => tempAnnouncer.Dispose();
                MainWindow = wizard;
                wizard.Show();
            }
            else
            {
                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                mainWindow.Show();
            }
        }
        catch (Exception ex)
        {
            LogException("OnStartup.Launch", ex);
            MessageBox.Show($"Failed to launch SafeSpeak:\n\n{ex.Message}\n\n{ex.InnerException?.Message}", "SafeSpeak Launch Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
