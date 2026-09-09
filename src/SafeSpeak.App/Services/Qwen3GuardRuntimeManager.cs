using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace SafeSpeak.App.Services;

public sealed record QwenModelInstallProgress(double Percent, string Status);

/// <summary>
/// Owns SafeSpeak's private, packaged Ollama process and its optional Qwen3Guard
/// model. Nothing is installed system-wide and no external Ollama installation
/// or terminal command is required.
/// </summary>
public sealed class Qwen3GuardRuntimeManager : IAsyncDisposable
{
    public const string ModelName = "sileader/qwen3guard:0.6b";
    public const long ModelBlobBytes = 484_220_000;
    public const string ModelBlobSha256 =
        "2a53e0ce1cdd156b4dcf023e4f7285d4cc1d0d32bbbb0ffa9baed8f5c617f4ad";

    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _modelGate = new(1, 1);
    private readonly HttpClient _httpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };
    private readonly string _runtimeDirectory;
    private readonly string _modelRoot;
    private readonly int _port;
    private Process? _process;
    private bool _disposed;

    public Qwen3GuardRuntimeManager(
        string? runtimeDirectory = null,
        string? modelRoot = null,
        int? port = null)
    {
        _runtimeDirectory = Path.GetFullPath(runtimeDirectory ?? Path.Combine(
            AppContext.BaseDirectory,
            "Runtime",
            "Ollama"));
        _modelRoot = Path.GetFullPath(modelRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SafeSpeak",
            "Models",
            "Qwen3Guard",
            "ollama"));
        _port = port is > 0 and <= 65535 ? port.Value : ReserveLoopbackPort();
    }

    public string EndpointUrl => $"http://127.0.0.1:{_port}";
    public string RuntimeExecutablePath => Path.Combine(_runtimeDirectory, "ollama.exe");
    public bool IsRuntimeAvailable => File.Exists(RuntimeExecutablePath);
    public bool IsRunning => _process is { HasExited: false };
    public bool IsModelInstalled
    {
        get
        {
            string blobPath = GetModelBlobPath();
            return File.Exists(blobPath) && new FileInfo(blobPath).Length == ModelBlobBytes;
        }
    }

    public async Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsRuntimeAvailable)
        {
            return false;
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning && await IsHealthyAsync(cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            StopNow();
            Directory.CreateDirectory(_modelRoot);
            var startInfo = new ProcessStartInfo
            {
                FileName = RuntimeExecutablePath,
                Arguments = "serve",
                WorkingDirectory = _runtimeDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.Environment["OLLAMA_HOST"] = $"127.0.0.1:{_port}";
            startInfo.Environment["OLLAMA_MODELS"] = _modelRoot;
            startInfo.Environment["OLLAMA_NO_CLOUD"] = "1";
            startInfo.Environment["OLLAMA_KEEP_ALIVE"] = "5m";
            startInfo.Environment["OLLAMA_MAX_LOADED_MODELS"] = "1";
            startInfo.Environment["OLLAMA_NUM_PARALLEL"] = "1";
            startInfo.Environment["OLLAMA_LLM_LIBRARY"] = "cpu";
            startInfo.Environment["CUDA_VISIBLE_DEVICES"] = "-1";
            startInfo.Environment["HIP_VISIBLE_DEVICES"] = "-1";

            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            if (!_process.Start())
            {
                _process.Dispose();
                _process = null;
                return false;
            }

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            using var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            startupTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            while (!startupTimeout.IsCancellationRequested && IsRunning)
            {
                if (await IsHealthyAsync(startupTimeout.Token).ConfigureAwait(false))
                {
                    return true;
                }

                await Task.Delay(200, startupTimeout.Token).ConfigureAwait(false);
            }

            StopNow();
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            StopNow();
            return false;
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task InstallModelAsync(
        IProgress<QwenModelInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _modelGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsModelInstalled)
            {
                progress?.Report(new QwenModelInstallProgress(100, "The optional model is already installed."));
                return;
            }

            progress?.Report(new QwenModelInstallProgress(0, "Starting SafeSpeak's private model service."));
            if (!await EnsureStartedAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    "The packaged local model runtime is missing or could not start.");
            }

            progress?.Report(new QwenModelInstallProgress(
                1,
                "Downloading the optional Qwen3Guard model. SafeSpeak remains usable during the download."));
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{EndpointUrl}/api/pull")
            {
                Content = JsonContent.Create(new { model = ModelName, stream = true })
            };
            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using JsonDocument update = JsonDocument.Parse(line);
                JsonElement root = update.RootElement;
                string status = root.TryGetProperty("status", out JsonElement statusElement)
                    ? statusElement.GetString() ?? "Downloading model"
                    : "Downloading model";
                long total = root.TryGetProperty("total", out JsonElement totalElement) &&
                             totalElement.TryGetInt64(out long totalValue)
                    ? totalValue
                    : 0;
                long completed = root.TryGetProperty("completed", out JsonElement completedElement) &&
                                 completedElement.TryGetInt64(out long completedValue)
                    ? completedValue
                    : 0;
                double percent = total > 0
                    ? Math.Clamp(completed * 96.0 / total, 1, 97)
                    : 1;
                progress?.Report(new QwenModelInstallProgress(percent, status));
            }

            progress?.Report(new QwenModelInstallProgress(98, "Verifying the downloaded model."));
            await VerifyModelBlobAsync(cancellationToken).ConfigureAwait(false);
            progress?.Report(new QwenModelInstallProgress(100, "Optional Qwen3Guard model installed and ready."));
        }
        catch
        {
            if (!IsModelInstalled)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    StopNow();
                }
                DeleteIncompleteModelFiles();
            }
            throw;
        }
        finally
        {
            _modelGate.Release();
        }
    }

    public async Task RemoveModelAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _modelGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StopNow();
            if (!Directory.Exists(_modelRoot))
            {
                return;
            }

            string allowedRoot = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SafeSpeak",
                "Models",
                "Qwen3Guard"));
            string modelRootWithSeparator = _modelRoot.TrimEnd(Path.DirectorySeparatorChar) +
                                            Path.DirectorySeparatorChar;
            string allowedRootWithSeparator = allowedRoot.TrimEnd(Path.DirectorySeparatorChar) +
                                              Path.DirectorySeparatorChar;
            if (!modelRootWithSeparator.StartsWith(
                    allowedRootWithSeparator,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to remove a model directory outside SafeSpeak data.");
            }

            Directory.Delete(_modelRoot, recursive: true);
        }
        finally
        {
            _modelGate.Release();
        }
    }

    public void StopNow()
    {
        Process? process = _process;
        _process = null;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // App shutdown must never wait on or be blocked by the private model process.
        }
        finally
        {
            process.Dispose();
        }
    }

    private async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(TimeSpan.FromMilliseconds(500));
            using HttpResponseMessage response = await _httpClient.GetAsync(
                $"{EndpointUrl}/api/version",
                requestTimeout.Token).ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch
        {
            return false;
        }
    }

    private async Task VerifyModelBlobAsync(CancellationToken cancellationToken)
    {
        string blobPath = GetModelBlobPath();
        if (!File.Exists(blobPath) || new FileInfo(blobPath).Length != ModelBlobBytes)
        {
            throw new InvalidDataException("The downloaded Qwen3Guard model has an unexpected size.");
        }

        await using FileStream file = File.OpenRead(blobPath);
        byte[] hash = await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false);
        string actualHash = Convert.ToHexString(hash).ToLowerInvariant();
        if (!actualHash.Equals(ModelBlobSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The downloaded Qwen3Guard model failed its SHA-256 integrity check.");
        }
    }

    private string GetModelBlobPath() => Path.Combine(
        _modelRoot,
        "blobs",
        $"sha256-{ModelBlobSha256}");

    private void DeleteIncompleteModelFiles()
    {
        try
        {
            if (!Directory.Exists(_modelRoot))
            {
                return;
            }

            string allowedRoot = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SafeSpeak",
                "Models",
                "Qwen3Guard"));
            string modelRootWithSeparator = _modelRoot.TrimEnd(Path.DirectorySeparatorChar) +
                                            Path.DirectorySeparatorChar;
            string allowedRootWithSeparator = allowedRoot.TrimEnd(Path.DirectorySeparatorChar) +
                                              Path.DirectorySeparatorChar;
            if (modelRootWithSeparator.StartsWith(
                    allowedRootWithSeparator,
                    StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(_modelRoot, recursive: true);
            }
        }
        catch
        {
            // A later install can resume or replace any remaining temporary data.
        }
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopNow();
        await Task.CompletedTask;
        _httpClient.Dispose();
    }
}
