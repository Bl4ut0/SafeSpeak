# Main release and later track

This split keeps SafeSpeak simple enough to operate non-visually under live-stream pressure while preserving a deliberate path for advanced features.

## Repository audit result

The audited `main` checkout did not contain a user-facing channel system or a moderation-review queue. `HeldForManualReview` was a dormant enum value; approved messages went directly to the bounded speech queue and rejected messages were redacted. No channel or review UI therefore needed destructive removal.

The interface did contain roughly forty focus targets across expert moderation, event announcement, dual-route audio, model, and accessibility controls. The primary redesign limits the release workflow to four areas; cleanup and acceptance are still in progress.

## Main release

This table is the release target, not a statement that every row is already complete.

| Area | Included behavior | Release rule |
| --- | --- | --- |
| Live | Automatic source connection, Arm SafeSpeak, Pause TTS, Manual Mode, Speak Next Approved Message, Stop Current Speech, Clear Queue, Emergency Stop, spoken status, and redacted activity | Connect automatically but always start disarmed; disarmed events are dropped before moderation, activity, logging, or TTS |
| Safety | Mandatory deterministic rules, selected local MiniLM/ONNX classifier with fallback, intent-strength slider, custom banned terms, and a side-effect-free filter test | No setting may disable severe built-in rules; report only the model actually selected and runnable |
| Voice | One main route, accessible voice selection and Preview, speed, volume, and supported voice-pack import after a real backend passes its gates | Never show a voice without a working synthesis path; Kokoro must be pinned and verified or deferred |
| Settings | Independent built-in spoken guidance, Light/Dark/High Contrast theme, reader speed, explicit local-log consent, Run Setup Again, safe data reset, and keyboard reference | Full keyboard and external screen-reader access; logging is off until explicitly enabled |
| Onboarding | First-launch spoken-guidance and Light/Dark/High Contrast choices; matching second-launch confirmation followed by platform, connector-detection, and enhanced-filter setup | A pending first choice is not treated as confirmed; no disconnected model download may report success |
| Connectors | Generic descriptor/registry/normalized-event contract; TikFinity adapter | New providers cannot bypass the moderation pipeline |

There is no manual moderation queue in the main release. An approved message may enter speech only while SafeSpeak is armed. Approved chat includes its independently moderated viewer name; an unsafe name becomes `A viewer`. A rejected message is represented by a redacted outcome and never exposes hostile text through UI Automation.

## Later track

- Channel, workspace, or multi-room management
- Manual moderation review and any use of `HeldForManualReview`
- Per-event routing for gifts, follows, shares, subscriptions, joins, and likes
- Audience-tier filters, cooldown tuning, language policy, and other expert moderation controls
- Direct TikTok authentication and every additional platform connector
- Unsupported pack formats, additional synthesis engines, voice-pack export, and remote voice endpoints
- Separate private-monitor routing, mirroring, meters, and virtual-cable guidance
- Stream Deck's full action matrix and authenticated local control redesign
- Simulator UI, diagnostics bundles, and advanced support tooling
- Alternate local-LLM or cloud moderation engines, downloadable model switching,
  and advanced diagnostic/export logging; these require explicit consent, protected
  credential storage, pinned assets, bounded diagnostics, and accessible
  failover controls before they can enter the main release

The later track may retain code in the repository for compatibility, but it must not add focus targets or settings to the main interface until it passes the graduation rules below.

## Feature graduation rules

A later-track feature can move into the main release only when:

1. A blind user can complete its primary task with keyboard alone.
2. It exposes a stable UI Automation name, role, state, and written status.
3. It works at 100%, 200%, and 400% scaling and Windows High Contrast.
4. Failure is safe, redacted, recoverable, and explained without requiring sight.
5. Persistence errors are surfaced.
6. Core behavior has deterministic tests and provider changes have privacy-safe fixtures.
7. The feature reduces more complexity than it adds to the primary workflow, or is placed behind an explicitly advanced surface.

## Implementation status

- [x] Audit the tracked feature surface and confirm no channel/review UI exists.
- [x] Make moderation strength affect real intent decisions; keep banned rules mandatory.
- [x] Bundle a hash-verified local MiniLM ONNX moderation model with deterministic fallback.
- [x] Speak independently moderated viewer names for chat and other events.
- [x] Add view/remove support for custom banned terms.
- [x] Introduce `ISourceConnector`, descriptors, registry, normalized events, auto-connect, reconnect, cancellation linkage, and a payload limit.
- [x] Replace the settings-heavy shell with Live, Safety, Voice, and Settings.
- [x] Add WPF `LiveRegionChanged` events, stateful Arm semantics, deterministic initial focus, and shortcut-registration warnings.
- [x] Keep alternate moderation engines and raw transcript logging dormant and
  non-persistent in the main release; legacy settings cannot silently enable them.
- [x] Add x64 and arm64 CI packaging coverage.
- [ ] Complete independent spoken-guidance plus Light/Dark/High Contrast settings and the two-launch onboarding/platform flow.
- [ ] Replace the old playback action with Pause, Manual, Speak Next, Stop Current Speech, and Clear Queue; prove Emergency Stop and disarmed intake.
- [ ] Present truthful enhanced-filter status without activating the disconnected downloader.
- [ ] Complete the side-effect-free filter test and explicit opt-in local audit logging.
- [ ] Add a supported, atomic, accessible voice-pack import path or keep import unavailable.
- [ ] Complete human assistive-technology and scaling acceptance tests.
- [ ] Add UI Automation regression tests and settings schema migrations.
- [ ] Pin and verify the Kokoro asset checksum.
- [ ] Sign and certify the final Store artifacts.

## Release test plan

### Automated on every change

- Release build with zero warnings
- Full xUnit suite
- Anti-evasion corpus and custom banned terms at every moderation level
- Bundled-model checksum/load test plus clean, hostile-profanity, repeated-letter, spaced-letter, zero-width, and hostile-name cases
- Different decisions at Relaxed, Balanced, Strong, and Maximum
- First-launch pending guidance/theme choice, matching second-launch confirmation, mismatch reset, platform/filter continuation, and Settings override
- Connector registration, normalized event parsing, malformed input, payload limit, cancellation, and reconnect behavior
- Playback-state transitions, queue bounds, one-at-a-time manual advance, Emergency Stop cancellation, disarmed event dropping, explicit preview, IPC validation, and package traversal rejection
- x64 and arm64 ZIP/MSIX build workflows

### Keyboard-only acceptance

1. Complete first launch, exit, reopen, and confirm built-in spoken guidance with each of Light, Dark, and High Contrast; continue through platform and filter setup.
2. Reach Arm from startup focus, hear status, pause/resume, use manual Speak Next, stop current speech, clear pending speech, use Emergency Stop, and recover focus.
3. Change moderation strength and add/remove a banned phrase without a mouse.
4. Review approved and rejected redacted activity.
5. Change output and voice, run Preview selected voice, and return to Live.
6. Unplug or stop the source, hear reconnecting state, restore it, and verify automatic recovery.

### Assistive-technology acceptance

Run the keyboard plan with Narrator, NVDA, and JAWS at 100%, 200%, and 400% scaling in Light, Dark, High Contrast, and Windows High Contrast. Verify exactly one useful live announcement per state change, current Toggle state, every changed list option, no hostile content exposure, no clipped control, and focus recovery after every dialog or failure.

### Release environment

Test clean install, upgrade, repair, uninstall, settings preservation, offline launch, missing audio device, clean Windows user, x64, arm64, signed MSIX, Windows App Certification Kit, and Store identity metadata. Existing unsigned artifacts are not release candidates.
