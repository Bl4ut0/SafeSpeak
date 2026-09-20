using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using SafeSpeak.Core.Accessibility;
using SafeSpeak.Core.Models;

namespace SafeSpeak.App.Views;

public partial class SetupUpdatePromptDialog : Window
{
    private readonly AppSettings _settings;
    private readonly ScreenReaderAnnouncer? _announcer;
    private readonly Action? _onAccepted;
    private readonly Action? _onDeclined;
    private bool _handled;

    public bool IsClosing { get; private set; }

    public SetupUpdatePromptDialog(
        AppSettings settings,
        ScreenReaderAnnouncer? announcer = null,
        Action? onAccepted = null,
        Action? onDeclined = null)
    {
        _settings = settings;
        _announcer = announcer;
        _onAccepted = onAccepted;
        _onDeclined = onDeclined;

        InitializeComponent();

        AutomationProperties.SetHelpText(this, ReleaseUpdateInfo.GetHelpText());

        PreviewKeyDown += SetupUpdatePromptDialog_PreviewKeyDown;
        Loaded += SetupUpdatePromptDialog_Loaded;
        Closing += SetupUpdatePromptDialog_Closing;
    }

    public IReadOnlyList<string> HighlightItems { get; } = ReleaseUpdateInfo.GetFormattedBulletHighlights();

    public static string FullUpdateAnnouncement => ReleaseUpdateInfo.GetAnnouncementText();

    private void SetupUpdatePromptDialog_Loaded(object sender, RoutedEventArgs e)
    {
        YesButton.Focus();
        Keyboard.Focus(YesButton);

        _announcer?.Announce(FullUpdateAnnouncement, interrupt: true);
    }

    private void SetupUpdatePromptDialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl)
        {
            _announcer?.StopSpeaking();
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (e.Key == Key.Y)
        {
            AcceptUpdate();
            e.Handled = true;
        }
        else if (e.Key is Key.N or Key.Escape)
        {
            DeclineUpdate();
            e.Handled = true;
        }
        else if (e.Key == Key.R)
        {
            _announcer?.Announce(FullUpdateAnnouncement, interrupt: true);
            e.Handled = true;
        }
        else if (e.Key is Key.Enter or Key.Return)
        {
            if (Keyboard.FocusedElement == DeclineButton)
            {
                DeclineUpdate();
            }
            else
            {
                AcceptUpdate();
            }
            e.Handled = true;
        }
    }

    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        AcceptUpdate();
    }

    private void DeclineButton_Click(object sender, RoutedEventArgs e)
    {
        DeclineUpdate();
    }

    public void AcceptUpdate()
    {
        if (_handled) return;
        _handled = true;
        try { _announcer?.StopSpeaking(); } catch { }
        _settings.LastAcknowledgedSetupVersion = AppSettings.CurrentSetupGuideVersion;
        _settings.TrySave(out _);
        if (_onAccepted is not null)
        {
            _onAccepted();
        }
        else
        {
            DialogResult = true;
            Close();
        }
    }

    public void DeclineUpdate()
    {
        if (_handled) return;
        _handled = true;
        try { _announcer?.StopSpeaking(); } catch { }
        _settings.LastAcknowledgedSetupVersion = AppSettings.CurrentSetupGuideVersion;
        _settings.TrySave(out _);
        if (_onDeclined is not null)
        {
            _onDeclined();
        }
        else
        {
            DialogResult = false;
            Close();
        }
    }

    private void SetupUpdatePromptDialog_Closing(object? sender, CancelEventArgs e)
    {
        IsClosing = true;
        if (!_handled)
        {
            _handled = true;
            _settings.LastAcknowledgedSetupVersion = AppSettings.CurrentSetupGuideVersion;
            _settings.TrySave(out _);
            _onDeclined?.Invoke();
        }
    }
}
