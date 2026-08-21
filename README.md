# SafeSpeak

SafeSpeak is a Windows-first, accessibility-focused TTS safety application for livestreamers. It connects to TikFinity's local event feed, applies local moderation before speech, queues only approved content, and provides keyboard, integrated-reader, and optional Stream Deck controls.

> [!WARNING]
> SafeSpeak reduces TTS abuse risk but cannot guarantee detection of every hostile message. Start disarmed with automatic playback off, disable TikFinity's independent TTS, and keep TikTok moderation and trusted moderators active.

## Current implementation

- WPF desktop application targeting .NET 8 on Windows 10 build 19041 or later.
- A visible and spoken first-run reader question that requires two matching Yes or No answers before saving. A mismatch restarts confirmation without changing settings.
- Full Tab/Shift+Tab navigation, access keys, large controls, visible keyboard focus, UI Automation names/live regions, Windows High Contrast support, and a saved in-app extra-high-contrast theme.
- Four-section dashboard: Live feed, Moderation, Audio, and Accessibility.
- TikFinity localhost WebSocket connection, reconnection, defensive parsing for chat, gifts, follows, shares, subscriptions, joins, and likes, plus an offline emulator.
- Local Unicode normalization, invisible-character and homoglyph handling, mixed-script rules, built-in/custom terms, audience rules, cooldown, URL removal, and optional contextual heuristic classification.
- Safe display-name moderation and Narrator-safe redaction of rejected feed content.
- A bounded 50-message queue that starts disarmed with automatic playback disabled, plus manual play, skip, pause, clear, and emergency cancellation.
- Installed Windows voices plus an optional, explicitly downloaded Kokoro model with 27 genuine local neural English voices.
- The selected Windows or Kokoro voice also powers SafeSpeak's integrated reader, with an accessible private voice-test button on the Audio tab.
- Independently selectable WASAPI broadcast and private-monitor endpoints, optional approved-message mirroring, and safe private blocked-message notices that never speak rejected text.
- Audio device, voice, rate, volume, blocked-term, and moderation preferences are applied at runtime and saved locally for the next launch.
- A loopback-only Stream Deck control service hardened against web-page origins and GET-based control requests.
- A separate 24-action Stream Deck plug-in covering safety, playback, connection, every live-event category, audio outputs, and high contrast. It never creates or changes a user's profile.
- Repeatable self-contained ZIP and structurally verified MSIX builds for x64 and arm64, plus schema-validated Stream Deck packaging, with pinned .NET SDK and Windows CI.

## Build and test

```powershell
dotnet build SafeSpeak.sln -c Release
dotnet test SafeSpeak.sln -c Release --no-build
```

Build a self-contained ZIP and MSIX candidate:

```powershell
./installer/Build-Release.ps1 -Architecture x64 -PackageVersion 0.1.0.0 -Format Both
```

The MSIX path requires Windows SDK packaging tools. Store submissions also require the exact identity values assigned in Partner Center. See the [packaging and Microsoft Store guide](installer/README.md).

## Repository layout

- `src/SafeSpeak.App` — accessible WPF desktop application
- `src/SafeSpeak.Core` — moderation, queue, Windows/Kokoro speech, connector, accessibility, audio routing, and local-control services
- `tests/SafeSpeak.Core.Tests` — deterministic safety, packaging-adjacent, and regression tests
- `streamdeck` — separately installed Elgato plug-in
- `installer` — self-contained ZIP/MSIX scripts, manifests, assets, and WinGet generation
- `docs` — implementation scope and UI/accessibility roadmap
- `tools` — TikFinity emulator and development utilities

## Important limitations

- The current English-only option enforces writing-system rules; a full language-identification model is not yet integrated.
- The current contextual classifier is a local heuristic layer, not a general-purpose language model.
- TikFinity payloads can change between releases; the supported event parser must be regression-tested against the installed TikFinity version.
- Kokoro requires an explicit roughly 326 MB model download. It is not bundled into the base installer, and Windows voices remain available without it.
- Stream Deck physical-button and full Narrator/NVDA/JAWS acceptance testing remain release gates.
- Store identity, listing, production artwork, certification answers, signing, and Partner Center submission remain external release steps.

See the [product plan](docs/product-plan.md) and [UI/accessibility roadmap](docs/ui-accessibility-roadmap.md).

Voice source, privacy, and installation details are in [voice engines](docs/voice-engines.md).

## License

SafeSpeak is licensed under the [MIT License](LICENSE).
