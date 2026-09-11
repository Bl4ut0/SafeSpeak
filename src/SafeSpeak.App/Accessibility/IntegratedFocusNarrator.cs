using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
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
    private readonly RoutedPropertyChangedEventHandler<double> _valueChangedHandler;
    private readonly SelectionChangedEventHandler _selectionChangedHandler;
    private readonly RoutedEventHandler _toggleChangedHandler;
    private readonly TextCompositionEventHandler _textInputHandler;
    private readonly Func<bool> _includeDetailedHelp;
    private readonly Func<bool> _typingEchoEnabled;
    private string? _lastAnnouncement;
    private DateTime _lastAnnouncementAt;
    private bool _suppressNextFocus;
    private bool _disposed;

    public void SuppressNextFocusAnnouncement() => _suppressNextFocus = true;

    public IntegratedFocusNarrator(
        Window window,
        ScreenReaderAnnouncer announcer,
        Func<bool>? includeDetailedHelp = null,
        Func<bool>? typingEchoEnabled = null)
    {
        _window = window;
        _announcer = announcer;
        _includeDetailedHelp = includeDetailedHelp ?? (() => true);
        _typingEchoEnabled = typingEchoEnabled ?? (() => false);
        _focusHandler = OnGotKeyboardFocus;
        _valueChangedHandler = OnRangeValueChanged;
        _selectionChangedHandler = OnSelectionChanged;
        _toggleChangedHandler = OnToggleChanged;
        _textInputHandler = OnTextInput;
        _window.AddHandler(Keyboard.GotKeyboardFocusEvent, _focusHandler, handledEventsToo: true);
        _window.AddHandler(RangeBase.ValueChangedEvent, _valueChangedHandler, handledEventsToo: true);
        _window.AddHandler(Selector.SelectionChangedEvent, _selectionChangedHandler, handledEventsToo: true);
        _window.AddHandler(ToggleButton.CheckedEvent, _toggleChangedHandler, handledEventsToo: true);
        _window.AddHandler(ToggleButton.UncheckedEvent, _toggleChangedHandler, handledEventsToo: true);
        _window.AddHandler(TextCompositionManager.TextInputEvent, _textInputHandler, handledEventsToo: true);
    }

    internal static string? Describe(DependencyObject element) =>
        Describe(element, includeListOwnerName: true, includeHelpText: true);

    private static string? Describe(
        DependencyObject element,
        bool includeListOwnerName,
        bool includeHelpText)
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

        if (element is ListBoxItem listBoxItem)
        {
            return DescribeListBoxItem(
                listBoxItem,
                name,
                includeListOwnerName,
                includeHelpText);
        }

        var parts = new List<string> { name };

        switch (element)
        {
            case Label heading when
                AutomationProperties.GetHeadingLevel(heading) != AutomationHeadingLevel.None:
                parts.Add("chapter heading");
                if (includeHelpText) AddDistinct(parts, AutomationProperties.GetHelpText(heading));
                break;

            case TabItem tab:
                parts.Add(tab.IsSelected ? "selected tab" : "tab");
                if (includeHelpText) parts.Add("Use Left and Right Arrow keys to change sections");
                break;

            case CheckBox checkBox:
                parts.Add(checkBox.IsChecked switch
                {
                    true => "checked",
                    false => "not checked",
                    null => "partially checked"
                });
                parts.Add("check box");
                if (includeHelpText) AddDistinct(parts, AutomationProperties.GetHelpText(checkBox));
                break;

            case RadioButton radioButton:
                parts.Add(radioButton.IsChecked == true ? "selected" : "not selected");
                parts.Add("radio button");
                if (includeHelpText) AddDistinct(parts, AutomationProperties.GetHelpText(radioButton));
                break;

            case ComboBox comboBox:
                AddDistinct(parts, GetComboBoxValue(comboBox));
                if (includeHelpText)
                {
                    AddDistinct(parts, AutomationProperties.GetItemStatus(comboBox));
                }
                parts.Add("selection box");
                if (includeHelpText)
                {
                    AddDistinct(
                        parts,
                        GetHelpTextOrDefault(
                            comboBox,
                            "Press Enter or Alt plus Down Arrow to open the choices, then use Arrow keys and Enter to select"));
                }
                break;

            case Slider slider:
                string valueText = slider.Value.ToString("0.##", CultureInfo.CurrentCulture);
                string maxText = slider.Maximum.ToString("0.##", CultureInfo.CurrentCulture);
                parts.Add($"{valueText} of {maxText}");
                parts.Add("slider");
                if (includeHelpText)
                {
                    AddDistinct(
                        parts,
                        GetHelpTextOrDefault(
                            slider,
                            "Use Left and Right Arrow keys to adjust"));
                }
                break;

            case TextBox textBox:
                AddDistinct(parts, textBox.Text);
                parts.Add("edit box");
                if (includeHelpText) AddDistinct(parts, AutomationProperties.GetHelpText(textBox));
                break;

            case ListViewItem:
                parts.Add("list item");
                break;

            case ListView:
                parts.Add("list");
                if (includeHelpText) parts.Add("Use Up and Down Arrow keys to review messages");
                break;

            case ListBox listBox:
                AddDistinct(parts, AutomationProperties.GetItemStatus(listBox));
                parts.Add("list");
                if (includeHelpText)
                {
                    AddDistinct(
                        parts,
                        GetHelpTextOrDefault(
                            listBox,
                            "Use Up and Down Arrow keys to review items"));
                }
                break;

            case Button button:
                AddDistinct(parts, GetControlText(button));
                parts.Add("button");
                if (includeHelpText) AddDistinct(parts, AutomationProperties.GetHelpText(button));
                break;

            case ToggleButton toggleButton:
                parts.Add(toggleButton.IsChecked == true ? "pressed" : "not pressed");
                parts.Add("toggle button");
                if (includeHelpText) AddDistinct(parts, AutomationProperties.GetHelpText(toggleButton));
                break;

            default:
                return null;
        }

        return string.Join(". ", parts) + ".";
    }

    private void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_suppressNextFocus)
        {
            _suppressNextFocus = false;
            return;
        }

        if (!_announcer.IsEnhancedAccessibilityEnabled || e.NewFocus is not DependencyObject element)
        {
            return;
        }

        bool includeListOwnerName = element is not ListBoxItem listBoxItem ||
            ShouldAnnounceListOwner(e.OldFocus as DependencyObject, listBoxItem);
        string? announcement = Describe(
            element,
            includeListOwnerName,
            includeHelpText: _includeDetailedHelp());
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
        _announcer.AnnounceFocus(announcement);
    }

    public void AnnounceCurrentFocus(bool includeHelpText = true)
    {
        if (!_announcer.IsEnhancedAccessibilityEnabled ||
            Keyboard.FocusedElement is not DependencyObject element)
        {
            return;
        }

        string? announcement = Describe(
            element,
            includeListOwnerName: true,
            includeHelpText);
        if (!string.IsNullOrWhiteSpace(announcement))
        {
            _announcer.AnnounceFocus(announcement);
        }
    }

    private void OnRangeValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (e.OriginalSource is Slider slider && slider.IsKeyboardFocusWithin)
        {
            AnnounceChangedControl(slider);
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject element &&
            element is UIElement control &&
            control.IsKeyboardFocusWithin &&
            element is ComboBox or ListBox)
        {
            // SelectionChanged is raised before two-way bindings and bound UIA
            // ItemStatus text have necessarily caught up. Announce after data
            // binding so arrow navigation speaks the new option, not the owner.
            _window.Dispatcher.BeginInvoke(
                () => AnnounceChangedSelection(element),
                DispatcherPriority.DataBind);
        }
    }

    private void AnnounceChangedSelection(DependencyObject element)
    {
        if (!_announcer.IsEnhancedAccessibilityEnabled ||
            element is not UIElement control ||
            !control.IsKeyboardFocusWithin)
        {
            return;
        }

        string itemStatus = CleanAccessKey(AutomationProperties.GetItemStatus(element));
        if (!string.IsNullOrWhiteSpace(itemStatus))
        {
            _announcer.AnnounceFocus(itemStatus);
            return;
        }

        (string value, int index, int count) = element switch
        {
            ComboBox comboBox =>
                (GetComboBoxValue(comboBox), comboBox.SelectedIndex, comboBox.Items.Count),
            ListBox listBox =>
                (GetSelectedItemText(listBox), listBox.SelectedIndex, listBox.Items.Count),
            _ => (string.Empty, -1, 0)
        };
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        string position = index >= 0 && count > 0
            ? $", option {index + 1} of {count}"
            : string.Empty;
        _announcer.AnnounceFocus($"{value}{position}, selected.");
    }

    private void OnToggleChanged(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is ToggleButton toggle && toggle.IsKeyboardFocusWithin)
        {
            AnnounceChangedControl(toggle);
        }
    }

    private void OnTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!_announcer.IsEnhancedAccessibilityEnabled ||
            !_typingEchoEnabled() ||
            e.OriginalSource is not TextBox textBox ||
            !textBox.IsKeyboardFocusWithin ||
            string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        string spoken = e.Text.All(char.IsWhiteSpace) ? "space" : e.Text;
        _announcer.AnnounceFocus(spoken);
    }

    private void AnnounceChangedControl(DependencyObject element)
    {
        if (!_announcer.IsEnhancedAccessibilityEnabled)
        {
            return;
        }

        string? announcement = Describe(
            element,
            includeListOwnerName: false,
            includeHelpText: false);
        if (!string.IsNullOrWhiteSpace(announcement))
        {
            _announcer.AnnounceFocus(announcement);
        }
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

        if (comboBox.SelectedItem is not null)
        {
            Type itemType = comboBox.SelectedItem.GetType();
            foreach (string propertyName in new[] { "DisplayName", "Name", "Title", "Id" })
            {
                string? value = itemType
                    .GetProperty(propertyName)?
                    .GetValue(comboBox.SelectedItem)?
                    .ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return string.Empty;
    }

    private static string GetSelectedItemText(ListBox listBox)
    {
        if (listBox.SelectedItem is ListBoxItem item)
        {
            return CleanAccessKey(item.Content?.ToString() ?? string.Empty);
        }

        return CleanAccessKey(listBox.SelectedItem?.ToString() ?? string.Empty);
    }

    private static string DescribeListBoxItem(
        ListBoxItem item,
        string itemName,
        bool includeOwnerName,
        bool includeHelpText)
    {
        var parts = new List<string>();
        ItemsControl? owner = ItemsControl.ItemsControlFromItemContainer(item);
        if (owner is not null)
        {
            if (includeOwnerName)
            {
                AddDistinct(parts, AutomationProperties.GetName(owner));
            }
            int index = owner.ItemContainerGenerator.IndexFromContainer(item);
            string position = index >= 0
                ? $"{itemName}, option {index + 1} of {owner.Items.Count}"
                : itemName;
            AddDistinct(parts, position);
            parts.Add(item.IsSelected ? "selected" : "not selected");
            if (includeHelpText) AddDistinct(parts, AutomationProperties.GetHelpText(owner));
        }
        else
        {
            AddDistinct(parts, itemName);
            parts.Add(item.IsSelected ? "selected list item" : "list item");
        }

        return string.Join(". ", parts) + ".";
    }

    private static bool ShouldAnnounceListOwner(
        DependencyObject? previousFocus,
        ListBoxItem nextItem)
    {
        ItemsControl? nextOwner = ItemsControl.ItemsControlFromItemContainer(nextItem);
        if (nextOwner is null)
        {
            return true;
        }

        if (ReferenceEquals(previousFocus, nextOwner))
        {
            return false;
        }

        return previousFocus is not ListBoxItem previousItem ||
            !ReferenceEquals(
                ItemsControl.ItemsControlFromItemContainer(previousItem),
                nextOwner);
    }

    private static string GetHelpTextOrDefault(
        DependencyObject element,
        string defaultText)
    {
        string helpText = CleanAccessKey(AutomationProperties.GetHelpText(element));
        return string.IsNullOrWhiteSpace(helpText) ? defaultText : helpText;
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
        _window.RemoveHandler(RangeBase.ValueChangedEvent, _valueChangedHandler);
        _window.RemoveHandler(Selector.SelectionChangedEvent, _selectionChangedHandler);
        _window.RemoveHandler(ToggleButton.CheckedEvent, _toggleChangedHandler);
        _window.RemoveHandler(ToggleButton.UncheckedEvent, _toggleChangedHandler);
        _window.RemoveHandler(TextCompositionManager.TextInputEvent, _textInputHandler);
        _disposed = true;
    }
}
