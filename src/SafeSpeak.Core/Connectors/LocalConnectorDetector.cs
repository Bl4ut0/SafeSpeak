using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;

namespace SafeSpeak.Core.Connectors;

public enum LocalConnectorDetectionStatus
{
    ConsentRequired = 0,
    Detected = 1,
    NotDetected = 2,
    TimedOut = 3,
    Failed = 4
}

public sealed record LocalConnectorDetectionResult(
    string ConnectorId,
    string DisplayName,
    LocalConnectorDetectionStatus Status,
    string SafeDescription);

public interface ILocalConnectorProbe
{
    ValueTask<bool> IsAnyApprovedProcessRunningAsync(
        IReadOnlyList<string> processNames,
        CancellationToken cancellationToken);

    ValueTask<bool> IsLocalTcpPortListeningAsync(
        int port,
        CancellationToken cancellationToken);
}

/// <summary>
/// Detects only explicitly approved local connector process names and listener
/// ports. It never opens a network connection, authenticates, scans files, or
/// starts a connector.
/// </summary>
public sealed class LocalConnectorDetector
{
    private static readonly LocalConnectorDefinition[] DefaultDefinitions =
    [
        new(
            TikFinityWebSocketClient.ConnectorDescriptor.Id,
            TikFinityWebSocketClient.ConnectorDescriptor.DisplayName,
            ["TikFinity", "TikFinityApp"],
            [21213])
    ];

    private readonly ILocalConnectorProbe _probe;
    private readonly IReadOnlyList<LocalConnectorDefinition> _definitions;
    private readonly TimeSpan _perConnectorTimeout;

    public LocalConnectorDetector(
        ILocalConnectorProbe? probe = null,
        TimeSpan? perConnectorTimeout = null)
        : this(probe, DefaultDefinitions, perConnectorTimeout)
    {
    }

    internal LocalConnectorDetector(
        ILocalConnectorProbe? probe,
        IReadOnlyList<LocalConnectorDefinition> definitions,
        TimeSpan? perConnectorTimeout = null)
    {
        _probe = probe ?? new SystemLocalConnectorProbe();
        _definitions = definitions;
        _perConnectorTimeout = perConnectorTimeout ?? TimeSpan.FromSeconds(1);
        if (_perConnectorTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(perConnectorTimeout));
        }
    }

    public async Task<IReadOnlyList<LocalConnectorDetectionResult>> DetectAsync(
        bool userConsented,
        CancellationToken cancellationToken = default)
    {
        if (!userConsented)
        {
            return _definitions
                .Select(definition => new LocalConnectorDetectionResult(
                    definition.ConnectorId,
                    definition.DisplayName,
                    LocalConnectorDetectionStatus.ConsentRequired,
                    "Not checked. Local detection requires your permission."))
                .ToArray();
        }

        var results = new List<LocalConnectorDetectionResult>(_definitions.Count);
        foreach (LocalConnectorDefinition definition in _definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(_perConnectorTimeout);

            try
            {
                bool processFound =
                    await _probe.IsAnyApprovedProcessRunningAsync(
                        definition.ApprovedProcessNames,
                        timeout.Token);
                bool portFound = false;
                if (!processFound)
                {
                    foreach (int port in definition.LocalListenerPorts)
                    {
                        if (await _probe.IsLocalTcpPortListeningAsync(
                                port,
                                timeout.Token))
                        {
                            portFound = true;
                            break;
                        }
                    }
                }

                bool detected = processFound || portFound;
                results.Add(new LocalConnectorDetectionResult(
                    definition.ConnectorId,
                    definition.DisplayName,
                    detected
                        ? LocalConnectorDetectionStatus.Detected
                        : LocalConnectorDetectionStatus.NotDetected,
                    detected
                        ? $"{definition.DisplayName} appears to be available on this computer."
                        : $"{definition.DisplayName} was not detected. You can still select it and connect later."));
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested)
            {
                results.Add(new LocalConnectorDetectionResult(
                    definition.ConnectorId,
                    definition.DisplayName,
                    LocalConnectorDetectionStatus.TimedOut,
                    $"{definition.DisplayName} detection timed out. You can configure it manually."));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                results.Add(new LocalConnectorDetectionResult(
                    definition.ConnectorId,
                    definition.DisplayName,
                    LocalConnectorDetectionStatus.Failed,
                    $"{definition.DisplayName} could not be checked. You can configure it manually."));
            }
        }

        return results;
    }

    internal sealed record LocalConnectorDefinition(
        string ConnectorId,
        string DisplayName,
        IReadOnlyList<string> ApprovedProcessNames,
        IReadOnlyList<int> LocalListenerPorts);

    private sealed class SystemLocalConnectorProbe : ILocalConnectorProbe
    {
        public ValueTask<bool> IsAnyApprovedProcessRunningAsync(
            IReadOnlyList<string> processNames,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (string processName in processNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Process[] processes = Process.GetProcessesByName(processName);
                try
                {
                    if (processes.Length > 0)
                    {
                        return ValueTask.FromResult(true);
                    }
                }
                finally
                {
                    foreach (Process process in processes)
                    {
                        process.Dispose();
                    }
                }
            }

            return ValueTask.FromResult(false);
        }

        public ValueTask<bool> IsLocalTcpPortListeningAsync(
            int port,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool listening = IPGlobalProperties
                .GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Any(endpoint =>
                    endpoint.Port == port &&
                    IsLocalListenerAddress(endpoint.Address));
            return ValueTask.FromResult(listening);
        }

        private static bool IsLocalListenerAddress(IPAddress address) =>
            IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any);
    }
}
