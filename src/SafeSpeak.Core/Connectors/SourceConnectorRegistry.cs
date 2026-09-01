namespace SafeSpeak.Core.Connectors;

/// <summary>
/// Explicit registry for built-in and future source adapters. Registration is
/// side-effect free; a connector is created only when selected.
/// </summary>
public sealed class SourceConnectorRegistry
{
    private readonly Dictionary<string, (SourceConnectorDescriptor Descriptor, Func<ISourceConnector> Factory)> _registrations =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<SourceConnectorDescriptor> Descriptors =>
        _registrations.Values
            .Select(item => item.Descriptor)
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

    public void Register(SourceConnectorDescriptor descriptor, Func<ISourceConnector> factory)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(factory);
        if (string.IsNullOrWhiteSpace(descriptor.Id))
        {
            throw new ArgumentException("A connector ID is required.", nameof(descriptor));
        }

        if (!_registrations.TryAdd(descriptor.Id, (descriptor, factory)))
        {
            throw new InvalidOperationException($"A source connector with ID '{descriptor.Id}' is already registered.");
        }
    }

    public ISourceConnector Create(string connectorId)
    {
        if (!_registrations.TryGetValue(connectorId, out var registration))
        {
            throw new KeyNotFoundException($"Source connector '{connectorId}' is not registered.");
        }

        ISourceConnector connector = registration.Factory();
        if (!string.Equals(
                connector.Descriptor.Id,
                registration.Descriptor.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            connector.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw new InvalidOperationException("The connector factory returned a different connector ID.");
        }

        return connector;
    }

    public static SourceConnectorRegistry CreateDefault()
    {
        var registry = new SourceConnectorRegistry();
        registry.Register(
            TikFinityWebSocketClient.ConnectorDescriptor,
            static () => new TikFinityWebSocketClient());
        return registry;
    }
}
