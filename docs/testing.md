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

The simulator listens only on `127.0.0.1:21213` and sends benign fixtures for normal chat, spaced block-term text, an invisible character, mixed Cyrillic/Latin text, a non-Latin message, a gift event, and malformed chat data. Arm TTS in the app to let approved chat enter the queue. This build deliberately does not produce audio.

## Stream Deck desktop test

Build and validate the plugin, install and launch Stream Deck 7.1 or later, and then run:

```powershell
cd streamdeck
npx streamdeck link com.bl4ut0.safespeak.sdPlugin
```

Use a disposable test profile for development. Confirm that SafeSpeak appears as an action category, each chosen action can be added manually, toggle state changes only after an acknowledged response, and buttons show an alert when the app is closed. Also confirm the plugin has not added or switched any profile.

Real TikFinity traffic, actual Stream Deck hardware, NVDA/JAWS/Narrator, audio routing, floods, fuzzing, installer upgrades, and classifier regression corpora remain required before release.
