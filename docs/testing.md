# Testing

## Automated checks

Run the complete local checks from the repository root:

```powershell
dotnet format SafeSpeak.sln --verify-no-changes --no-restore
dotnet build SafeSpeak.sln --configuration Release --no-restore
dotnet test SafeSpeak.sln --configuration Release --no-build
cd streamdeck
npm run check
npm test
npm run build
npm run validate
```

The .NET suite covers Unicode normalization, invisible characters, confusables, mixed and disallowed scripts, block terms, URLs, audience policy, classifier failure, cooldowns, bounded queue behavior, TikFinity payload variants, settings fallback, and an actual current-user named-pipe round trip. TypeScript tests cover control request and response framing. GitHub Actions repeats the checks on Windows.

Tests use neutral sentinel terms such as `badword`; they do not need to contain real slurs.

## Offline TikFinity test

Run these commands in separate terminals:

```powershell
dotnet run --project tools/SafeSpeak.TikFinitySimulator
dotnet run --project src/SafeSpeak.App
```

The simulator listens only on `127.0.0.1:21213`, waits one second after connection so TTS can be armed, and sends benign fixtures for normal chat, spaced block-term text, an invisible character, mixed Cyrillic/Latin text, a non-Latin message, a gift event, and malformed chat data. Arm TTS in the app to let approved chat enter the queue. Confirm the Approved queue section lists only normalized, approved text and the TikFinity bridge section reaches seven text events, five valid chat messages, and two ignored events. This build deliberately does not produce audio.

To replay the spoken first-run question without changing the saved accessibility preference, run:

```powershell
dotnet run --project src/SafeSpeak.App -- --test-first-run
```

## Stream Deck desktop test

Build and validate the plugin, install and launch Stream Deck 7.1 or later, and then run:

```powershell
cd streamdeck
npx streamdeck link com.bl4ut0.safespeak.sdPlugin
npx streamdeck dev
npx streamdeck restart com.bl4ut0.safespeak
```

Use a disposable test profile for development. Confirm that SafeSpeak appears as an action category, each chosen action can be added manually, toggle state changes only after an acknowledged response, and buttons show an alert when the app is closed. Also confirm the plugin has not added or switched any profile.

Real TikFinity traffic, physical Stream Deck button acceptance, NVDA/JAWS/Narrator, audio routing, floods, fuzzing, installer upgrades, and classifier regression corpora remain required before release.

## Desktop smoke-test record

On August 18, 2026, Stream Deck 7.5.1 validated and linked the development plugin, fetched its managed Node.js 24 runtime, launched `bin/plugin.js`, detected a connected 5×3 Stream Deck, and logged `[com.bl4ut0.safespeak] Plugin connected`. A live SafeSpeak process also returned a valid status response through the current-user named pipe. The profile store was checked afterward and contained no SafeSpeak action or profile entry.

This verifies plugin loading and app communication, not physical button behavior. Per the product requirement, no action was placed on the user's existing profile; the streamer or a sighted helper must choose and position buttons.
