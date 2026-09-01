# Source connector development guide

Every source integration must follow the same boundary: provider data enters one connector, becomes a normalized `LivestreamEvent`, and then passes through the shared moderation and speech path. Provider adapters do not make speech decisions.

## Contract

`ISourceConnector` exposes:

- a stable `SourceConnectorDescriptor`
- current `ConnectionState`
- a user-readable endpoint description
- normalized `EventReceived` notifications
- written connection-state notifications
- cancellable connect, disconnect, and disposal

The descriptor declares a lowercase stable ID, display/provider names, a non-secret connection description, supported event capabilities, and whether automatic reconnection is supported.

`SourceConnectorRegistry` registers a descriptor and a side-effect-free factory. Construction must not authenticate, open sockets, show UI, or start background work. SafeSpeak creates only the selected connector.

## Required event normalization

Map provider payloads to `LivestreamEvent` before raising them:

- provider user ID to `Author`
- already-safe display label candidate to `AuthorDisplayName`
- chat body to `Text`
- provider membership roles to `AuthorTier`
- gifts and counts to their typed fields
- receive time to `TimestampUtc`

Do not embed provider JSON in application view models. Unknown event types are ignored. Malformed events return no event; they do not crash or enter speech.

Display names and text are still untrusted after normalization. The shared moderation pipeline is the only route into speech. A connector must never enqueue audio directly.

## Lifecycle

1. SafeSpeak constructs the selected connector.
2. The app subscribes to events.
3. After the main window is ready, automatic connection begins if enabled.
4. A local-source failure changes state to Reconnecting and uses bounded backoff.
5. SafeSpeak remains disarmed across connection and reconnection.
6. User Retry cancels the current loop before starting another.
7. Window shutdown stops accepting events, cancels the connector, and awaits bounded disposal as part of the app's five-second background cleanup window.

Link caller cancellation into the connector-owned token. Disconnect and disposal must be idempotent, abort pending I/O, and use an internal deadline shorter than the app's five-second shutdown bound. Do not use untracked fire-and-forget tasks. Event handling in the application is serialized so one provider burst cannot reorder moderation state. Shutdown failures are observed and logged where appropriate; they must never open a blocking dialog.

## Safety and privacy requirements

- Default to localhost or an explicitly configured trusted endpoint.
- Put maximum byte and event-rate bounds before parsing.
- Bound reconnect delays and avoid tight failure loops.
- Do not log raw chat, credentials, tokens, rejected text, or exact hostile matches.
- Reject path traversal, unsafe URLs, unsupported schemes, and invalid identifiers in connector configuration.
- Store secrets through an OS credential facility, never in `settings.json`.
- Provide privacy-safe captured fixtures with invented users and messages.
- Keep errors user-readable and redact endpoints when they contain secrets.
- Connection success never arms speech.

The TikFinity adapter currently limits an individual WebSocket message to 256 KB, links cancellation, parses defensively, normalizes supported event types, and retries with backoff. On close it aborts pending socket work, limits the connection-loop wait to two seconds, and limits a peer close acknowledgement to one second.

## Accessibility requirements

Each connector supplies:

- a short display name
- a plain-language provider description
- Connecting, Connected, Reconnecting, Disconnected, and Faulted states
- a recovery action when automatic retry is insufficient

State is conveyed as text and through a UI Automation live-region event. Do not rely on a colored dot, icon, animation, or toast alone. Retry must be keyboard reachable and restore focus predictably.

## Adding a connector

1. Implement `ISourceConnector` in `src/SafeSpeak.Core/Connectors`.
2. Define one static descriptor and keep its ID stable across releases.
3. Normalize all events to `LivestreamEvent`.
4. Register the descriptor and factory in `SourceConnectorRegistry.CreateDefault`.
5. Add persisted non-secret configuration and a migration if the schema changes.
6. Add parser fixtures, malformed/oversize/cancellation/reconnect tests, and an offline fake.
7. Run the shared moderation corpus against normalized chat.
8. Add accessible connection copy and manual Narrator/NVDA/JAWS tests.
9. Document provider version compatibility and privacy behavior.

Example registration:

```csharp
registry.Register(
    ExampleConnector.ConnectorDescriptor,
    static () => new ExampleConnector());
```

## Pull-request checklist

- [ ] Stable descriptor and capabilities
- [ ] No side effects in factory or constructor
- [ ] Cancellation and idempotent disposal tested against an internal deadline shorter than five seconds
- [ ] Bounded payloads and reconnect backoff
- [ ] Privacy-safe fixtures
- [ ] No raw provider payload outside the connector
- [ ] All chat reaches the shared moderation pipeline
- [ ] Connection never arms speech
- [ ] Written and spoken failure/recovery state
- [ ] Keyboard and screen-reader acceptance evidence
- [ ] Closing during connect, reconnect, and receive work stops new events without a dialog
