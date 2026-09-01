using SafeSpeak.Core.AI;
using SafeSpeak.Core.Audio;
using SafeSpeak.Core.Connectors;
using SafeSpeak.Core.Models;
using SafeSpeak.Core.Moderation;
using SafeSpeak.Mobile.Services;

namespace SafeSpeak.Mobile;

public sealed class MainPage : ContentPage
{
    private readonly ModerationPipeline _moderation = new(
        intentClassifier: new HeuristicIntentClassifier());
    private readonly ISpeechOutput _speech = new MobileSpeechOutput();
    private readonly Switch _armedSwitch = new();
    private readonly Editor _messageEditor = new()
    {
        Text = "Welcome to the SafeSpeak mobile test build!",
        AutoSize = EditorAutoSizeOption.TextChanges,
        MinimumHeightRequest = 96,
        MaxLength = 200
    };
    private readonly Label _statusLabel = new()
    {
        Text = "Disarmed. Review a message before enabling speech.",
        FontAttributes = FontAttributes.Bold
    };
    private readonly Button _speakButton = new()
    {
        Text = "Speak approved message",
        IsEnabled = false
    };
    private string? _approvedSpeech;

    public MainPage()
    {
        Title = "SafeSpeak Mobile";

        var heading = new Label
        {
            Text = "SafeSpeak mobile test lab",
            FontSize = 28,
            FontAttributes = FontAttributes.Bold
        };
        SemanticProperties.SetHeadingLevel(heading, SemanticHeadingLevel.Level1);

        var introduction = new Label
        {
            Text = "Test the portable safety pipeline and your phone's built-in text-to-speech. This build starts disarmed and does not connect to a live account."
        };

        var armedLabel = new Label
        {
            Text = "Allow approved test messages to speak",
            VerticalOptions = LayoutOptions.Center
        };
        _armedSwitch.Toggled += OnArmedChanged;
        SemanticProperties.SetDescription(_armedSwitch,
            "Safety switch. Off means no message can be spoken.");

        var reviewButton = new Button { Text = "Review test message" };
        reviewButton.Clicked += OnReviewClicked;
        SemanticProperties.SetDescription(reviewButton,
            "Checks the entered text with SafeSpeak moderation without speaking it.");

        _speakButton.Clicked += OnSpeakClicked;
        var stopButton = new Button { Text = "Stop speech" };
        stopButton.Clicked += OnStopClicked;

        SemanticProperties.SetDescription(_messageEditor,
            "Message to test. Maximum 200 characters.");
        SemanticProperties.SetDescription(_statusLabel,
            "Current moderation and speech status.");

        var connectorHeading = new Label
        {
            Text = "Connector development status",
            FontSize = 22,
            FontAttributes = FontAttributes.Bold
        };
        SemanticProperties.SetHeadingLevel(connectorHeading, SemanticHeadingLevel.Level2);

        var connectorList = new VerticalStackLayout { Spacing = 10 };
        foreach (ConnectorRoadmapItem connector in ConnectorRoadmap.All)
        {
            connectorList.Add(new Label
            {
                Text = $"{connector.DisplayName} — {FormatAvailability(connector.Availability)}\n{connector.Description}"
            });
        }

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(24),
                Spacing = 16,
                Children =
                {
                    heading,
                    introduction,
                    new HorizontalStackLayout
                    {
                        Spacing = 12,
                        Children = { _armedSwitch, armedLabel }
                    },
                    _messageEditor,
                    reviewButton,
                    _speakButton,
                    stopButton,
                    _statusLabel,
                    connectorHeading,
                    connectorList
                }
            }
        };
    }

    private void OnArmedChanged(object? sender, ToggledEventArgs e)
    {
        _speakButton.IsEnabled = e.Value && !string.IsNullOrWhiteSpace(_approvedSpeech);
        _statusLabel.Text = e.Value
            ? "Armed for approved test messages only."
            : "Disarmed. Speech output is blocked.";

        if (!e.Value) _ = _speech.StopAsync();
        SemanticScreenReader.Announce(_statusLabel.Text);
    }

    private async void OnReviewClicked(object? sender, EventArgs e)
    {
        _approvedSpeech = null;
        _speakButton.IsEnabled = false;

        var livestreamEvent = new LivestreamEvent
        {
            Platform = "Offline simulator",
            Type = LivestreamEventType.Chat,
            Author = "mobile_tester",
            AuthorDisplayName = "Mobile tester",
            Text = _messageEditor.Text ?? string.Empty
        };

        try
        {
            ModerationDecision decision = await _moderation.ProcessMessageAsync(
                livestreamEvent.ToChatMessage());
            if (decision.Passed)
            {
                _approvedSpeech = decision.SpokenText;
                _speakButton.IsEnabled = _armedSwitch.IsToggled;
                _statusLabel.Text = "Approved. Arm SafeSpeak, then choose Speak approved message.";
            }
            else
            {
                _statusLabel.Text = $"Not approved. {decision.SafeReasonDescription}";
            }
        }
        catch (Exception)
        {
            _statusLabel.Text = "The safety check could not complete. Nothing will be spoken.";
        }

        SemanticScreenReader.Announce(_statusLabel.Text);
    }

    private async void OnSpeakClicked(object? sender, EventArgs e)
    {
        if (!_armedSwitch.IsToggled || string.IsNullOrWhiteSpace(_approvedSpeech))
        {
            _statusLabel.Text = "Speech blocked. Arm SafeSpeak and approve a message first.";
            SemanticScreenReader.Announce(_statusLabel.Text);
            return;
        }

        _statusLabel.Text = "Speaking with the phone's installed text-to-speech voice.";
        try
        {
            await _speech.SpeakAsync(new SpeechOutputRequest(_approvedSpeech));
            _statusLabel.Text = "Speech complete.";
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Speech stopped.";
        }
        catch (Exception)
        {
            _statusLabel.Text = "The phone's text-to-speech service was unavailable.";
        }

        SemanticScreenReader.Announce(_statusLabel.Text);
    }

    private async void OnStopClicked(object? sender, EventArgs e)
    {
        await _speech.StopAsync();
        _statusLabel.Text = "Speech stopped.";
        SemanticScreenReader.Announce(_statusLabel.Text);
    }

    private static string FormatAvailability(ConnectorAvailability availability) => availability switch
    {
        ConnectorAvailability.Available => "available for offline testing",
        ConnectorAvailability.Planned => "planned",
        ConnectorAvailability.AccessRequired => "official platform access required",
        _ => "not available"
    };
}
