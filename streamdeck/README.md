# SafeSpeak Stream Deck plug-in

This separately installed Windows plug-in exposes SafeSpeak controls in Elgato's action list. It never creates, imports, selects, replaces, or edits a Stream Deck profile. A sighted helper can add only the desired buttons to the streamer's existing layout.

## Current actions

The plug-in deliberately exposes only the controls needed during a live session:

1. Hear Status
2. Arm or Disarm
3. Emergency Stop
4. Automatic or Manual Playback Mode
5. Pause or Resume TTS
6. Speak Next
7. Stop Current
8. Clear Queue

Safety rules, announcement types, source connections, audio routing, usernames,
language filtering, moderation strength, and themes are configured inside
SafeSpeak. Keeping those choices in the app gives screen-reader users the label,
current value, and help text needed to change them safely.

The plug-in uses Elgato's local WebSocket host and SafeSpeak's loopback service at `127.0.0.1:21214`. Control requests use POST with a plug-in marker; SafeSpeak rejects ordinary GET controls and non-local web-page origins. This reduces browser-based localhost attacks, but a future per-user authenticated named-pipe transport remains the preferred security design.

## Build and installation

The MSIX intentionally does not modify Stream Deck. SafeSpeak keeps the plug-in separate so it cannot replace or alter an existing profile.

Install Elgato's current Stream Deck CLI once on the development machine, then validate and package the installer:

```powershell
npm install -g @elgato/cli@latest
./streamdeck/Build-Plugin.ps1
```

The build script regenerates correctly sized assets, validates the manifest against Elgato's current schema, stages a `com.safespeak.streamdeck.sdPlugin` directory, and creates a `.streamDeckPlugin` installer in `artifacts`. Open that installer through Stream Deck, then let the streamer or a sighted helper place only the desired actions manually.

Before release, verify every action on physical Stream Deck hardware, state refresh after reconnect, the app-closed alert path, and that no user profile changes occur automatically.
