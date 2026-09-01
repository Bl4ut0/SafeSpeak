using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Threading;

namespace SafeSpeak.App.Accessibility;

/// <summary>
/// Raises the WPF UI Automation live-region event whenever bound status text
/// changes. AutomationProperties.LiveSetting alone does not raise this event.
/// </summary>
public static class LiveRegion
{
    public static readonly DependencyProperty AnnouncementProperty =
        DependencyProperty.RegisterAttached(
            "Announcement",
            typeof(string),
            typeof(LiveRegion),
            new PropertyMetadata(string.Empty, OnAnnouncementChanged));

    public static void SetAnnouncement(DependencyObject element, string value) =>
        element.SetValue(AnnouncementProperty, value);

    public static string GetAnnouncement(DependencyObject element) =>
        (string)element.GetValue(AnnouncementProperty);

    private static void OnAnnouncementChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not UIElement element ||
            Equals(args.OldValue, args.NewValue) ||
            string.IsNullOrWhiteSpace(args.NewValue?.ToString()))
        {
            return;
        }

        element.Dispatcher.BeginInvoke(() =>
        {
            AutomationPeer? peer =
                UIElementAutomationPeer.FromElement(element) ??
                UIElementAutomationPeer.CreatePeerForElement(element);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }, DispatcherPriority.Background);
    }
}
