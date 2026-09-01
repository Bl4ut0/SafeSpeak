# UI and accessibility roadmap

## Design direction

SafeSpeak should feel calm, legible, and operational under pressure. Global safety comes first, live state second, and task-specific controls third. Accessibility information is the same state visible users receive, expressed through text, standard control patterns, focus, and UI Automation.

## Verified interface foundation

- Four short tabs: Live, Safety, Voice, and Settings
- Automatic source status and Retry without manual connect/disconnect clutter
- One named moderation-strength slider and one manageable banned-terms list
- Explicit Tab order, access keys, arrow-key tab help, and deterministic focus on Arm
- WPF UI Automation `LiveRegionChanged` events for connection, arm, queue, playback, moderation strength, setup, and latest status
- Toggle pattern and current state for Arm
- Warning when Windows refuses one or more global shortcuts
- Rejected text and unsafe author redaction
- 3-pixel focus visuals and normal-theme component borders darkened beyond the previous faint token
- Wrapping top-level controls and reduced minimum window/setup width for high scaling
- Automatic Windows High Contrast palette plus a saved high-contrast palette foundation
- Non-blocking close: disarm and input cancellation begin immediately, cleanup runs away from the interface thread, and the app exits after a five-second deadline without showing a shutdown dialog

## Current redesign target

The following work is planned or in progress and is not yet a completed-feature claim:

- Persistent Arm SafeSpeak, Pause TTS, Manual Mode, Speak Next Approved Message, Stop Current Speech, Clear Queue, Emergency Stop, and Hear Status controls
- Disarmed intake that drops new source events before moderation, activity, logging, or speech
- A first launch that asks separately about built-in spoken guidance and Light, Dark, or High Contrast
- A second-launch confirmation followed by platform selection, consent-based connector detection, truthful enhanced-filter status, and a written/spoken review
- Settings-time guidance/theme changes, Run Setup Again, and a separate safe data reset
- Accessible selection announcements for every object-backed list, including voices, devices, connectors, and imported packs
- A voice-pack import surface only after a supported backend can execute, preview, cancel, persist, and roll back safely
- Removal or isolation of expert event, audience, dual-route audio, cloud/endpoint model, channel, and manual-review settings from the primary focus order

## Known limitations

- Built-in SafeSpeak guidance uses the Windows default playback device. It is not guaranteed private when stream software captures desktop audio.
- `MainViewModel` still retains advanced compatibility behavior and should be split after the release interaction model stabilizes.
- Dark theme, the independent guidance/theme setting, and the complete two-launch onboarding flow are not yet finished.
- Playback-state transitions and real Windows/Kokoro cancellation still need acceptance evidence.
- External screen readers need manual confirmation that each status produces one useful announcement rather than duplicate speech.
- WPF layout has not yet been accepted at 200% and 400% on small displays.
- Hotkey availability is reported at startup, but there is not yet a persistent per-shortcut status table.
- Settings persistence is reported on failure but is not yet versioned, migrated, validated, or debounced.

## P0 release accessibility gates

1. Complete task-based sessions with a fully blind streamer: first run, reconnect, arm, pause, enter manual mode, speak one approved message, stop current speech, clear pending speech, use Emergency Stop, change strength, add/remove a banned term, test a voice, and change guidance/theme settings.
2. Run Narrator, NVDA, and JAWS at 100%, 200%, and 400% scaling.
3. Verify Light, Dark, High Contrast, and Windows High Contrast.
4. Add UIA regression tests for focus order, unique names, Toggle state, tab selection, enabled state, and live events.
5. Verify focus recovery after setup, connection failure, model or voice-pack work, and Emergency Stop.
6. Confirm rejected content never reaches visible text, built-in speech, UIA names/help, logs, or external screen readers.
7. Verify unavailable global shortcuts are announced and the on-screen equivalent remains reachable.
8. Close with Windows speech and Kokoro speech actively synthesizing or playing. Confirm new source events stop immediately, repeated Close does not restart cleanup, no dialog or focus trap appears, and the process exits at the five-second cleanup deadline.

## P1 visual and operational quality

- Designed empty, connected, reconnecting, queue-full, no-device, download, and route-failure states
- Audio route test and written result
- Consistent text-adjacent icons; never use icon or color as the only meaning
- Compact and comfortable density options that preserve target sizes
- Final Store iconography and screenshot layouts

## P2 later-track accessibility

- Searchable advanced settings and command discovery
- Privacy-safe diagnostics export
- Localized visible, access-key, spoken, and Store text
- Additional voice engines and remote endpoints after the supported local pack path passes release gates
- Accessible multi-connector selection and authentication
- Any manual review workflow, with deliberately redacted detail

## Acceptance rule

A UI change is complete only when it works with mouse, keyboard alone, Light, Dark, High Contrast, Windows High Contrast, built-in guidance on and off, and external screen readers. Visual polish cannot replace accessible names, control state, focus, written status, or predictable navigation.
