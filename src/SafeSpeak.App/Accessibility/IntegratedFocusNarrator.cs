using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SafeSpeak.Core.Accessibility;

namespace SafeSpeak.App.Accessibility;

/// <summary>
/// Gives the optional SafeSpeak reader the focus narration normally provided by
/// a full screen reader. Windows Narrator, NVDA, and JAWS continue to use the
/// standard UI Automation tree independently of this helper.
/// </summary>
public sealed class IntegratedFocusNarrator : IDisposable
{
    private readonly Window _window;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly KeyboardFocusChangedEventHandler _focusHandler;
    private string? _lastAnnouncement;
    private DateTime _lastAnnouncementAt;
    private bool _disposed;

    public IntegratedFocusNarrator(Window window, ScreenReaderAnnouncer announcer)
    {
        _window = window;
        _announcer = announcer;
        _focusHandler = OnGotKeyboardFocus;
        _window.AddHandler(Keyboard.GotKeyboardFocusEvent, _focusHandler, handledEventsToo: true);
    }

    internal static string? Describe(DependencyObject element)
    {
        string name = CleanAccessKey(AutomationProperties.GetName(element));
        if (string.IsNullOrWhiteSpace(name))
        {
            name = GetControlText(element);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var parts = new List<string> { name };

        switch (element)
        {
            case TabItem tab:
                parts.Add(tab.IsSelected ? "selected tab" : "tab");
                parts.Add("Use Left and Right Arrow keys to change sections");
                break;

            case CheckBox checkBox:
                parts.Add(checkBox.IsChecked switch
                {
                    true => "checked",
                    false => "not checked",
                    null => "partially checked"
                });
                parts.Add("check box");
                break;

            case RadioButton radioButton:
                parts.Add(radioButton.IsChecked == true ? "selected" : "not selected");
                parts.Add("radio button");
                break;

            case ComboBox comboBox:
                AddDistinct(parts, GetComboBoxValue(comboBox));
                parts.Add("selection box");
                parts.Add("Use Up and Down Arrow keys to change the selection");
                break;

            case Slider slider:
                parts.Add(slider.Value.ToString("0.##", CultureInfo.CurrentCulture));
                parts.Add("slider");
                parts.Add("Use Left and Right Arrow keys to adjust");
                break;

            case TextBox:
                parts.Add("edit box");
                break;

            case ListViewItem:
                parts.Add("list item");
                break;

            case ListBoxItem:
                parts.Add("list item");
                break;

            case ListView:
                parts.Add("list");
                parts.Add("Use Up and Down Arrow keys to review messages");
                break;

            case ListBox:
                parts.Add("list");
                parts.Add("Use Up and Down Arrow keys to review items");
                break;

            case Button button:
                AddDistinct(parts, GetControlText(button));
                parts.Add("button");
                break;

            case ToggleButton toggleButton:
                parts.Add(toggleButton.IsChecked == true ? "pressed" : "not pressed");
                parts.Add("toggle button");
                break;

            default:
                return null;
        }

        return string.Join(". ", parts) + ".";
    }

    private void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_announcer.IsEnhancedAccessibilityEnabled || e.NewFocus is not DependencyObject element)
        {
            return;
        }

        string? announcement = Describe(element);
        if (string.IsNullOrWhiteSpace(announcement))
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        if (string.Equals(announcement, _lastAnnouncement, StringComparison.Ordinal) &&
            now - _lastAnnouncementAt < TimeSpan.FromMilliseconds(250))
        {
            return;
        }

        _lastAnnouncement = announcement;
        _lastAnnouncementAt = now;
        bool persistCache = element is Button or CheckBox or RadioButton or TabItem or
            Slider or ListView or ListBox;
        _announcer.AnnounceFocus(announcement, persistCache);
    }

    private static string GetControlText(DependencyObject element)
    {
        object? content = element switch
        {
            HeaderedContentControl headered => headered.Header,
            ContentControl contentControl => contentControl.Content,
            _ => null
        };

        return CleanAccessKey(content switch
        {
            string text => text,
            AccessText accessText => accessText.Text,
            TextBlock textBlock => textBlock.Text,
            _ => string.Empty
        });
    }

    private static string GetComboBoxValue(ComboBox comboBox)
    {
        if (!string.IsNullOrWhiteSpace(comboBox.Text))
        {
            return comboBox.Text;
        }

        if (comboBox.SelectedItem is ComboBoxItem comboBoxItem)
        {
            return comboBoxItem.Content?.ToString() ?? string.Empty;
        }

        if (comboBox.SelectedItem is not null && !string.IsNullOrWhiteSpace(comboBox.DisplayMemberPath))
        {
            return comboBox.SelectedItem.GetType()
                .GetProperty(comboBox.DisplayMemberPath)?
                .GetValue(comboBox.SelectedItem)?
                .ToString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static void AddDistinct(ICollection<string> parts, string candidate)
    {
        candidate = CleanAccessKey(candidate);
        if (string.IsNullOrWhiteSpace(candidate) ||
            parts.Any(part => string.Equals(part, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        parts.Add(candidate);
    }

    private static string CleanAccessKey(string? text) =>
        string.IsNullOrWhiteSpace(text) ? string.Empty : text.Replace("_", string.Empty).Trim();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _window.RemoveHandler(Keyboard.GotKeyboardFocusEvent, _focusHandler);
        _disposed = true;
    }
}
