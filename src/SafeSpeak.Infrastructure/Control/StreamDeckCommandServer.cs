using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using SafeSpeak.Core.Control;

namespace SafeSpeak.Infrastructure.Control;

public sealed class StreamDeckCommandServer(
    IControlCommandHandler commandHandler,
    string pipeName = "SafeSpeakControl") : IAsyncDisposable
{
    private const int MaximumRequestCharacters = 8 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CancellationTokenSource _disposeCancellation = new();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCancellation.Token);

        while (!linkedCancellation.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            try
            {
                await pipe.WaitForConnectionAsync(linkedCancellation.Token);
                await HandleConnectionAsync(pipe, linkedCancellation.Token);
            }
            catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
                // A disconnected Stream Deck client is expected; accept the next connection.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _disposeCancellation.CancelAsync();
        _disposeCancellation.Dispose();
    }

    private async Task HandleConnectionAsync(Stream pipe, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        while (!cancellationToken.IsCancellationRequested && pipe.CanRead && pipe.CanWrite)
        {
            string? line;
            try
            {
                line = await ReadBoundedLineAsync(reader, cancellationToken);
            }
            catch (InvalidDataException)
            {
                var oversizedResponse = new ControlResponse(
                    "response",
                    false,
                    "unknown",
                    "Command exceeded the safety limit");
                await writer.WriteLineAsync(JsonSerializer.Serialize(oversizedResponse, JsonOptions));
                return;
            }

            if (line is null)
            {
                return;
            }

            ControlResponse response;
            try
            {
                ControlRequest? request = JsonSerializer.Deserialize<ControlRequest>(line, JsonOptions);
                response = request is null
                    ? new("response", false, "unknown", "Invalid command")
                    : await commandHandler.HandleAsync(request, cancellationToken);
            }
            catch (JsonException)
            {
                response = new("response", false, "unknown", "Invalid JSON");
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                response = new("response", false, "unknown", "Command failed");
            }

            await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions));
        }
    }

    private static async ValueTask<string?> ReadBoundedLineAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var line = new StringBuilder();
        char[] character = new char[1];

        while (true)
        {
            int count = await reader.ReadAsync(character.AsMemory(), cancellationToken);
            if (count == 0)
            {
                return line.Length == 0 ? null : line.ToString();
            }

            if (character[0] == '\n')
            {
                return line.ToString();
            }

            if (character[0] != '\r')
            {
                line.Append(character[0]);
                if (line.Length > MaximumRequestCharacters)
                {
                    throw new InvalidDataException();
                }
            }
        }
    }
}
