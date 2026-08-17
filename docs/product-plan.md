# SafeSpeak product plan

## Objective

Deliver a single accessible Windows application that runs alongside TikFinity, receives TikTok LIVE chat from TikFinity's local WebSocket, blocks abusive or evasive messages, and exposes approved TTS as a named Windows audio session. A bundled Stream Deck plug-in provides physical controls without changing the user's existing profiles.

## Deployment model

- One Windows installer.
- One SafeSpeak application launched alongside TikFinity.
- One Elgato plug-in installed during setup and hosted automatically by Stream Deck.
- Local moderation model and rule data included with the installer.
- No required cloud service or recurring usage cost.

The plug-in only adds SafeSpeak actions to Elgato's action list. It must not install, replace, select, or edit a Stream Deck profile. A sighted helper can place desired actions into the user's established layout.

## Core components

### TikFinity connector

- Connect to `ws://localhost:21213/`.
- Parse chat events defensively and ignore unknown fields.
- Reconnect with bounded backoff.
- Reject malformed and oversized payloads.
- Report connection health without retaining message content.
- Include an offline event simulator for development and support.

### Moderation pipeline

1. Validate message length and event structure.
2. Apply audience eligibility and user cooldown rules.
3. Normalize Unicode, invisible characters, spacing, repetition, and common substitutions.
4. Detect mixed or disallowed writing systems.
5. Apply configurable literal and normalized block rules.
6. Run a local toxicity classifier when enabled.
7. Approve, hold for manual playback, or reject.
8. Queue only approved text for speech.

Usernames are not spoken by default and must pass the same checks when enabled.

### TTS and audio

- Use an offline Windows voice for the first release.
- Create one named broadcast audio session: `SafeSpeak TTS`.
- Select a Windows output endpoint or follow the default endpoint.
- Provide a routing test and immediate emergency cancellation.
- Keep private accessibility feedback out of the broadcast audio session.

### Stream Deck plug-in

- Communicate with SafeSpeak through a local authenticated channel.
- Expose independent actions in Elgato's normal action list.
- Synchronize toggle state from SafeSpeak instead of assuming a press succeeded.
- Never alter existing profiles or layouts.
- Show a disconnected state when SafeSpeak is not running.

Initial actions:

- Arm or disarm TTS.
- Enable or disable automatic playback.
- Play next approved message.
- Skip the current message.
- Pause or resume the queue.
- Clear the queue.
- Emergency stop and clear.
- Announce status privately.
- Cycle audience mode.
- Cycle moderation strictness.
- Toggle English-only TTS.
- Play configurable preset messages.

### Accessibility

SafeSpeak is accessible in every mode. On first launch it asks whether the user is fully blind and wants screen-reader-optimized operation. That preference enables additional private announcements and guidance but does not gate basic accessibility.

Requirements:

- Native controls with useful accessible names and descriptions.
- Complete keyboard operation with logical focus order.
- Screen-reader announcements for state changes and errors.
- No information conveyed only by colour, position, or animation.
- No rejected message spoken as part of a warning.
- A status command that reports connection, armed state, queue state, and output route.

### Diagnostics and privacy

- Built-in TikFinity and Stream Deck simulators.
- One-command self-test for connector, moderation, TTS, audio, and controls.
- Exportable support package containing versions, settings, event shapes, and error codes.
- Chat text and usernames excluded from support packages by default.
- Rejected content not persisted unless the user explicitly enables a bounded diagnostic session.

## Safety defaults

- Start disarmed after installation or an unexpected restart.
- English-only TTS enabled.
- Unicode normalization and invisible-character removal enabled.
- Mixed writing systems rejected.
- Usernames and URLs not spoken.
- Conservative message length, cooldown, and queue limits.
- Classifier, connector, or output-device failure pauses automatic speech.

## Testing strategy

- Unit tests for deterministic moderation transforms and policies.
- Regression corpus for Unicode confusables, zero-width characters, spacing, repetition, mixed scripts, and known TTS evasions.
- Fuzz tests for malformed TikFinity payloads.
- Simulated floods, duplicates, disconnects, and reconnects.
- Queue cancellation and fail-closed tests.
- Screen-reader inspection and keyboard-only walkthroughs.
- Stream Deck protocol tests with a mock host.
- Installer, upgrade, uninstall, and settings-migration tests.
- A guided acceptance test on the streamer's real TikFinity, audio, screen-reader, and Stream Deck setup.

## Milestones

1. Connector contracts, simulator, and moderation core.
2. Accessible Windows shell and first-run setup.
3. TTS queue and named Windows audio session.
4. Stream Deck plug-in and state synchronization.
5. Local model integration and attack regression suite.
6. Self-diagnostics and privacy-safe support export.
7. Installer and real-machine acceptance test.

## Out of scope for the first release

- Direct connection to TikTok without TikFinity.
- Automatic modification of Stream Deck profiles.
- Cloud-required moderation.
- Moderating or deleting TikTok chat messages on the platform.
- Guaranteeing detection of every possible abusive utterance.

