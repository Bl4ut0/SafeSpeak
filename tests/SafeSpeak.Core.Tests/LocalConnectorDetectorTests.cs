using SafeSpeak.Core.Connectors;

namespace SafeSpeak.Core.Tests;

public sealed class LocalConnectorDetectorTests
{
    [Fact]
    public async Task WithoutConsent_DoesNotInspectProcessesOrPorts()
    {
        var probe = new FakeProbe(processFound: true, portFound: true);
        var detector = new LocalConnectorDetector(probe);

        IReadOnlyList<LocalConnectorDetectionResult> results =
            await detector.DetectAsync(userConsented: false);

        LocalConnectorDetectionResult result = Assert.Single(results);
        Assert.Equal(LocalConnectorDetectionStatus.ConsentRequired, result.Status);
        Assert.Equal(0, probe.ProcessChecks);
        Assert.Equal(0, probe.PortChecks);
    }

    [Fact]
    public async Task ApprovedProcess_IsReportedWithoutOpeningEndpoint()
    {
        var probe = new FakeProbe(processFound: true, portFound: false);
        var detector = new LocalConnectorDetector(probe);

        LocalConnectorDetectionResult result =
            Assert.Single(await detector.DetectAsync(userConsented: true));

        Assert.Equal(LocalConnectorDetectionStatus.Detected, result.Status);
        Assert.Equal("tikfinity", result.ConnectorId);
        Assert.Equal(1, probe.ProcessChecks);
        Assert.Equal(0, probe.PortChecks);
    }

    [Fact]
    public async Task ApprovedLocalListener_IsDetectedWhenProcessIsAbsent()
    {
        var probe = new FakeProbe(processFound: false, portFound: true);
        var detector = new LocalConnectorDetector(probe);

        LocalConnectorDetectionResult result =
            Assert.Single(await detector.DetectAsync(userConsented: true));

        Assert.Equal(LocalConnectorDetectionStatus.Detected, result.Status);
        Assert.Equal(1, probe.ProcessChecks);
        Assert.Equal(1, probe.PortChecks);
    }

    [Fact]
    public async Task NothingAvailable_ReportsManualSetupWithoutFailure()
    {
        var probe = new FakeProbe(processFound: false, portFound: false);
        var detector = new LocalConnectorDetector(probe);

        LocalConnectorDetectionResult result =
            Assert.Single(await detector.DetectAsync(userConsented: true));

        Assert.Equal(LocalConnectorDetectionStatus.NotDetected, result.Status);
        Assert.Contains("connect later", result.SafeDescription);
    }

    [Fact]
    public async Task SlowProbe_IsBoundedAndReportsTimeout()
    {
        var probe = new FakeProbe(
            processFound: false,
            portFound: false,
            delay: TimeSpan.FromSeconds(5));
        var detector = new LocalConnectorDetector(
            probe,
            perConnectorTimeout: TimeSpan.FromMilliseconds(25));

        LocalConnectorDetectionResult result =
            Assert.Single(await detector.DetectAsync(userConsented: true));

        Assert.Equal(LocalConnectorDetectionStatus.TimedOut, result.Status);
    }

    [Fact]
    public async Task CallerCancellation_IsPropagated()
    {
        var probe = new FakeProbe(
            processFound: false,
            portFound: false,
            delay: TimeSpan.FromSeconds(5));
        var detector = new LocalConnectorDetector(probe);
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => detector.DetectAsync(
                userConsented: true,
                cancellation.Token));
    }

    private sealed class FakeProbe(
        bool processFound,
        bool portFound,
        TimeSpan? delay = null) : ILocalConnectorProbe
    {
        public int ProcessChecks { get; private set; }
        public int PortChecks { get; private set; }

        public async ValueTask<bool> IsAnyApprovedProcessRunningAsync(
            IReadOnlyList<string> processNames,
            CancellationToken cancellationToken)
        {
            ProcessChecks++;
            Assert.All(processNames, name => Assert.DoesNotContain(
                Path.DirectorySeparatorChar,
                name));
            if (delay is not null)
            {
                await Task.Delay(delay.Value, cancellationToken);
            }
            return processFound;
        }

        public async ValueTask<bool> IsLocalTcpPortListeningAsync(
            int port,
            CancellationToken cancellationToken)
        {
            PortChecks++;
            Assert.Equal(21213, port);
            if (delay is not null)
            {
                await Task.Delay(delay.Value, cancellationToken);
            }
            return portFound;
        }
    }
}
