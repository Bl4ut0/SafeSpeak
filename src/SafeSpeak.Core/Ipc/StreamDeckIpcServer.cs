using System.Net;
using System.Text;
using System.Text.Json;

namespace SafeSpeak.Core.Ipc;

/// <summary>
/// Loopback-only control server for the Elgato Stream Deck plug-in on 127.0.0.1:21214.
/// Mutating requests require POST plus a plug-in marker and reject web-page origins.
/// </summary>
public sealed class StreamDeckIpcServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Func<IpcStateBroadcast> _stateProvider;
    private readonly Func<string, string, Task<string>> _commandHandler;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public bool IsRunning => _listener.IsListening;
    public int Port { get; }

    public StreamDeckIpcServer(
        Func<IpcStateBroadcast> stateProvider,
        Func<string, string, Task<string>> commandHandler,
        int port = 21214)
    {
        _stateProvider = stateProvider;
        _commandHandler = commandHandler;
        Port = port;
    }

    public void Start()
    {
        if (_listener.IsListening) return;

        try
        {
            _listener.Prefixes.Clear();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();

            _cts = new CancellationTokenSource();
            _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        }
        catch
        {
            // Port might be in use, ignore or log
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener.Stop(); } catch { }
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = ProcessRequestAsync(context);
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Continue listening
            }
        }
    }

    private async Task ProcessRequestAsync(HttpListenerContext context)
    {
        var req = context.Request;
        var resp = context.Response;

        string? origin = req.Headers["Origin"];
        bool isPluginOrigin = string.IsNullOrEmpty(origin) || string.Equals(origin, "null", StringComparison.OrdinalIgnoreCase);
        if (!isPluginOrigin)
        {
            resp.StatusCode = 403;
            resp.Close();
            return;
        }

        // Elgato's legacy HTML plug-in host uses an opaque (null) origin.
        if (!string.IsNullOrEmpty(origin))
        {
            resp.Headers.Add("Access-Control-Allow-Origin", origin);
            resp.Headers.Add("Vary", "Origin");
        }
        resp.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        resp.Headers.Add("Access-Control-Allow-Headers", "Content-Type, X-SafeSpeak-Client");

        if (req.HttpMethod == "OPTIONS")
        {
            resp.StatusCode = 200;
            resp.Close();
            return;
        }

        try
        {
            string path = req.Url?.AbsolutePath.ToLowerInvariant() ?? "/";

            if (path == "/state")
            {
                var state = _stateProvider();
                string json = JsonSerializer.Serialize(state);
                byte[] bytes = Encoding.UTF8.GetBytes(json);

                resp.ContentType = "application/json";
                resp.StatusCode = 200;
                await resp.OutputStream.WriteAsync(bytes);
            }
            else if (path == "/command")
            {
                if (!string.Equals(req.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(req.Headers["X-SafeSpeak-Client"], "streamdeck", StringComparison.Ordinal))
                {
                    resp.StatusCode = 403;
                    return;
                }

                string cmd = req.QueryString["cmd"] ?? "";
                string param = req.QueryString["param"] ?? "";

                if (string.IsNullOrEmpty(cmd) && req.HasEntityBody)
                {
                    using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                    string body = await reader.ReadToEndAsync();
                    try
                    {
                        var parsed = JsonSerializer.Deserialize<IpcCommandMessage>(body);
                        if (parsed != null)
                        {
                            cmd = parsed.Command;
                            param = parsed.Parameter;
                        }
                    }
                    catch { }
                }

                string result = await _commandHandler(cmd, param);
                byte[] bytes = Encoding.UTF8.GetBytes(result);

                resp.ContentType = "text/plain";
                resp.StatusCode = 200;
                await resp.OutputStream.WriteAsync(bytes);
            }
            else
            {
                resp.StatusCode = 404;
            }
        }
        catch (Exception ex)
        {
            resp.StatusCode = 500;
            byte[] err = Encoding.UTF8.GetBytes(ex.Message);
            await resp.OutputStream.WriteAsync(err);
        }
        finally
        {
            try { resp.Close(); } catch { }
        }
    }

    public void Dispose()
    {
        Stop();
        _listener.Close();
        _cts?.Dispose();
    }
}
