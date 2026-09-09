using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SafeSpeak.App.Services;
using SafeSpeak.Core.AI;
using SafeSpeak.Core.Models;

namespace SafeSpeak.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly Qwen3GuardRuntimeManager _qwenRuntime = new();
    private CancellationTokenSource? _moderationModelInstallCts;
    private int _lastSpokenModelDownloadBucket = -1;

    [ObservableProperty]
    private bool _isInstallingModerationModel;

    [ObservableProperty]
    private double _moderationModelDownloadProgress;

    [ObservableProperty]
    private string _moderationModelDownloadStatus =
        "The optional Qwen3Guard model is not installed.";

    [ObservableProperty]
    private string _moderationModelAccessibleProgress =
        "Optional Qwen3Guard model is not installed.";

    public bool IsQwenModelSelected =>
        SelectedModerationModel == ModerationModelPreference.Qwen3Guard06BCompressed;
    public bool IsQwenModelInstalled => _qwenRuntime.IsModelInstalled;
    public bool CanInstallQwenModel =>
        IsQwenModelSelected && !IsQwenModelInstalled && !IsInstallingModerationModel;
    public bool CanCancelQwenModelInstall => IsInstallingModerationModel;
    public bool CanRemoveQwenModel => IsQwenModelInstalled && !IsInstallingModerationModel;
    public string QwenModelStorageSummary =>
        "Optional download: about 484 megabytes. It is stored only in SafeSpeak's local app data and can be removed here.";

    private IIntentClassifier CreateSelectedIntentClassifier() =>
        IntentClassifierFactory.Create(_settings, _qwenRuntime.EndpointUrl);

    private void RefreshQwenModelProperties()
    {
        OnPropertyChanged(nameof(IsQwenModelSelected));
        OnPropertyChanged(nameof(IsQwenModelInstalled));
        OnPropertyChanged(nameof(CanInstallQwenModel));
        OnPropertyChanged(nameof(CanCancelQwenModelInstall));
        OnPropertyChanged(nameof(CanRemoveQwenModel));
        OnPropertyChanged(nameof(IntentModelStatus));
        OnPropertyChanged(nameof(IntentModelShortStatus));
        OnPropertyChanged(nameof(ModerationModelSelectionAccessibleText));
    }

    private async Task StartInstalledQwenModelAsync()
    {
        if (!IsQwenModelSelected || !IsQwenModelInstalled || _incomingEventCts.IsCancellationRequested)
        {
            return;
        }

        ModerationModelDownloadStatus = "Starting SafeSpeak's private Qwen3Guard model service.";
        bool started = await _qwenRuntime.EnsureStartedAsync(_incomingEventCts.Token);
        if (started && _pipeline.Classifier is Qwen3GuardIntentClassifier classifier)
        {
            await classifier.ClassifyAsync(
                "I enjoy playing this game.",
                _incomingEventCts.Token);
        }
        if (_incomingEventCts.IsCancellationRequested)
        {
            return;
        }

        ModerationModelDownloadStatus = started
            ? "The optional model is installed and its private local service is ready."
            : "The optional model is installed, but its private local service could not start. The built-in filter remains active.";
        RefreshQwenModelProperties();
    }

    [RelayCommand]
    public async Task InstallQwenModel()
    {
        if (!CanInstallQwenModel)
        {
            AnnounceState(IsQwenModelInstalled
                ? "The optional Qwen3Guard model is already installed."
                : "Select Qwen3Guard in the contextual filtering model selector before installing it.");
            return;
        }

        if (!_qwenRuntime.IsRuntimeAvailable)
        {
            ModerationModelDownloadStatus =
                "This build is missing SafeSpeak's packaged local model runtime. The built-in filter remains active.";
            RefreshQwenModelProperties();
            AnnounceState(ModerationModelDownloadStatus);
            return;
        }

        _moderationModelInstallCts?.Dispose();
        _moderationModelInstallCts = CancellationTokenSource.CreateLinkedTokenSource(
            _incomingEventCts.Token);
        IsInstallingModerationModel = true;
        ModerationModelDownloadProgress = 0;
        ModerationModelAccessibleProgress = "Optional model installation started, 0 percent.";
        _lastSpokenModelDownloadBucket = -1;
        RefreshQwenModelProperties();
        AnnounceState(
            "Installing the optional Qwen3Guard model. The download is about 484 megabytes. SafeSpeak and its built-in filtering remain usable. Use Cancel model download to stop.");

        var progress = new Progress<QwenModelInstallProgress>(update =>
        {
            ModerationModelDownloadProgress = update.Percent;
            ModerationModelDownloadStatus = update.Status;

            int bucket = (int)(update.Percent / 10);
            if (bucket > _lastSpokenModelDownloadBucket && bucket is > 0 and < 10)
            {
                _lastSpokenModelDownloadBucket = bucket;
                ModerationModelAccessibleProgress =
                    $"Optional model download {bucket * 10} percent complete.";
                AnnounceState($"Optional model download {bucket * 10} percent complete.");
            }
        });

        try
        {
            await _qwenRuntime.InstallModelAsync(
                progress,
                _moderationModelInstallCts.Token);
            _pipeline.SetIntentClassifier(CreateSelectedIntentClassifier());
            if (_pipeline.Classifier is Qwen3GuardIntentClassifier classifier)
            {
                await classifier.ClassifyAsync(
                    "I enjoy playing this game.",
                    _moderationModelInstallCts.Token);
            }

            ModerationModelDownloadProgress = 100;
            ModerationModelDownloadStatus =
                "Optional Qwen3Guard model installed, verified, and active. The built-in filter remains its safety fallback.";
            ModerationModelAccessibleProgress = ModerationModelDownloadStatus;
            AnnounceState(ModerationModelDownloadStatus);
        }
        catch (OperationCanceledException)
        {
            ModerationModelDownloadStatus =
                "Optional model download canceled. The built-in filter remains active.";
            ModerationModelAccessibleProgress = ModerationModelDownloadStatus;
            AnnounceState(ModerationModelDownloadStatus);
        }
        catch (Exception ex)
        {
            ModerationModelDownloadStatus =
                $"Optional model installation failed: {ex.Message} The built-in filter remains active.";
            ModerationModelAccessibleProgress = ModerationModelDownloadStatus;
            AnnounceState(ModerationModelDownloadStatus);
        }
        finally
        {
            IsInstallingModerationModel = false;
            _moderationModelInstallCts?.Dispose();
            _moderationModelInstallCts = null;
            RefreshQwenModelProperties();
        }
    }

    [RelayCommand]
    public void CancelQwenModelInstall()
    {
        if (!IsInstallingModerationModel)
        {
            AnnounceState("There is no optional model download in progress.");
            return;
        }

        ModerationModelDownloadStatus = "Canceling the optional model download.";
        ModerationModelAccessibleProgress = ModerationModelDownloadStatus;
        AnnounceState(ModerationModelDownloadStatus);
        _moderationModelInstallCts?.Cancel();
    }

    [RelayCommand]
    public async Task RemoveQwenModel()
    {
        if (!CanRemoveQwenModel)
        {
            AnnounceState("The optional Qwen3Guard model is not installed.");
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            "Remove the optional Qwen3Guard model from this computer? SafeSpeak will switch to its built-in enhanced filter.",
            "Remove optional moderation model",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            AnnounceState("Optional model removal canceled.");
            return;
        }

        SelectedModerationModel = ModerationModelPreference.BuiltInHybrid;
        ModerationModelDownloadStatus = "Removing the optional Qwen3Guard model.";
        RefreshQwenModelProperties();
        try
        {
            await _qwenRuntime.RemoveModelAsync(_incomingEventCts.Token);
            ModerationModelDownloadProgress = 0;
            ModerationModelDownloadStatus =
                "Optional Qwen3Guard model removed. The built-in enhanced filter is active.";
            ModerationModelAccessibleProgress = ModerationModelDownloadStatus;
            AnnounceState(ModerationModelDownloadStatus);
        }
        catch (Exception ex)
        {
            ModerationModelDownloadStatus = $"SafeSpeak could not remove the optional model: {ex.Message}";
            ModerationModelAccessibleProgress = ModerationModelDownloadStatus;
            AnnounceState(ModerationModelDownloadStatus);
        }
        finally
        {
            RefreshQwenModelProperties();
        }
    }
}
