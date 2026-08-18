# SafeSpeak

SafeSpeak is a Windows-first, accessibility-focused TTS safety application for livestreamers. It is designed primarily for blind streamers who want physical control of chat text-to-speech and stronger protection against abusive, obfuscated, or non-English bypass messages.

> [!WARNING]
> This is a development foundation, not a live-stream safety release. The connector, rule engine, queue, accessible shell, and Stream Deck protocol work; spoken audio, the local toxicity model, configuration UI, and installer are not implemented. Do not rely on this build to protect a live stream.

## Current foundation

- C# and .NET 10 WPF Windows application.
- An automatically spoken first-run question with one-key choices for fully blind, partially sighted, or standard operation.
- Native keyboard and screen-reader controls with live status announcements.
- Defensive TikFinity WebSocket client for `ws://localhost:21213/` with reconnection and payload limits.
- Offline TikFinity event simulator.
- Unicode NFKC normalization, invisible-character removal, spacing and repetition controls, common confusable handling, script detection, URL rejection, audience checks, cooldowns, and a bounded approved-message queue.
- Fail-closed interface for a future local ONNX toxicity classifier.
- User-only Windows named-pipe control protocol.
- Elgato Stream Deck plugin with 12 actions, including emergency stop, queue controls, English-only mode, and configurable preset-message buttons.
- Deterministic .NET and TypeScript tests plus Windows CI.

SafeSpeak starts disarmed. TikFinity's own TTS must eventually be disabled so unmoderated messages cannot bypass SafeSpeak.

The running foundation includes only the neutral `badword` sentinel used by its regression tests. A real abusive-language ruleset and classifier are still required; the normalization layer alone is not a complete hate-speech filter.

## Architecture

```text
TikTok LIVE -> TikFinity -> local WebSocket -> SafeSpeak rules -> approved queue -> TTS/audio (next milestone)
                                                        ^
                                                        |
Stream Deck -> SafeSpeak plugin -> current-user named pipe
```

The plugin only adds SafeSpeak actions to Elgato's normal action list. It contains no profiles and cannot install, replace, select, or edit the user's existing Stream Deck profile. A sighted helper can drag the desired actions into the streamer's established layout.

See [architecture](docs/architecture.md), [accessibility](docs/accessibility.md), [testing](docs/testing.md), and the [product plan](docs/product-plan.md).

## Build and run

Requirements for development:

- Windows 10 or later
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Node.js 24 or later for the Stream Deck plugin
- Stream Deck 7.1 or later for desktop plugin testing

```powershell
dotnet restore SafeSpeak.sln
dotnet build SafeSpeak.sln --configuration Release
dotnet test SafeSpeak.sln --configuration Release
```

For an offline connector test, start the simulator and the app in separate terminals:

```powershell
dotnet run --project tools/SafeSpeak.TikFinitySimulator
dotnet run --project src/SafeSpeak.App
```

Build and validate the plugin:

```powershell
cd streamdeck
npm ci
npm run check
npm test
npm run build
npm run validate
```

The first launch writes only user preferences to `%LOCALAPPDATA%\SafeSpeak\settings.json`. Chat text and usernames are not logged or persisted by this foundation.

## Safety boundaries

No automated moderation can guarantee that every abusive message will be detected. SafeSpeak is intended to reduce risk; it does not replace TikTok moderation, trusted human moderators, or account-level safety controls. A release will not be considered ready until the local model, audio cancellation, diagnostics, screen-reader walkthroughs, and real TikFinity/Stream Deck acceptance tests are complete.

## License

SafeSpeak is licensed under the [MIT License](LICENSE).
