using System;
using System.ComponentModel;
using System.Windows;
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

        PreviewKeyDown += SetupUpdatePromptDialog_PreviewKeyDown;
        Loaded += SetupUpdatePromptDialog_Loaded;
        Closing += SetupUpdatePromptDialog_Closing;
    }

    private void SetupUpdatePromptDialog_Loaded(object sender, RoutedEventArgs e)
    {
        YesButton.Focus();
        Keyboard.Focus(YesButton);

        _announcer?.Announce(
            "SafeSpeak settings update. SafeSpeak has updated with new setup options and features. Press Y to review the setup guide, or press N to keep current settings and continue.",
            interrupt: true);
    }

    private void SetupUpdatePromptDialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
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
        if (!_handled)
        {
            _handled = true;
            _settings.LastAcknowledgedSetupVersion = AppSettings.CurrentSetupGuideVersion;
            _settings.TrySave(out _);
            _onDeclined?.Invoke();
        }
    }
}
