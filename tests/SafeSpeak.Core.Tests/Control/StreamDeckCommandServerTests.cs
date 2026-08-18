using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using SafeSpeak.Core.Control;
using SafeSpeak.Infrastructure.Control;

namespace SafeSpeak.Core.Tests.Control;

public sealed class StreamDeckCommandServerTests
{
    [Fact]
    public async Task ReturnsAResponseOverTheCurrentUserPipe()
    {
        string pipeName = $"SafeSpeakTest-{Guid.NewGuid():N}";
        var handler = new RecordingHandler();
        await using var server = new StreamDeckCommandServer(handler, pipeName);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task serverTask = server.RunAsync(cancellation.Token);

        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(cancellation.Token);
        using var reader = new StreamReader(client, Encoding.UTF8, false, leaveOpen: true);
        using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        await writer.WriteLineAsync("{\"type\":\"command\",\"command\":\"queue.clear\"}");
        string? line = await reader.ReadLineAsync(cancellation.Token);
        ControlResponse? response = JsonSerializer.Deserialize<ControlResponse>(line!, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal(ControlCommands.ClearQueue, handler.LastCommand);

        await cancellation.CancelAsync();
        await serverTask;
    }

    private sealed class RecordingHandler : IControlCommandHandler
    {
        public string? LastCommand { get; private set; }

        public ValueTask<ControlResponse> HandleAsync(
            ControlRequest request,
            CancellationToken cancellationToken = default)
        {
            LastCommand = request.Command;
            return ValueTask.FromResult(new ControlResponse("response", true, request.Command, "accepted"));
        }
    }
}
