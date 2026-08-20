# SafeSpeak product and release plan

## Goal

Ship one accessible Windows desktop application that runs alongside TikFinity, prevents unapproved chat from reaching TTS, gives blind and sighted streamers direct playback control, and supports Microsoft Store distribution without requiring a cloud subscription.

## Implemented foundation

- TikFinity localhost connection for chat, gift, follow, share, subscription, join, and like events, plus an offline emulator.
- Deterministic anti-evasion moderation plus optional local contextual heuristics.
- Safe author substitution and rejected-content redaction for both speech and UI Automation.
- Bounded queue, manual/automatic playback, safety arming, skip, pause, clear, and emergency stop.
- Windows speech and 27 optional Kokoro offline neural English voices with real ONNX synthesis.
- Independent broadcast/private endpoints, approved-message mirroring, and redacted private moderation notices.
- Persisted audio, voice, speech, blocked-term, and moderation preferences.
- Two-matching-answer integrated-reader setup, settings override, keyboard navigation, descriptive arrow-key tab-group guidance, UI Automation, Windows High Contrast support, and a saved extra-high-contrast theme.
- Separate Stream Deck plug-in with 24 controls and no profile mutation.
- Reproducible self-contained ZIP/MSIX builds, CI verification, release hashes, and WinGet manifest generation.

## Safety defaults

- Start disarmed.
- Start with automatic playback disabled.
- English writing-system and mixed-script checks enabled.
- Severe built-in terms cannot be overridden by user allow-list entries.
- Usernames are not spoken unless enabled and always pass a separate safety check.
- Queue capacity is 50; overflow does not evict previously reviewed items.
- Rejected message text, unsafe names, and exact matched hostile terms are not exposed to Narrator.
- Models and voices are downloaded only after explicit user action.

## Next functional milestones

1. Add probabilistic language identification with understandable confidence controls and regression data for misspellings and code-switching.
2. Validate supported TikFinity event shapes against release versions and add captured, privacy-safe compatibility fixtures.
3. Add route test tones, written meters, missing-device fail-closed behavior, and TikTok LIVE Studio setup guidance.
4. Replace or formally constrain the loopback Stream Deck transport with a per-user authenticated channel.
5. Add privacy-safe diagnostics export, connector/audio/voice self-tests, and redacted support bundles.
6. Enforce signed model manifests/checksums in addition to the documented source and license metadata.

## Accessibility and UI release gates

- Complete blind-user task testing with Narrator, NVDA, and JAWS.
- Verify every task at 100%, 200%, and 400% scaling and under Windows High Contrast themes.
- Automate focus-order, accessible-name, and live-region regression checks where practical.
- Add designed empty, loading, reconnecting, offline, download, and route-failure states.
- Verify that rejected content never reaches integrated speech or external screen readers.
- Let a sighted helper place selected actions in the streamer's existing Stream Deck profile; never automate profile changes.

## Store and deployment gates

- Reserve SafeSpeak in Partner Center and use the assigned package Identity Name and Publisher.
- Replace generated development assets with final production artwork and Store screenshots.
- Provide privacy policy, support URL, age rating, accessibility evidence, and `runFullTrust` justification.
- Test clean install, upgrade, repair, uninstall, app-data preservation, and rollback.
- Run Windows App Certification Kit and clean-machine x64/arm64 smoke tests.
- Produce a Store candidate with a zero revision component and upload through Partner Center.
- Publish WinGet metadata only after the exact signed public artifact and hashes exist.

## Out of scope for the first release

- Direct TikTok authentication without TikFinity.
- Deleting chat messages, banning users, or moderating TikTok accounts.
- Automatic Stream Deck profile creation or editing.
- Cloud-required moderation.
- A guarantee that every abusive or evasive utterance will be detected.
