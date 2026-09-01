# SafeSpeak product and execution plan

## Product goal

Ship one calm Windows application that a blind or low-vision streamer can operate under pressure, while incubating Android and iOS companions on isolated development tracks. SafeSpeak receives normalized livestream events, rejects unsafe speech locally, and keeps the streamer in control.

The first release optimizes for one source, one speech route, one automatic moderation path, and four areas: Live, Safety, Voice, and Settings. See [release tracks](release-tracks.md) for the scope boundary.

## Release principles

1. **Safe by default.** Connect automatically, but start disarmed. Severe banned rules cannot be disabled.
2. **One obvious path.** Arm starts approved playback; Emergency Stop cancels, clears, and disarms.
3. **Accessible state, not a separate accessible mode.** Sighted and non-sighted users receive the same written state through visuals and UI Automation.
4. **Intent strength, not model jargon.** Relaxed, Balanced, Strong, and Maximum must produce measurably different decisions.
5. **Provider adapters normalize; the core moderates.** No connector or platform bypasses the shared pipeline.
6. **No pretend features.** A voice, connector, or setting is not shown until its end-to-end path works and fails safely.

## Verified foundation in the current tree

- Keyboard-first WPF shell with deterministic initial focus and four arrow-key tabs
- Real WPF `LiveRegionChanged` events, dynamic accessible names, stateful Arm Toggle pattern, and unavailable-hotkey reporting
- Higher-contrast component borders, wrapping primary controls, and a 420-DIP minimum width
- Pinned 23M-parameter MiniLM ONNX classifier with verified model/tokenizer hashes, six toxicity labels, and four tested product thresholds
- Deterministic heuristic fallback when model assets or ONNX inference are unavailable
- Unicode normalization, invisible-character and homoglyph defenses, script checks, severe built-in terms, and custom banned terms
- Always-on, separately moderated viewer-name attribution; unsafe names are replaced with `A viewer`
- Add, review, duplicate feedback, and remove for custom banned terms
- Redacted rejected-message text, author, match, and screen-reader summaries
- Generic source descriptor, registry, normalized event contract, automatic TikFinity connection/reconnection, cancellation linkage, and 256 KB payload bound
- Serialized incoming event processing
- Installed Windows speech plus development foundations for Kokoro and validated voice-package archives
- Main output, voice preview, speed, and volume foundations
- Bounded speech queue and Emergency Stop foundations; the state model and real-engine cancellation still require revision
- x64 and arm64 CI packaging matrix

## Current release redesign

These items are planned or in progress and are not completed-feature claims:

- Separate built-in spoken guidance from the visual themes named Light, Dark, and High Contrast
- Use the first launch to save pending guidance/theme choices, then confirm them on the second launch before platform, connector-detection, and enhanced-filter setup
- Replace the old Skip behavior with Pause TTS, Manual Mode, Speak Next Approved Message, Stop Current Speech, and Clear Queue
- Drop source events before moderation, activity, logging, or TTS whenever SafeSpeak is disarmed
- Report only the moderation model that is actually verified, selected, and runnable; never present a disconnected model download as enhanced filtering
- Add a side-effect-free filter test, explicit local-log consent, Run Setup Again, and a separate data reset
- Add accessible voice-pack import only after at least one pack backend can synthesize, preview, persist, cancel, and roll back safely
- Complete the Light, Dark, High Contrast, keyboard, screen-reader, scaling, and shutdown acceptance gates

## Execution plan

### Phase 1 — release-core implementation

Status: in progress. The authoritative package-level status is in the [implementation and execution plan](implementation-execution-plan.md).

- Simplify the interface and isolate later-track controls.
- Complete two-launch onboarding, playback-state controls, moderation strength, banned-term management, live regions, focus, and connection lifecycle.
- Complete the theme selector, filter test, opt-in logging, setup reset, and supported voice-pack path without exposing unfinished engines.
- Document connector and feature-graduation rules.
- Add regression tests and keep both Windows architectures buildable.

### Phase 2 — acceptance and hardening

Status: next.

- Run blind-user task sessions with Narrator, NVDA, and JAWS.
- Test 100%, 200%, and 400% display/text scaling with Light, Dark, High Contrast, and Windows High Contrast.
- Add automated WPF UIA tests for focus order, names, Toggle state, tab selection, and exactly-once live announcements.
- Verify the versioned settings schema, migrations, validation, and debounced persistence.
- Surface audio-device disappearance and connection failure as designed, recoverable states.
- Add privacy-safe connector fixtures from supported TikFinity releases.
- Expand the local-model evaluation corpus with real, consented, de-identified livestream language and publish false-positive/false-negative results.
- Pin and verify the Kokoro asset with an approved checksum or signature.

### Phase 3 — release candidates

- Clean install, upgrade, repair, uninstall, rollback, and settings-preservation tests
- Clean-machine x64 and arm64 smoke tests
- Windows App Certification Kit
- Final iconography and Store screenshots after assistive-technology sign-off
- Package identity, publisher, signing, privacy policy, support URL, age rating, and full-trust justification
- Signed ZIP/MSIX hashes and release manifest

### Phase 4 — later track

- Additional connectors following [connector development](connector-development.md)
- Android and iOS test builds following [mobile development tracks](mobile-development-tracks.md), using native system TTS without requiring TikFinity
- Additional unsupported voice engines and remote voice endpoints
- Advanced routing, event channels, Stream Deck hardening, diagnostics, and optional manual review

## First-release non-goals

- Direct TikTok authentication without TikFinity
- Deleting chat, banning accounts, or replacing platform moderation
- Manual moderation review
- Channel/workspace management
- Cloud-required moderation
- User-adjustable disabling of the intent layer
- A guarantee that every abusive or evasive utterance will be detected
- Presenting unimplemented custom voice engines as available

## Release decision

A build is not a release candidate until automated tests pass, both CI architectures package, hostile content remains redacted, and the full keyboard plan passes with Narrator, NVDA, and JAWS. Visual polish alone cannot waive an accessibility or safety gate.
