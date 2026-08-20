# SafeSpeak product and release plan

## Goal

Ship one accessible Windows desktop application that runs alongside TikFinity, prevents unapproved chat from reaching TTS, gives blind and sighted streamers direct playback control, and supports Microsoft Store distribution without requiring a cloud subscription.

## Implemented foundation

- TikFinity localhost chat connection and offline emulator.
- Deterministic anti-evasion moderation plus optional local contextual heuristics.
- Safe author substitution and rejected-content redaction for both speech and UI Automation.
- Bounded queue, manual/automatic playback, safety arming, skip, pause, clear, and emergency stop.
- Windows speech, optional offline voice packages, and selectable broadcast endpoint.
- Persisted audio, voice, speech, blocked-term, and moderation preferences.
- Two-matching-answer integrated-reader setup, settings override, keyboard navigation, UI Automation, and High Contrast-compatible UI.
- Separate Stream Deck plug-in with 13 controls and no profile mutation.
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
2. Restore TikFinity gift, follow, share, subscription, join, and like event parsing with independent application and Stream Deck toggles.
3. Implement truly independent broadcast and private-monitor endpoints, mirroring, route tests, missing-device fail-closed behavior, and TikTok LIVE Studio guidance.
4. Replace or formally constrain the legacy loopback Stream Deck transport with a per-user authenticated channel and expand back to the planned control inventory.
5. Add privacy-safe diagnostics export, connector/audio self-test, and redacted support bundles.
6. Add signed model manifests/checksums and clear license/source metadata for every downloadable voice or classifier asset.

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
