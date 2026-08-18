# SafeSpeak Stream Deck plugin

This Windows-only Node.js 24 plugin exposes SafeSpeak controls in Stream Deck 7.1 or later. It talks to the running SafeSpeak app through the current user's `SafeSpeakControl` named pipe; it does not open a network port.

## Actions

- Arm or disarm TTS
- Toggle automatic playback
- Play next approved message
- Skip current message
- Pause or resume the queue
- Clear the queue
- Emergency stop and clear
- Report status
- Cycle audience mode
- Cycle moderation strictness
- Toggle English-only mode
- Play a configurable preset message

Toggle buttons use the state returned by SafeSpeak after each request. A failed or disconnected command displays Stream Deck's alert indicator.

The manifest intentionally contains no `Profiles` section. Installation adds these actions to the SafeSpeak category only. A sighted helper should add and position selected buttons in the user's existing profile.

## Develop and test

```powershell
npm ci
npm run check
npm test
npm run build
npm run validate
```

After installing and launching Elgato Stream Deck desktop, link the development build:

```powershell
npx streamdeck link com.bl4ut0.safespeak.sdPlugin
```

Then restart the plugin or Stream Deck, drag SafeSpeak actions into a test profile, and run the SafeSpeak Windows app. The `Play Preset Message` action exposes its message in Elgato's property inspector.

The generated plugin is under `com.bl4ut0.safespeak.sdPlugin`. Run `scripts/generate-assets.ps1` to regenerate its marketplace PNG icons.
