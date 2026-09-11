using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SafeSpeak.App.Accessibility;
using SafeSpeak.App.ViewModels;
using SafeSpeak.Core.Accessibility;
using SafeSpeak.Core.Connectors;

namespace SafeSpeak.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly GlobalHotkeyService _hotkeyService = new();
    private nint _windowHandle;
    private HwndSource? _hwndSource;
    private IntegratedFocusNarrator? _focusNarrator;
    private bool _shutdownCleanupStarted;
    private bool _globalShortcutGroupActive;
    private bool _hotkeysSuspendedForShortcutCapture;
    private GlobalShortcutEditorViewModel? _capturingShortcutEditor;
    private string? _firstCapturedShortcut;
    private ModifierKeys _shortcutCaptureModifiers;
    private string? _shortcutCaptureKeyName;
    private bool _shortcutTriggerReleased;
    private LiveConnectorViewModel? _capturingConnector;
    private string? _firstConnectorUsername;
    private LiveConnectorViewModel? _managingConnector;
    private bool _editingConnectorConfiguration;

    public MainWindow()
    {
        InitializeComponent();
        if (DataContext is MainViewModel vm)
        {
            _focusNarrator = new IntegratedFocusNarrator(
                this,
                vm.Announcer,
                () => vm.NarrateDetailedHelp,
                () => vm.NarrateTypedCharacters);
            vm.GlobalShortcutsChanged += ViewModel_GlobalShortcutsChanged;
        }
        Closing += (s, e) =>
        {
            SafeSpeak.Core.Logging.AppLogger.LogInformation("MainWindow", $"MainWindow Closing event triggered (Cancel={e.Cancel}).");
            MainWindow_Closing(s, e);
        };
        Closed += (_, _) =>
        {
            SafeSpeak.Core.Logging.AppLogger.LogInformation("MainWindow", "MainWindow Closed event triggered.");
        };
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        PreviewMouseWheel += MainWindow_PreviewMouseWheel;
        Loaded += (_, _) =>
        {
            SafeSpeak.Core.Logging.AppLogger.LogInformation("MainWindow", "MainWindow Loaded event triggered.");
            Dispatcher.BeginInvoke(() =>
            {
                HearStatusButton.Focus();
                Keyboard.Focus(HearStatusButton);
            }, DispatcherPriority.Input);
        };
    }

    private void ThemeSelector_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ListBox selector || selector.Items.Count == 0)
        {
            return;
        }

        int nextIndex = e.Key switch
        {
            Key.Left or Key.Up => Math.Max(0, selector.SelectedIndex - 1),
            Key.Right or Key.Down => Math.Min(selector.Items.Count - 1, selector.SelectedIndex + 1),
            Key.Home => 0,
            Key.End => selector.Items.Count - 1,
            _ => -1
        };
        if (nextIndex < 0)
        {
            return;
        }

        selector.SelectedIndex = nextIndex;
        selector.ScrollIntoView(selector.SelectedItem);
        e.Handled = true;
    }

    private async void SettingsConnectorCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: LiveConnectorViewModel connector } ||
            DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (connector.IsConfigured)
        {
            BeginInlineConnectorManagement(connector);
            return;
        }

        if (
            string.Equals(connector.Id, TikTokLiveConnector.ConnectorDescriptor.Id, StringComparison.OrdinalIgnoreCase))
        {
            BeginInlineConnectorCapture(connector, editing: false);
            return;
        }

        CancelInlineConnectorSurfaces();
        await viewModel.ToggleConnectorConfigurationAsync(connector);
        FocusSettingsConnectorCard(connector);
    }

    private void PositionInlineConnectorPanels(bool forEnabledConnector)
    {
        if (Chapter1ConnectorsPanel is null ||
            InlineConnectorManagementPanel is null ||
            InlineConnectorCapturePanel is null ||
            DisabledConnectorsBorder is null)
        {
            return;
        }

        Chapter1ConnectorsPanel.Children.Remove(InlineConnectorManagementPanel);
        Chapter1ConnectorsPanel.Children.Remove(InlineConnectorCapturePanel);

        int disabledIndex = Chapter1ConnectorsPanel.Children.IndexOf(DisabledConnectorsBorder);
        if (disabledIndex >= 0)
        {
            if (forEnabledConnector)
            {
                Chapter1ConnectorsPanel.Children.Insert(disabledIndex, InlineConnectorManagementPanel);
                Chapter1ConnectorsPanel.Children.Insert(disabledIndex + 1, InlineConnectorCapturePanel);
            }
            else
            {
                Chapter1ConnectorsPanel.Children.Insert(disabledIndex + 1, InlineConnectorManagementPanel);
                Chapter1ConnectorsPanel.Children.Insert(disabledIndex + 2, InlineConnectorCapturePanel);
            }
        }
        else
        {
            Chapter1ConnectorsPanel.Children.Add(InlineConnectorManagementPanel);
            Chapter1ConnectorsPanel.Children.Add(InlineConnectorCapturePanel);
        }
    }

    private void BeginInlineConnectorManagement(LiveConnectorViewModel connector)
    {
        PositionInlineConnectorPanels(forEnabledConnector: true);
        CancelInlineConnectorSurfaces();
        _managingConnector = connector;
        InlineConnectorManagementHeading.Text = $"Connector options: {connector.DisplayName}";
        InlineConnectorManagementPrompt.Text =
            $"{connector.DisplayName} is enabled. Choose Edit to review its configuration, Delete to disable it, or Cancel.";
        InlineConnectorManagementPanel.Visibility = Visibility.Visible;
        InlineConnectorManagementPanel.BringIntoView();
        Dispatcher.BeginInvoke(
            () => FocusElement(InlineConnectorEditButton),
            DispatcherPriority.Input);
    }

    private void BeginInlineConnectorCapture(
        LiveConnectorViewModel connector,
        bool editing)
    {
        PositionInlineConnectorPanels(forEnabledConnector: editing || connector.IsConfigured);
        CloseInlineConnectorManagement();
        _capturingConnector = connector;
        _firstConnectorUsername = null;
        _editingConnectorConfiguration = editing;
        InlineConnectorUsernameTextBox.Clear();
        InlineConnectorCapturePrompt.Text =
            "Enter the TikTok username without the at sign, then press Enter.";
        InlineConnectorCaptureStatus.Text =
            "Waiting for the first username entry.";
        InlineConnectorCapturePanel.Visibility = Visibility.Visible;
        InlineConnectorCapturePanel.BringIntoView();
        Dispatcher.BeginInvoke(() =>
        {
            FocusElement(InlineConnectorUsernameTextBox);
        }, DispatcherPriority.Input);
    }

    private void InlineConnectorEditButton_Click(object sender, RoutedEventArgs e)
    {
        if (_managingConnector is not { } connector ||
            DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (string.Equals(
                connector.Id,
                TikTokLiveConnector.ConnectorDescriptor.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            BeginInlineConnectorCapture(connector, editing: true);
            return;
        }

        CloseInlineConnectorManagement();
        viewModel.AnnounceState(
            "TikFinity has no additional configuration fields. It remains enabled. Focus returned to TikFinity.",
            interrupt: true);
        FocusSettingsConnectorCard(connector);
    }

    private async void InlineConnectorDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_managingConnector is not { } connector ||
            DataContext is not MainViewModel viewModel)
        {
            return;
        }

        CloseInlineConnectorManagement();
        await viewModel.ToggleConnectorConfigurationAsync(connector);
        FocusSettingsConnectorCard(connector);
    }

    private void InlineConnectorManagementCancelButton_Click(object sender, RoutedEventArgs e)
        => CancelInlineConnectorManagement(announce: true);

    private void CancelInlineConnectorManagement(bool announce)
    {
        LiveConnectorViewModel? connector = _managingConnector;
        CloseInlineConnectorManagement();
        if (connector is null)
        {
            return;
        }

        if (announce && DataContext is MainViewModel viewModel)
        {
            viewModel.AnnounceState(
                $"Connector options closed for {connector.DisplayName}. Nothing was changed.",
                interrupt: true);
        }
        FocusSettingsConnectorCard(connector);
    }

    private void CloseInlineConnectorManagement()
    {
        InlineConnectorManagementPanel.Visibility = Visibility.Collapsed;
        _managingConnector = null;
    }

    private async void InlineConnectorUsernameTextBox_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelInlineConnectorCapture(announce: true);
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Enter or Key.Return) ||
            _capturingConnector is not { } connector ||
            DataContext is not MainViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        if (!TikTokLiveConnector.TryNormalizeUsername(
                InlineConnectorUsernameTextBox.Text,
                out string username))
        {
            SetInlineConnectorStatus(
                "That username cannot be used. Enter 2 to 24 letters, numbers, periods, or underscores, without the at sign.");
            InlineConnectorUsernameTextBox.SelectAll();
            return;
        }

        if (_firstConnectorUsername is null)
        {
            _firstConnectorUsername = username;
            InlineConnectorUsernameTextBox.Clear();
            InlineConnectorCapturePrompt.Text =
                "Enter the same TikTok username again, then press Enter to verify and save.";
            SetInlineConnectorStatus(
                $"First entry captured as {username}. Enter the same username again and press Enter.");
            return;
        }

        if (!string.Equals(_firstConnectorUsername, username, StringComparison.OrdinalIgnoreCase))
        {
            InlineConnectorUsernameTextBox.Clear();
            SetInlineConnectorStatus(
                $"The usernames did not match. The first entry was {_firstConnectorUsername}. Enter that username again and press Enter, or press Escape to cancel.");
            return;
        }

        InlineConnectorUsernameTextBox.IsEnabled = false;
        SetInlineConnectorStatus($"Verified {username}. Saving TikTok Direct now.");
        bool saved;
        try
        {
            saved = await viewModel.ConfigureTikTokDirectAsync(username);
        }
        catch (Exception ex)
        {
            saved = false;
            viewModel.AnnounceState(
                $"TikTok Direct could not be configured. {ex.Message}",
                interrupt: true);
        }
        finally
        {
            InlineConnectorUsernameTextBox.IsEnabled = true;
        }

        bool wasEditing = _editingConnectorConfiguration;
        CloseInlineConnectorCapture();
        viewModel.AnnounceState(saved
            ? wasEditing
                ? "TikTok Direct username verified and saved. TikTok Direct remains in the Enabled connectors area. Focus returned to TikTok Direct."
                : "TikTok Direct username verified and saved. TikTok Direct moved to the Enabled connectors area. Focus returned to TikTok Direct."
            : "TikTok Direct could not be saved. Review the announced error and try again.",
            interrupt: true);
        FocusSettingsConnectorCard(connector);
    }

    private void InlineConnectorCancelButton_Click(object sender, RoutedEventArgs e) =>
        CancelInlineConnectorCapture(announce: true);

    private void CancelInlineConnectorCapture(bool announce)
    {
        LiveConnectorViewModel? connector = _capturingConnector;
        if (connector is null)
        {
            return;
        }

        CloseInlineConnectorCapture();
        if (announce && DataContext is MainViewModel viewModel)
        {
            viewModel.AnnounceState(
                "TikTok Direct configuration cancelled. Nothing was changed.",
                interrupt: true);
        }
        FocusSettingsConnectorCard(connector);
    }

    private void CloseInlineConnectorCapture()
    {
        InlineConnectorCapturePanel.Visibility = Visibility.Collapsed;
        InlineConnectorUsernameTextBox.Clear();
        _capturingConnector = null;
        _firstConnectorUsername = null;
        _editingConnectorConfiguration = false;
    }

    private void CancelInlineConnectorSurfaces()
    {
        CloseInlineConnectorCapture();
        CloseInlineConnectorManagement();
    }

    private void SetInlineConnectorStatus(string message)
    {
        InlineConnectorCaptureStatus.Text = message;
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Announcer.AnnounceFocus(message);
        }
    }

    private void FocusSettingsConnectorCard(LiveConnectorViewModel connector)
    {
        Dispatcher.BeginInvoke(() =>
        {
            Button? card = EnumerateVisualDescendants(SettingsPanel)
                .OfType<Button>()
                .FirstOrDefault(button =>
                    Equals(button.Tag, "SettingsConnectorCard") &&
                    ReferenceEquals(button.DataContext, connector));
            if (card is not null)
            {
                card.BringIntoView();
                FocusElement(card);
            }
        }, DispatcherPriority.Loaded);
    }

    private void GlobalShortcutGroupEntry_Click(object sender, RoutedEventArgs e) =>
        EnterGlobalShortcutGroup(focusFirstAction: true);

    private void EnterGlobalShortcutGroup(bool focusFirstAction)
    {
        _globalShortcutGroupActive = true;
        ExitGlobalShortcutGroupButton.Visibility = Visibility.Visible;
        ExitGlobalShortcutGroupButton.IsTabStop = true;
        Button[] actionButtons = ShortcutActionButtons().ToArray();
        foreach (Button button in actionButtons)
        {
            button.IsTabStop = true;
        }

        SetShortcutGroupStatus(
            "Keybind group entered. Action boxes are now tabbable. Use Tab or Arrow keys to navigate. Press Enter to edit an action. Press Escape or choose Exit keybind group when finished.");
        if (focusFirstAction && actionButtons.FirstOrDefault() is Button first)
        {
            Dispatcher.BeginInvoke(() => FocusElement(first), DispatcherPriority.Input);
        }
    }

    private void ExitGlobalShortcutGroupButton_Click(object sender, RoutedEventArgs e) =>
        ExitGlobalShortcutGroup();

    private void ExitGlobalShortcutGroup()
    {
        DeactivateGlobalShortcutGroup(
            announce: true,
            restoreEntryFocus: true,
            "Keybind group exited. Focus returned to the Enter keybind group button. Tab continues through Settings.");
    }

    private void DeactivateGlobalShortcutGroup(
        bool announce,
        bool restoreEntryFocus,
        string status)
    {
        CancelInlineShortcutCapture(announce: false, restoreActionFocus: false);
        _globalShortcutGroupActive = false;
        foreach (Button button in ShortcutActionButtons())
        {
            button.IsTabStop = false;
        }

        ExitGlobalShortcutGroupButton.IsTabStop = false;
        ExitGlobalShortcutGroupButton.Visibility = Visibility.Collapsed;
        if (announce)
        {
            SetShortcutGroupStatus(status);
        }
        else
        {
            GlobalShortcutGroupStatus.Text = status;
        }

        if (restoreEntryFocus)
        {
            Dispatcher.BeginInvoke(
                () => FocusElement(GlobalShortcutGroupEntry),
                DispatcherPriority.Input);
        }
    }

    private void GlobalShortcutNavigationGroup_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ExitGlobalShortcutGroup();
            e.Handled = true;
            return;
        }

        if (_capturingShortcutEditor is not null ||
            e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End) ||
            Keyboard.FocusedElement is not Button focused ||
            !Equals(focused.Tag, "GlobalShortcutActionCard"))
        {
            return;
        }

        Button[] buttons = ShortcutActionButtons().ToArray();
        int current = Array.IndexOf(buttons, focused);
        if (current < 0)
        {
            return;
        }

        int target = e.Key switch
        {
            Key.Left => current - 1,
            Key.Right => current + 1,
            Key.Up => current - 2,
            Key.Down => current + 2,
            Key.Home => 0,
            Key.End => buttons.Length - 1,
            _ => current
        };
        target = Math.Clamp(target, 0, buttons.Length - 1);
        if (target != current)
        {
            buttons[target].BringIntoView();
            FocusElement(buttons[target]);
        }

        e.Handled = true;
    }

    private void GlobalShortcutActionCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: GlobalShortcutEditorViewModel editor })
        {
            return;
        }

        if (!_globalShortcutGroupActive)
        {
            EnterGlobalShortcutGroup(focusFirstAction: false);
        }

        BeginInlineShortcutCapture(editor);
    }

    private void BeginInlineShortcutCapture(GlobalShortcutEditorViewModel editor)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.SelectedGlobalShortcut = editor;
        _hotkeyService.UnregisterHotkeys();
        _hotkeysSuspendedForShortcutCapture = true;
        _capturingShortcutEditor = editor;
        _firstCapturedShortcut = null;
        ResetShortcutCaptureKeys();

        InlineShortcutCaptureHeading.Text = $"Change shortcut: {editor.DisplayName}";
        InlineShortcutCaptureDescription.Text = editor.Description;
        InlinePendingShortcutText.Text = "Press the shortcut once";
        InlineShortcutCapturePanel.Visibility = Visibility.Visible;
        SetInlineShortcutStatus(
            $"Listening for {editor.DisplayName}. Press and release the complete shortcut combination once.");
        InlineShortcutCapturePanel.BringIntoView();
        Dispatcher.BeginInvoke(
            () => FocusElement(InlineShortcutCaptureButton),
            DispatcherPriority.Input);
    }

    private void InlineShortcutCaptureButton_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys keyModifier = GetShortcutModifier(key);
        if (keyModifier != ModifierKeys.None)
        {
            _shortcutCaptureModifiers |= keyModifier;
            e.Handled = true;
            return;
        }

        ModifierKeys modifiers = Keyboard.Modifiers;
        if (key == Key.Tab &&
            _shortcutCaptureModifiers == ModifierKeys.None &&
            modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            return;
        }

        if (!TryGetShortcutKeyName(key, out string keyName))
        {
            SetInlineShortcutStatus(
                $"{key} cannot be used as a global shortcut trigger. Try another combination.");
            e.Handled = true;
            return;
        }

        if (_shortcutCaptureKeyName is not null &&
            !string.Equals(_shortcutCaptureKeyName, keyName, StringComparison.OrdinalIgnoreCase))
        {
            SetInlineShortcutStatus(
                $"Only one trigger key can be used. Release every held key, then try the complete shortcut again.");
            e.Handled = true;
            return;
        }

        _shortcutCaptureModifiers |= modifiers;
        _shortcutCaptureKeyName = keyName;
        _shortcutTriggerReleased = false;
        e.Handled = true;
    }

    private void InlineShortcutCaptureButton_PreviewKeyUp(
        object sender,
        KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        ModifierKeys releasedModifier = GetShortcutModifier(key);
        if (releasedModifier != ModifierKeys.None)
        {
            _shortcutCaptureModifiers |= releasedModifier;
        }
        else if (_shortcutCaptureKeyName is not null &&
                 TryGetShortcutKeyName(key, out string releasedKeyName) &&
                 string.Equals(_shortcutCaptureKeyName, releasedKeyName, StringComparison.OrdinalIgnoreCase))
        {
            _shortcutTriggerReleased = true;
        }

        ModifierKeys remainingModifiers = releasedModifier == ModifierKeys.None
            ? Keyboard.Modifiers
            : Keyboard.Modifiers & ~releasedModifier;
        bool chordIsComplete = remainingModifiers == ModifierKeys.None &&
            ((_shortcutCaptureKeyName is not null && _shortcutTriggerReleased) ||
             (_shortcutCaptureKeyName is null && _shortcutCaptureModifiers != ModifierKeys.None));
        if (chordIsComplete)
        {
            string candidate = FormatShortcutGesture(
                _shortcutCaptureModifiers,
                _shortcutCaptureKeyName);
            ResetShortcutCaptureKeys();
            ProcessShortcutCapture(candidate);
        }

        e.Handled = true;
    }

    private void InlineShortcutCaptureButton_LostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e) => ResetShortcutCaptureKeys();

    private void ProcessShortcutCapture(string candidate)
    {
        if (!GlobalShortcutGesture.TryParse(
                candidate,
                out GlobalShortcutGesture parsed,
                out string error))
        {
            SetInlineShortcutStatus($"That shortcut cannot be used. {error}");
            return;
        }

        string normalized = parsed.DisplayText;
        string spoken = SpeakableShortcut(normalized);
        string chapterWarning = IsChapterNavigationGesture(normalized)
            ? " This will override the matching SafeSpeak chapter-navigation shortcut while this global binding is enabled."
            : string.Empty;
        if (_firstCapturedShortcut is null)
        {
            _firstCapturedShortcut = normalized;
            InlinePendingShortcutText.Text = $"First entry: {normalized}. Repeat it to confirm.";
            SetInlineShortcutStatus(
                $"Captured {spoken}.{chapterWarning} Now press the same complete shortcut again to confirm it.");
            return;
        }

        if (!string.Equals(_firstCapturedShortcut, normalized, StringComparison.OrdinalIgnoreCase))
        {
            SetInlineShortcutStatus(
                $"The confirmation did not match. First entry was {SpeakableShortcut(_firstCapturedShortcut)}; second entry was {spoken}. Press {SpeakableShortcut(_firstCapturedShortcut)} again, or choose Cancel and start over.");
            return;
        }

        InlinePendingShortcutText.Text = $"Verified: {normalized}";
        SetInlineShortcutStatus(
            $"Verified {spoken}.{chapterWarning} Saving now.");
        CommitInlineShortcut(normalized, enabled: true);
    }

    private void InlineClearShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        CommitInlineShortcut(string.Empty, enabled: false);
    }

    private void CommitInlineShortcut(string gesture, bool enabled)
    {
        if (_capturingShortcutEditor is not { } editor ||
            DataContext is not MainViewModel viewModel)
        {
            return;
        }

        string previousGesture = editor.Gesture;
        bool previousEnabled = editor.IsEnabled;
        bool wasAlreadySaved = previousEnabled == enabled &&
            string.Equals(previousGesture, gesture, StringComparison.OrdinalIgnoreCase);
        editor.Gesture = gesture;
        editor.IsEnabled = enabled && !string.IsNullOrEmpty(gesture);
        editor.Status = editor.IsEnabled
            ? $"Verified {editor.Gesture}; saving and registering with Windows."
            : "Verified cleared and disabled; saving now.";

        if (!viewModel.TryApplyGlobalShortcuts())
        {
            editor.Gesture = previousGesture;
            editor.IsEnabled = previousEnabled;
            SetInlineShortcutStatus(
                $"The shortcut for {editor.DisplayName} could not be saved. The previously saved shortcut remains active. {viewModel.GlobalShortcutStatus}");
            return;
        }

        bool active = editor.IsEnabled &&
            editor.Status.StartsWith("Active globally:", StringComparison.OrdinalIgnoreCase);
        string result = !editor.IsEnabled
            ? wasAlreadySaved
                ? $"Verified. The shortcut for {editor.DisplayName} was already disabled. Focus returned to {editor.DisplayName} in the keybind menu."
                : $"Verified and saved. The shortcut for {editor.DisplayName} is disabled. Focus returned to {editor.DisplayName} in the keybind menu."
            : active
                ? wasAlreadySaved
                    ? $"Verified. {SpeakableShortcut(editor.Gesture)} is already saved for {editor.DisplayName} and is active system-wide. Focus returned to {editor.DisplayName} in the keybind menu."
                    : $"Verified and saved {SpeakableShortcut(editor.Gesture)} for {editor.DisplayName}. The shortcut is active system-wide. Focus returned to {editor.DisplayName} in the keybind menu."
                : $"Verified and saved {SpeakableShortcut(editor.Gesture)} for {editor.DisplayName}, but Windows could not activate it. {editor.Status} Focus returned to {editor.DisplayName} in the keybind menu.";
        CloseInlineShortcutCapture();
        SetShortcutGroupStatus(result);
        Button? action = ShortcutActionButtons()
            .FirstOrDefault(button => ReferenceEquals(button.DataContext, editor));
        if (action is not null)
        {
            Dispatcher.BeginInvoke(() => FocusElement(action), DispatcherPriority.Input);
        }
    }

    private void InlineCancelShortcutButton_Click(object sender, RoutedEventArgs e) =>
        CancelInlineShortcutCapture(announce: true, restoreActionFocus: true);

    private void CancelInlineShortcutCapture(bool announce, bool restoreActionFocus)
    {
        GlobalShortcutEditorViewModel? editor = _capturingShortcutEditor;
        if (editor is null)
        {
            return;
        }

        CloseInlineShortcutCapture();
        if (announce)
        {
            SetShortcutGroupStatus(
                $"Shortcut change cancelled for {editor.DisplayName}. The saved shortcut was not changed.");
        }

        if (restoreActionFocus)
        {
            Button? action = ShortcutActionButtons()
                .FirstOrDefault(button => ReferenceEquals(button.DataContext, editor));
            if (action is not null)
            {
                Dispatcher.BeginInvoke(() => FocusElement(action), DispatcherPriority.Input);
            }
        }
    }

    private void CloseInlineShortcutCapture()
    {
        InlineShortcutCapturePanel.Visibility = Visibility.Collapsed;
        _capturingShortcutEditor = null;
        _firstCapturedShortcut = null;
        ResetShortcutCaptureKeys();
        ResumeGlobalHotkeysAfterShortcutCapture();
    }

    private void ResumeGlobalHotkeysAfterShortcutCapture()
    {
        if (!_hotkeysSuspendedForShortcutCapture)
        {
            return;
        }

        _hotkeysSuspendedForShortcutCapture = false;
        if (DataContext is MainViewModel viewModel && _windowHandle != nint.Zero)
        {
            RegisterCurrentGlobalShortcuts(viewModel, announce: false);
        }
    }

    private IEnumerable<Button> ShortcutActionButtons() =>
        EnumerateVisualDescendants(GlobalShortcutGrid)
            .OfType<Button>()
            .Where(button => Equals(button.Tag, "GlobalShortcutActionCard"));

    private void SetShortcutGroupStatus(string message)
    {
        GlobalShortcutGroupStatus.Text = message;
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Announcer.AnnounceFocus(message);
        }
    }

    private void SetInlineShortcutStatus(string message)
    {
        InlineShortcutCaptureStatus.Text = message;
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Announcer.AnnounceFocus(message);
        }
    }

    private void ResetShortcutCaptureKeys()
    {
        _shortcutCaptureModifiers = ModifierKeys.None;
        _shortcutCaptureKeyName = null;
        _shortcutTriggerReleased = false;
    }

    private static string SpeakableShortcut(string gesture) =>
        gesture.Replace("+", " plus ", StringComparison.Ordinal);

    private static bool IsChapterNavigationGesture(string gesture) =>
        gesture.Length == 5 &&
        gesture.StartsWith("Alt+", StringComparison.OrdinalIgnoreCase) &&
        char.IsAsciiDigit(gesture[4]);

    private static ModifierKeys GetShortcutModifier(Key key) => key switch
    {
        Key.LeftCtrl or Key.RightCtrl => ModifierKeys.Control,
        Key.LeftAlt or Key.RightAlt => ModifierKeys.Alt,
        Key.LeftShift or Key.RightShift => ModifierKeys.Shift,
        Key.LWin or Key.RWin => ModifierKeys.Windows,
        _ => ModifierKeys.None
    };

    private static string FormatShortcutGesture(
        ModifierKeys modifiers,
        string? keyName)
    {
        var parts = new List<string>(5);
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Control");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Windows");
        if (!string.IsNullOrEmpty(keyName)) parts.Add(keyName);
        return string.Join('+', parts);
    }

    private static bool TryGetShortcutKeyName(Key key, out string keyName)
    {
        int keyValue = (int)key;
        if (keyValue >= (int)Key.A && keyValue <= (int)Key.Z)
        {
            keyName = key.ToString();
            return true;
        }

        if (keyValue >= (int)Key.D0 && keyValue <= (int)Key.D9)
        {
            keyName = (keyValue - (int)Key.D0).ToString();
            return true;
        }

        if (keyValue >= (int)Key.NumPad0 && keyValue <= (int)Key.NumPad9)
        {
            keyName = $"Numpad{keyValue - (int)Key.NumPad0}";
            return true;
        }

        if (keyValue >= (int)Key.F1 && keyValue <= (int)Key.F24)
        {
            keyName = key.ToString();
            return true;
        }

        keyName = key switch
        {
            Key.Space => "Space",
            Key.Enter or Key.Return => "Enter",
            Key.Escape => "Escape",
            Key.Tab => "Tab",
            Key.Back => "Backspace",
            Key.Left => "Left",
            Key.Up => "Up",
            Key.Right => "Right",
            Key.Down => "Down",
            Key.Insert => "Insert",
            Key.Delete => "Delete",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.NumLock => "NumLock",
            Key.Scroll => "ScrollLock",
            _ => string.Empty
        };
        return keyName.Length > 0;
    }

    private void ThemeSelector_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not ListBox selector ||
            e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(selector, source) is not ListBoxItem item)
        {
            return;
        }

        object selected = selector.ItemContainerGenerator.ItemFromContainer(item);
        if (selected == DependencyProperty.UnsetValue)
        {
            return;
        }

        selector.SelectedItem = selected;
        selector.Focus();
        e.Handled = true;
    }

    private void SelectionComboBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || sender is not ComboBox comboBox || comboBox.IsDropDownOpen)
        {
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool openRequested = key is Key.Enter or Key.Space or Key.F4 ||
            (key == Key.Down && (Keyboard.Modifiers & ModifierKeys.Alt) != 0);
        if (openRequested)
        {
            comboBox.IsDropDownOpen = true;
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers &
             (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0 &&
            key is Key.Left or Key.Right or Key.Up or Key.Down or
                Key.Home or Key.End or Key.PageUp or Key.PageDown)
        {
            // A focused, collapsed selector is read-only. This prevents Tab
            // followed by an exploratory Arrow key from silently changing a
            // consequential setting. Open the list before navigating choices.
            if (DataContext is MainViewModel vm)
            {
                vm.Announcer.AnnounceFocus("Press Enter to open the list before choosing.");
            }

            e.Handled = true;
        }
    }

    private void SettingsPanel_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled ||
            e.Key != Key.Tab ||
            (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0 ||
            !SettingsPanel.IsKeyboardFocusWithin)
        {
            return;
        }

        if (_globalShortcutGroupActive &&
            GlobalShortcutNavigationGroup.IsKeyboardFocusWithin)
        {
            // Let the contained cycle manage Tab while the user has explicitly
            // entered the group. Escape or Control+1/2/3/4 exits the group.
            return;
        }

        List<Control> orderedStops = EnumerateVisualDescendants(SettingsPanel)
            .OfType<Control>()
            .Where(control =>
                control.IsVisible &&
                control.IsEnabled &&
                control.Focusable &&
                control.IsTabStop &&
                (KeyboardNavigation.GetTabIndex(control) < int.MaxValue ||
                 control.Tag is "SettingsConnectorCard" or "SettingsConnectorInlineControl"))
            .OrderBy(GetSettingsNavigationOrder)
            .ToList();
        if (orderedStops.Count == 0)
        {
            return;
        }

        DependencyObject? focused = Keyboard.FocusedElement as DependencyObject;
        int currentIndex = orderedStops.FindIndex(control =>
            ReferenceEquals(control, focused) ||
            (focused is not null && control.IsAncestorOf(focused)));
        if (currentIndex < 0)
        {
            return;
        }

        bool reverse = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        int nextIndex = currentIndex + (reverse ? -1 : 1);
        if (nextIndex < 0)
        {
            // The window-level navigation handler returns Shift+Tab from the
            // page entry control to the Settings tab header.
            return;
        }

        if (nextIndex >= orderedStops.Count)
        {
            FocusElement(HearStatusButton);
        }
        else
        {
            Control target = orderedStops[nextIndex];
            target.BringIntoView();
            FocusElement(target);
        }

        e.Handled = true;
    }

    private static int GetSettingsNavigationOrder(Control control)
    {
        if (control.Tag is "SettingsConnectorCard" or "SettingsConnectorInlineControl")
        {
            return 6;
        }

        return KeyboardNavigation.GetTabIndex(control);
    }

    private static IEnumerable<DependencyObject> EnumerateVisualDescendants(
        DependencyObject root)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (DependencyObject descendant in EnumerateVisualDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        ModifierKeys modifiers = Keyboard.Modifiers;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape && _managingConnector is not null)
        {
            CancelInlineConnectorManagement(announce: true);
            e.Handled = true;
            return;
        }
        bool controlOnly = (modifiers & ModifierKeys.Control) != 0 &&
            (modifiers & (ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows)) == 0;
        if (controlOnly && TryHandleDirectNavigationShortcut(key))
        {
            e.Handled = true;
            return;
        }

        bool altOnly = (modifiers & ModifierKeys.Alt) != 0 &&
            (modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Windows)) == 0;
        if (altOnly &&
            _capturingShortcutEditor is null &&
            TryHandleChapterNavigationShortcut(key))
        {
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Tab)
        {
            return;
        }

        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0)
        {
            return;
        }

        bool reverse = (modifiers & ModifierKeys.Shift) != 0;
        IInputElement? focusedElement = Keyboard.FocusedElement;
        if (ReferenceEquals(focusedElement, MainNavigation) ||
            focusedElement is TabItem focusedTab && MainNavigation.Items.Contains(focusedTab))
        {
            FocusElement(reverse ? HearStatusButton : GetSelectedPageEntryControl());
            e.Handled = true;
            return;
        }

        UIElement pageEntryControl = GetSelectedPageEntryControl();
        if (reverse && pageEntryControl.IsKeyboardFocusWithin)
        {
            if (MainNavigation.SelectedItem is TabItem selectedTab)
            {
                FocusElement(selectedTab);
            }
            else
            {
                FocusElement(MainNavigation);
            }

            e.Handled = true;
        }
    }

    private void SelectionComboBox_DropDownOpened(object? sender, EventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            comboBox.Focus();
        }
    }

    private void SettingsChapterJumpCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.SelectedValue is int chapterNumber)
        {
            JumpToChapter(chapterNumber);
        }
    }

    private void JumpToChapter(int chapterNumber)
    {
        Label[] chapters = EnumerateVisualDescendants(GetSelectedPageContent())
            .OfType<Label>()
            .Where(label =>
                label.IsVisible &&
                AutomationProperties.GetHeadingLevel(label) == AutomationHeadingLevel.Level2)
            .ToArray();
        if (chapterNumber >= 1 && chapterNumber <= chapters.Length)
        {
            Label target = chapters[chapterNumber - 1];
            target.BringIntoView();
            Dispatcher.BeginInvoke(() => FocusElement(target), DispatcherPriority.Input);
            if (DataContext is MainViewModel vm)
            {
                vm.Announcer.AnnounceFocus($"Jumped to Chapter {chapterNumber}. {target.Content}");
            }
        }
    }

    private void MainWindow_PreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        // When a ComboBox drop-down is open to navigate its list of choices,
        // do not redirect mouse wheel to the background page scroller.
        if (IsAnyComboBoxDropDownOpen())
        {
            return;
        }

        ScrollViewer? pageScroller = MainNavigation.SelectedIndex switch
        {
            1 => SafetyPageScrollViewer,
            2 => VoicePageScrollViewer,
            3 => SettingsPageScrollViewer,
            _ => null
        };
        if (pageScroller is null)
        {
            return;
        }

        // The wheel always belongs to the active page, not whichever selector,
        // slider, or nested list happens to be under the pointer. Controls are
        // changed only through deliberate click/touch or keyboard interaction.
        int configuredLines = SystemParameters.WheelScrollLines;
        double linesPerNotch = configuredLines < 0 ? 8 : Math.Max(1, configuredLines);
        double distance = (e.Delta / 120.0) * linesPerNotch * 16.0;
        pageScroller.ScrollToVerticalOffset(pageScroller.VerticalOffset - distance);
        e.Handled = true;
    }

    private bool IsAnyComboBoxDropDownOpen()
    {
        return EnumerateVisualDescendants(GetSelectedPageContent())
            .OfType<ComboBox>()
            .Any(c => c.IsDropDownOpen);
    }

    private bool TryHandleDirectNavigationShortcut(Key key)
    {
        int? tabIndex = key switch
        {
            Key.D1 or Key.NumPad1 => 0,
            Key.D2 or Key.NumPad2 => 1,
            Key.D3 or Key.NumPad3 => 2,
            Key.D4 or Key.NumPad4 => 3,
            _ => null
        };
        if (tabIndex is not null)
        {
            CancelInlineConnectorCaptureForNavigation();
            if (_globalShortcutGroupActive)
            {
                DeactivateGlobalShortcutGroup(
                    announce: false,
                    restoreEntryFocus: false,
                    $"Keybind group exited by Control plus {tabIndex.Value + 1}. Any unfinished shortcut capture was cancelled.");
            }

            SelectNavigationTab(tabIndex.Value);
            return true;
        }

        return false;
    }

    private bool TryHandleChapterNavigationShortcut(Key key)
    {
        int? chapterNumber = key switch
        {
            Key.D1 or Key.NumPad1 => 1,
            Key.D2 or Key.NumPad2 => 2,
            Key.D3 or Key.NumPad3 => 3,
            Key.D4 or Key.NumPad4 => 4,
            Key.D5 or Key.NumPad5 => 5,
            Key.D6 or Key.NumPad6 => 6,
            Key.D7 or Key.NumPad7 => 7,
            Key.D8 or Key.NumPad8 => 8,
            Key.D9 or Key.NumPad9 => 9,
            Key.D0 or Key.NumPad0 => 10,
            _ => null
        };
        if (chapterNumber is null)
        {
            return false;
        }

        string gesture = $"Alt+{chapterNumber.Value % 10}";
        MainViewModel? viewModel = DataContext as MainViewModel;
        if (viewModel?.IsGlobalShortcutConfigured(gesture) == true)
        {
            // The user's system-wide action owns this combination. Its WM_HOTKEY
            // handler remains authoritative even while SafeSpeak has focus.
            return false;
        }

        Label[] chapters = EnumerateVisualDescendants(GetSelectedPageContent())
            .OfType<Label>()
            .Where(label =>
                label.IsVisible &&
                AutomationProperties.GetHeadingLevel(label) == AutomationHeadingLevel.Level2)
            .ToArray();
        if (chapterNumber.Value > chapters.Length)
        {
            viewModel?.Announcer.AnnounceFocus(
                $"Chapter {chapterNumber.Value} is not available on this page. This page has {chapters.Length} chapters.");
            return true;
        }

        if (_globalShortcutGroupActive)
        {
            DeactivateGlobalShortcutGroup(
                announce: false,
                restoreEntryFocus: false,
                $"Keybind group exited by Alt plus {chapterNumber.Value % 10}. Any unfinished shortcut capture was cancelled.");
        }

        CancelInlineConnectorCaptureForNavigation();

        Label target = chapters[chapterNumber.Value - 1];
        target.BringIntoView();
        FocusElement(target);
        return true;
    }

    private void CancelInlineConnectorCaptureForNavigation()
    {
        if (_capturingConnector is not null || _managingConnector is not null)
        {
            CancelInlineConnectorSurfaces();
        }
    }

    private void SelectNavigationTab(int index)
    {
        if (index < 0 || index >= MainNavigation.Items.Count)
        {
            return;
        }

        MainNavigation.SelectedIndex = index;
        Dispatcher.BeginInvoke(() =>
        {
            if (MainNavigation.SelectedItem is TabItem selectedTab)
            {
                FocusElement(selectedTab);
            }
        }, DispatcherPriority.Input);
    }

    private UIElement GetSelectedPageContent() =>
        MainNavigation.SelectedContent is UIElement selectedContent
            ? selectedContent
            : MainNavigation;

    private UIElement GetSelectedPageEntryControl() => MainNavigation.SelectedIndex switch
    {
        0 => ArmToggle,
        1 => SafetyModerationChapterHeading,
        2 => VoiceSelectionChapterHeading,
        3 => SettingsSourceChapterHeading,
        _ => ArmToggle
    };

    private void GuidePlacementButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        bool isSafety = button.Name.Contains("Safety", StringComparison.Ordinal);
        bool movedToTop = isSafety
            ? viewModel.AreSafetyGuideControlsAtTop
            : viewModel.AreSettingsGuideControlsAtTop;
        UIElement focusTarget = (isSafety, movedToTop) switch
        {
            (true, true) => ReadModerationGuideButton,
            (true, false) => SafetyModerationChapterHeading,
            (false, true) => SettingsGuideButton,
            _ => SettingsSourceChapterHeading
        };
        string page = isSafety ? "Safety" : "Settings";
        string destination = movedToTop ? "top" : "bottom";
        string focusName = movedToTop
            ? "Play whole guide"
            : isSafety ? "Moderation strength" : "Theme selector";
        string movedButtonName = movedToTop
            ? "Move controls to bottom"
            : "Move controls to top";
        string findInstruction = movedToTop
            ? $"Navigate to the top of the {page} page to find it again."
            : $"Navigate to the bottom of the {page} page to find it again.";

        Dispatcher.BeginInvoke(() =>
        {
            FocusElement(focusTarget);
            viewModel.AnnounceState(
                $"{page} guide controls moved to the {destination} of the page. " +
                $"The button you pressed moved with them and is now labeled {movedButtonName}. " +
                $"{findInstruction} The guide text remains visible. Focus is now on {focusName}.",
                interrupt: true);
        }, DispatcherPriority.ContextIdle);
    }

    private static void FocusElement(UIElement element)
    {
        element.Focus();
        Keyboard.Focus(element);
    }

    private void PlaybackModeButton_Click(object sender, RoutedEventArgs e)
    {
        UIElement target = ReferenceEquals(sender, UseManualPlaybackButton)
            ? SpeakNextApprovedMessageButton
            : PauseOrResumeButton;

        Dispatcher.BeginInvoke(() =>
        {
            if (target.IsVisible && target.IsEnabled)
            {
                FocusElement(target);
            }
        }, DispatcherPriority.Input);
    }

    private void LiveFeedListView_GotKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.PauseLiveFeedReview();
        }
    }

    private void LiveFeedListView_LostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!LiveFeedListView.IsKeyboardFocusWithin &&
                DataContext is MainViewModel viewModel)
            {
                viewModel.ResumeLiveFeedReview();
            }
        }, DispatcherPriority.Input);
    }

    private void LiveFeedListView_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left ||
            e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(LiveFeedListView, source) is not ListViewItem item)
        {
            return;
        }

        item.IsSelected = true;
        e.Handled = ToggleSelectedFilteredFeedEntry();
    }

    private void LiveFeedListView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        e.Handled = ToggleSelectedFilteredFeedEntry();
    }

    private bool ToggleSelectedFilteredFeedEntry()
    {
        if (LiveFeedListView.SelectedItem is not LiveFeedEntryViewModel entry)
        {
            return false;
        }

        if (DataContext is MainViewModel viewModel)
        {
            if (entry.ToggleFilteredContent())
            {
                viewModel.AnnounceState(entry.RevealAnnouncement, interrupt: true);
            }
            else
            {
                viewModel.AnnounceState(
                    "This message was approved, so it has no hidden filtered text.",
                    interrupt: true);
            }

            return true;
        }

        return false;
    }

    private void EmergencyStopButton_IsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (!IsLoaded || e.NewValue is not false) return;

        // Emergency Stop and ordinary disarm both collapse the armed-only
        // controls. Return keyboard focus to the persistent re-arm action
        // instead of leaving it on a control that no longer exists visually.
        Dispatcher.BeginInvoke(() =>
        {
            ArmToggle.Focus();
            Keyboard.Focus(ArmToggle);
        }, DispatcherPriority.Input);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        try
        {
            var helper = new WindowInteropHelper(this);
            _windowHandle = helper.EnsureHandle();
            _hwndSource = HwndSource.FromHwnd(_windowHandle);
            _hwndSource?.AddHook(HwndHook);

            _hotkeyService.HotkeyTriggered += HotkeyService_HotkeyTriggered;
            if (DataContext is MainViewModel registrationViewModel)
            {
                RegisterCurrentGlobalShortcuts(registrationViewModel, announce: false);
            }
        }
        catch { }

        if (DataContext is MainViewModel startupViewModel)
        {
            GlobalShortcutBinding? statusShortcut = startupViewModel
                .GetGlobalShortcutBindings()
                .FirstOrDefault(binding =>
                    binding.Action == HotkeyAction.AnnounceStatus &&
                    binding.IsEnabled);
            string guidance = statusShortcut is null
                ? "SafeSpeak initialized. Global shortcuts can be configured in Settings."
                : $"SafeSpeak initialized. Press {statusShortcut.Gesture} anytime to hear status.";
            startupViewModel.AnnounceState(guidance);
        }
    }

    private void ViewModel_GlobalShortcutsChanged(object? sender, EventArgs e)
    {
        if (sender is MainViewModel viewModel)
        {
            _hotkeysSuspendedForShortcutCapture = false;
            RegisterCurrentGlobalShortcuts(viewModel, announce: true);
        }
    }

    private void RegisterCurrentGlobalShortcuts(
        MainViewModel viewModel,
        bool announce)
    {
        HotkeyRegistrationResult registration = _hotkeyService.RegisterHotkeys(
            _windowHandle,
            viewModel.GetGlobalShortcutBindings());
        viewModel.ReportGlobalShortcutRegistration(
            registration,
            announce || !registration.AllRegistered);
    }

    private void HotkeyService_HotkeyTriggered(object? sender, HotkeyTriggeredEventArgs e)
    {
        Dispatcher.BeginInvoke(async () =>
        {
            if (DataContext is MainViewModel vm)
            {
                await vm.ExecuteGlobalShortcutAsync(e.Action);
            }
        });
    }

    private nint HwndHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        _hotkeyService.ProcessWindowMessage(msg, wParam);
        return nint.Zero;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_shutdownCleanupStarted)
        {
            return;
        }

        _shutdownCleanupStarted = true;
        try
        {
            TryShutdownStep(_hotkeyService.Dispose);
            TryShutdownStep(() => _hwndSource?.RemoveHook(HwndHook));
            TryShutdownStep(() => _focusNarrator?.Dispose());

            if (DataContext is MainViewModel vm)
            {
                vm.GlobalShortcutsChanged -= ViewModel_GlobalShortcutsChanged;
                TryShutdownStep(vm.FlushAllSettingsToDisk);
                // Backend cancellation, native TTS teardown, connector disposal,
                // and disk flushing must never hold the WPF window open. Settings
                // are already persisted as they change, so cleanup is best-effort.
                _ = ObserveCleanupFailureAsync(
                    Task.Run(async () => await vm.DisposeAsync().ConfigureAwait(false)));
            }
        }
        catch
        {
            // Shutdown must remain non-blocking and accessible. Cleanup is
            // best-effort; process exit releases any remaining native model,
            // speech, or audio resources.
        }
    }

    private static async Task ObserveCleanupFailureAsync(Task cleanupTask)
    {
        try { await cleanupTask.ConfigureAwait(false); }
        catch { }
    }

    private static void TryShutdownStep(Action operation)
    {
        try { operation(); }
        catch { }
    }
}
