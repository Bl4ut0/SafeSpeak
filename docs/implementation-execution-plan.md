# SafeSpeak implementation execution plan

This is the authoritative, living plan for the SafeSpeak blind-first redesign. It exists so work can continue safely across multiple Codex, Gemini, or human sessions without relying on chat history.

## Document control

| Field | Value |
| --- | --- |
| Plan status | Implementation in progress |
| Last updated | 2026-08-31 |
| Current release baseline | 0.1.0.4 |
| Working tree | Composite uncommitted work from the user, Gemini, and Codex; never reset wholesale |
| Current checkpoint | Five-step Reader/Theme onboarding, accessible Settings routing, eight-action Stream Deck surface, pause-bypass queue, donor eligibility, conditional neural-voice installation, and obsolete monitor removal are integrated; 273 Core plus 80 App contracts pass; rendered acceptance remains open |
| Next implementation step | Launch the current Release build against the emulator and manually validate Reader Y/N, Settings traversal, pause-all versus event bypass, and donor eligibility; then return to the LIVE-002 post-moderation disarm race |
| Completion rule | A step is complete only when its acceptance evidence is recorded here |

## How every future session must use this document

1. Read this entire document before changing code.
2. Read git status --short and the latest Session log entry. Existing changes belong to the user unless proven otherwise.
3. Start from the single Next implementation step above. Do not jump ahead because a later package looks easier.
4. Change that step to IN PROGRESS before editing.
5. Keep one implementation step in progress unless parallel work touches clearly separate files.
6. After each step, record changed files, commands, results, unresolved risks, and the next exact action.
7. Mark a step DONE only after all acceptance criteria pass. Code written is not completion.
8. If interrupted, update Resume checkpoint and Session log before stopping whenever possible.
9. Do not delete or revert mixed working-tree changes as Gemini cleanup. Use CLEAN-001 through CLEAN-004.
10. Do not create a release candidate until every P0 gate in this document is complete.

Status values:

- NOT STARTED - no implementation work has begun.
- IN PROGRESS - implementation or evidence is incomplete.
- BLOCKED - an external dependency or recorded decision prevents progress.
- DONE - implementation and all acceptance checks passed.
- DEFERRED - deliberately outside the main release, with a reason recorded.

## Parallel execution queue

Parallel work is allowed only when file ownership is disjoint and the
coordinator integrates every result against this plan. The runtime currently
allows four active agents including the coordinator, so work runs in batches
of three subagents rather than by creating overlapping duplicates.

### Active batch

| Lane | Scope | Exclusive files | Required handoff |
| --- | --- | --- | --- |
| LIVE-CORE | DONE - the existing queue state owner passed five added race/transition contracts without production changes | tests/SafeSpeak.Core.Tests/TtsQueueTests.cs | 17/17 focused tests pass; real-engine LIVE-010 evidence remains |
| LOG-LIFECYCLE | DONE - retained and hardened the existing logger | src/SafeSpeak.Core/Logging/*; tests/SafeSpeak.Core.Tests/StreamAuditLoggerTests.cs | 11/11 tests cover midstream enable/disable, reconnect, bounded burst, disk failure, flush, and shutdown |
| SAFE-TEST-SERVICE | DONE for automated isolation; runtime focus/speech remains SAFE-004/005 | ModerationTestService production/test files | 8/8 tests prove production-equivalent privacy-safe evaluation with no live side-effect dependencies |
| VOICE-PACK-ATOMIC | DONE for ZIP import; alternate creator path remains outside the exposed UI | VoicePackageManager and focused tests | 9/9 tests cover traversal, archive bomb, invalid/cancel/collision rollback, replacement, and cleanup |
| COORDINATOR | DONE for this batch | AppSettings, MainViewModel/MainWindow integration, plan evidence | Schema 5 consent migration, immediate connected-session logging, exact path/failure UI, and full 340-test verification pass |
| LIVE-CONNECTOR | DONE | TikFinityWebSocketClient and lifecycle tests | 5/5 loopback tests cover reconnect, oversize, cancellation, remote close/backoff, and idempotent disposal |
| LIVE-SAPI-CANCEL | IN PROGRESS | Modular/System speech engines and focused tests | 4/4 deterministic exact-instance cancellation tests pass; real audible SAPI and Kokoro evidence remains LIVE-010 |

### Queued second batch

The coordinator replaces completed lanes with these tasks after reviewing the
first-batch evidence. Exact production files are assigned only after the
contract agents report defects.

1. ONB-FUNCTIONAL - DONE for persisted resume state: schema v4 connector
   consent/status/summary and review-model resume are covered by Core and
   source contracts. Real WPF keyboard/focus evidence remains.
2. THEME-VERIFY - remediate reported resource defects and prepare the
   100/200/400 percent visual checklist without changing onboarding code.
3. SHELL-REMEDIATE - DONE: fixed only contract-proven MainWindow focus, name,
   list, help, and hidden-tab defects without changing Core state owners.
4. RELEASE-CONTRACT - DONE for static entry-point evidence: six contracts prove
   both suites run through Build-Release and the emulator is excluded.

Shared constraints for every lane:

- Do not edit this plan from a subagent; the coordinator records evidence.
- Do not edit a file owned by another active lane.
- Do not mark human Narrator/NVDA/JAWS or scaling evidence complete from static
  inspection.
- Keep AppSettings, TtsQueue, the connector, and ModerationPipeline as their
  single existing state owners.
- Do not work around the failed Windows Computer Use helper with
  terminal-driven UI automation.

## Product outcome

SafeSpeak is a calm Windows application that a blind or low-vision streamer can operate entirely by keyboard while live. It connects to a selected local livestream source, moderates usernames and messages locally, and speaks only approved content. Sighted users receive a polished interface without creating a separate or reduced screen-reader experience.

The main application has four areas:

1. Live - source state, armed/playback state, approved queue, activity, and emergency controls.
2. Safety - moderation strength, model status, banned terms, safe pattern rules, and a side-effect-free filter test.
3. Voice - voice selection/preview, speed/volume, and validated local voice-pack import.
4. Settings - theme, spoken guidance, language/audience eligibility, event and pause routing, audio routing, local audit logging, setup rerun, data reset, and keyboard help.

Channel/workspace management and a manual moderation-review queue remain outside the main release. The moderation agent makes the release-time decision.

## Locked product and interaction decisions

### Theme names

The displayed theme names are exactly:

- Light
- Dark
- High Contrast

Windows High Contrast overrides application colors with Windows system brushes. The saved SafeSpeak theme returns when Windows High Contrast is turned off.

### First-run onboarding

Onboarding is an accessible first-launch wizard, not custom installer UI. It uses standard WPF controls and works with Tab, Shift+Tab, arrow keys, Enter, Space, Escape, and access keys.

First launch:

1. Step 1 Reader asks the direct Yes/No question, "Do you want to use the SafeSpeak built-in screen reader?" Y chooses Yes and N chooses No.
2. Step 2 Theme independently offers Light, Dark, and High Contrast with arrow-key selection.
3. Save the Reader/Theme pair as pending, explain the two-launch protection, and close SafeSpeak.

Second launch:

1. Repeat Step 1 Reader and Step 2 Theme so the user explicitly confirms the pending pair. A changed choice becomes the new pending pair and requires another restart.
2. Continue to Step 3 Platform without making the user repeat the whole wizard:
   - select TikFinity / TikTok for the first release;
   - choose Auto-detect local streaming connectors (Recommended) or manual setup;
   - detection checks only approved local processes/endpoints after consent;
   - it never arms TTS, authenticates remotely, or scans unrelated files.
3. Continue to Step 4 Filtering:
   - recommended by default but downloaded only after explicit confirmation;
   - state download size, disk use, offline behavior, and privacy;
   - if the verified bundled ONNX model already fills this role, report it as installed instead of presenting a fake download.
4. Step 5 Review presents every selection in written text and through UI Automation, saves, and enters the main application.

After completion, startup no longer asks. Settings always offers Run Setup Again.

### Live operating states

SafeSpeak uses explicit written states, not only colors or icons.

| State | Source/event handling | Queue | Speech | Primary action |
| --- | --- | --- | --- | --- |
| Disarmed | Source may remain connected, but new events are discarded before moderation/feed/log/TTS | No intake | Off | Arm SafeSpeak |
| Armed - Auto | Active | Approved items enter a bounded queue | FIFO automatic | Pause TTS or Manual Mode |
| Armed - Paused | Active | Approved items continue accumulating within capacity | Current item may finish; ordinary chat waits; only explicitly selected gift/follow/share/subscription bypasses may speak | Resume Auto or Manual Mode |
| Armed - Manual | Active | Approved items continue accumulating within capacity | One item per Speak Next Approved Message | Resume Auto |
| Emergency stopped | Source may remain connected, event handling off | Cleared; no intake | Current speech stopped | Re-arm SafeSpeak |

Pause TTS does not cut off the current item; Stop Current Speech does. The user can choose Pause All TTS or allow specific moderated event types through the serialized pause-bypass lane. Clear Queue removes both ordinary and bypass-pending items without interrupting current speech. Emergency Stop always stops and clears both lanes.

The old Skip concept is removed from the main interface and shortcut map. It is replaced by Stop Current Speech and Speak Next Approved Message.

Emergency Stop immediately:

1. stops current synthesis and playback;
2. clears pending TTS items;
3. disarms SafeSpeak and resets to a defined empty Auto/unpaused state for the next re-arm;
4. prevents new provider events from entering moderation/feed/log/TTS;
5. presents and announces: Speech stopped. Queue cleared. SafeSpeak is disarmed.;
6. requires explicit Re-arm SafeSpeak.

No confirmation dialog may delay Emergency Stop.

### Safety behavior

- Usernames and messages are independently normalized and moderated before speech.
- An unsafe username becomes A viewer.
- Hostile original text must not leak through feed, UI Automation, built-in guidance, errors, or logs unless explicit raw logging is enabled.
- Moderation strength is Relaxed, Balanced, Strong, or Maximum and produces measurably different results.
- Severe built-in terms cannot be disabled.
- Custom banned terms use an accessible selection list.
- Editable custom pattern rules ship only with validation, length/count bounds, compiled timeouts or non-backtracking execution, and ReDoS tests. Otherwise Safety shows built-in pattern protection read-only.
- Test a message uses the production normalization/rules/model path in an isolated context with no queue, cooldown, speech, connector, feed, or logging side effects.
- Test results state Allowed or Blocked plus a safe reason category. Test input never enters Live activity.

### Voice packs

- Installed voices appear in an accessible selection list.
- Arrowing through the list announces each option name, state, and position exactly once.
- A voice sample plays only through explicit Preview; list navigation does not synthesize an unexpected full sample.
- Import Voice Pack validates before installation, blocks traversal/oversize archives, reports progress/failure accessibly, and never partially replaces a working pack.
- A pack is selectable only when a real supported synthesis backend can execute it. Imported code or arbitrary scripts are never run.

### Local audit logging

- Checkbox name: Save chat and moderation decisions to a local text log.
- It is off by default.
- Nearby copy warns that logs can contain usernames, raw chat, and blocked text.
- Enabling is explicit persisted consent.
- Show the exact log directory and provide Open Logs Folder.
- Logging is bounded and cannot block moderation, TTS, or shutdown.

### Reset behavior

- Run Setup Again is non-destructive.
- Reset All SafeSpeak Data is a separate destructive action with an accessible confirmation listing exactly what will be removed.
- Imported voices and audit logs are preserved by default or selected explicitly; they are never silently deleted.
- Reset ends in a deterministic close/restart path and returns to onboarding.

## Accessibility contract

### Keyboard and focus

- Startup focus lands on Arm SafeSpeak after setup or the first onboarding choice before setup.
- Tab and Shift+Tab move in logical task order with no hidden, decorative, disabled, or duplicate focus stops.
- Arrow keys change tabs, radio groups, sliders, ComboBox choices, and list selections using native Windows patterns.
- Enter and Space activate controls according to native roles; Escape closes a popup/dialog and restores logical focus.
- After add/remove/import/test/reset/dialog actions, focus returns to the changed item, result, recovery action, or initiating control.
- Background connection, model, or queue changes never steal focus.
- Every focusable control has a visible focus indicator at least 3 DIPs thick in all app themes and Windows High Contrast.

### Lists and selectable options

Tab enters each ListBox, ListView, or ComboBox once; Up/Down moves through items. Every item exposes:

- a unique accessible name with the option label;
- selected, checked, and disabled state through the correct UI Automation pattern;
- useful status such as Installed, Connected, or Invalid when relevant;
- position N of M when the reader does not derive it;
- no hostile raw text in its accessible name.

Selection changes raise the native UI Automation selection event. SafeSpeak built-in guidance announces a changed option but suppresses duplicate speech when an external reader already provides it. Item-container automation names must be bound explicitly for object-backed lists such as voices, audio devices, connectors, and imported packs.

### Names, roles, state, and help

- Prefer native Button, ToggleButton, RadioButton, CheckBox, Slider, TabControl, ListBox, ListView, ComboBox, and TextBox controls.
- Every input has a visible targeting Label or equivalent programmatic relationship.
- Automation names are short, unique, and never contain unmoderated content.
- Help text explains consequences and keyboard use without repeating the name.
- Adjacent icons are decorative in the accessibility tree.
- Meaning never relies on color, position, animation, or sound.

### Announcements

- Connection, armed mode, playback mode, queue count, test result, import result, and errors are written states.
- Live regions produce one useful announcement per meaningful transition.
- Queue-count churn is throttled.
- Emergency Stop and failed destructive actions interrupt; ordinary updates are polite.
- Built-in spoken guidance never reads unmoderated chat.

### Layout and scaling

- Complete every primary task at 100%, 200%, and 400% Windows scaling.
- Pages scroll vertically; primary tasks do not require horizontal scrolling.
- Primary targets remain at least 44 DIPs high.
- Text wraps instead of clipping.
- Reduced window sizes never cover or displace Emergency Stop.

## Repository state at plan creation

### Last known verified baseline

Before this planning pass, release 0.1.0.4 had:

- 196/196 automated tests passing;
- Release build with zero warnings and errors;
- verified x64 EXE and ZIP;
- two normal-close lifecycle checks under 500 ms with no orphan SafeSpeak or emulator process;
- duplicate-launch rejection and hardened release-pointer validation.

BASE-001 through BASE-004 must rerun these checks because the tree is uncommitted and may receive other edits.

### Foundation to preserve

- Hash-verified local MiniLM/ONNX moderation with heuristic fallback.
- Unicode, invisible-character, homoglyph, repeated-obfuscation, script, and severe-term defenses.
- Independently moderated usernames and safe attribution.
- ISourceConnector, descriptors/registry, normalized events, bounded TikFinity input, reconnect, and cancellation.
- Serialized bounded incoming-event processing.
- Bounded TTS primitives for arming, auto, pause, manual-next, stop, clear, and emergency flush.
- System voices, optional Kokoro foundation, and explicit preview output.
- Voice archive validation and traversal/size defenses.
- Bounded audit logger foundation.
- WPF live regions, focus narrator, native controls, global hotkeys, and bounded shutdown cleanup.
- Release scripts and pointer/hash/version validation.

### Known gaps and contradictions

- UI still exposes Skip; Pause, Manual, Clear, and Next are incomplete or hidden.
- TtsQueue.Enqueue accepts while disarmed, and an existing test requires later playback after re-arm. That conflicts with the locked invariant.
- Queue booleans allow racy combinations. Emergency does not clear paused state; repeated manual-next can overlap; real SAPI cancellation is not proven immediate.
- The focus narrator describes selectors on focus entry, not every Up/Down change. Voice has bespoke behavior; most audio, connector, and theme lists do not.
- Audio ComboBox uses DisplayName while AudioEndpointInfo exposes Name, so items may be blank.
- Current accessibility profile makes spoken guidance and theme mutually exclusive.
- Theme persistence is a high-contrast Boolean; Dark does not exist.
- Onboarding lacks platforms, detection consent, enhanced-model truth/status, review, resume, and reset.
- MainViewModel is over 1,300 lines and retains hidden secondary-track settings.
- Safety has no isolated test field or managed custom pattern surface.
- Voice imports are not connected to ModularTtsEngine; imported packs cannot synthesize.
- The optional enhanced-model downloader is not selected by the active classifier and cannot truthfully claim improved filtering.
- Audit logging is hidden and JsonIgnore; enabling during an active connection does not start a session. Writer/session lifecycle needs review.
- Settings have no schema version, migration chain, backup recovery, or data-reset contract.
- Dark, large-scale, and human screen-reader acceptance is incomplete.

## Gemini and mixed-change cleanup policy

Clean up Gemini changes means reconcile behavior with this plan, not discard files by authorship. The working tree contains mixed user, Gemini, and Codex work.

Every changed or untracked file is classified first:

- KEEP - approved working foundation.
- REVISE - useful code with incorrect UX, persistence, safety, or accessibility behavior.
- SECONDARY - retained outside the main UI, with no focus targets or silent activation.
- REMOVE - obsolete duplicate, stale command/UI/docs, or proven unreferenced implementation.

Cleanup rules:

1. Capture status, diff stat, and baseline tests first.
2. Search every removal candidate across source, tests, docs, installer, workflows, and IPC.
3. Add or update a regression test before deleting safety behavior.
4. Never remove local ONNX assets, connector normalization, shutdown guards, release validation, or hostile-content redaction.
5. Keep optional cloud moderation, local LLM endpoints, advanced routing/events, simulator UI, channel/workspace, and manual review out of the main release unless separately promoted.
6. Remove hidden focus targets and false documentation even if secondary Core code remains.
7. Replace old displayed terms everywhere: Default becomes Light; Boolean high contrast becomes theme preference; Skip becomes Stop Current Speech; Emergency Panic becomes Emergency Stop.
8. Keep deprecated IPC aliases only for a documented compatibility window.
9. Run exact-symbol scans and classify every intended remaining match.
10. Never use blanket reset, blanket checkout, or broad recursive deletion.

## Target architecture

Implement in small slices instead of replacing the whole application.

Core state and services:

- ThemePreference: Light, Dark, HighContrast with stable serialization and display names.
- OnboardingState: schema version, pending/completed status, guidance/theme confirmation, connector IDs, detection consent, enhanced-model consent/install status.
- PlaybackMode: Auto, Paused, Manual; combined with IsArmed in one serialized transition controller.
- TtsQueueSnapshot: armed, mode, speaking, count/capacity, and transition reason.
- SettingsStore: atomic save, schema, validation, migrations, backup recovery, and test path.
- ConnectorDiscoveryService: consent-gated bounded local detection with no connection or arm side effects.
- ModerationTestService: production-equivalent isolated evaluation.
- VoicePackImportService: validate, stage, smoke-test, atomic install/rollback, cancel, and report.
- DataResetService: enumerates and removes only selected SafeSpeak paths.

Application view models should become feature-focused: Onboarding, Live, Safety, Voice, and Settings. They share one queue, connector coordinator, moderation pipeline, and settings store. Extract incrementally; never create duplicate state owners.

Theme resources should be split into Themes/Base.xaml, Themes/Light.xaml, Themes/Dark.xaml, and Themes/HighContrast.xaml using semantic Window, Surface, Text, Border, Focus, Accent, Success, Warning, and Danger keys and DynamicResource references.

## Work breakdown and execution order

### Phase 0 - baseline and plan control

| ID | Status | Work | Acceptance evidence |
| --- | --- | --- | --- |
| PLAN-001 | DONE | Create and cross-check this living plan. | Document covers approved features, three independent audits, and one resume checkpoint. |
| BASE-001 | DONE | Capture git status, branch/HEAD, diff stat, SDK, and running SafeSpeak processes. | Evidence in Session log; no reset. |
| BASE-002 | DONE | Run full Release build and tests before redesign edits. | Exact test count and warnings recorded. |
| BASE-003 | DONE | Run release packaging and pointer validation without launching stale builds. | Artifact paths, versions, hashes recorded. |
| BASE-004 | DONE | Run close, duplicate-launch, and orphan-process harness. | Timings and process counts recorded. |

### Phase 1 - mixed-change cleanup and settings foundation

| ID | Status | Work | Acceptance evidence |
| --- | --- | --- | --- |
| CLEAN-001 | DONE | Assign KEEP, REVISE, SECONDARY, or REMOVE to every changed/untracked file. | All 70 entries are recorded in docs/change-classification.md: 35 KEEP, 25 REVISE, 6 SECONDARY, 4 REMOVE. |
| CLEAN-002 | DONE | Remove obsolete Skip/profile names, dead duplicates, stale hidden focus targets, and superseded docs after scans. | Plan-required scan classified remaining matches as migration tests/code, Stream Deck compatibility, installer SkipTests, or historical documentation; Release build and 222 tests pass. |
| CLEAN-003 | DONE | Isolate cloud/local-endpoint engines, advanced routing/events, simulator UI, channel/review concepts, and unsupported settings. | Primary WPF surface has no cloud/local-endpoint or simulator controls/activation; optional settings are session-only; scan, build, and tests pass. |
| CLEAN-004 | DONE | Split MainViewModel only where needed, preserving behavior through tests. | Playback WPF glue moved to a partial file; TtsQueue remains the sole state owner; Release build is green and 12/12 playback regressions pass. |
| SET-001 | DONE | Add versioned SettingsStore with atomic persistence, validation, and backup recovery. | Round-trip, corrupt-primary recovery, corrupt-all fallback, partial normalization, atomic replace/backup, future-schema, and custom-path tests pass. |
| SET-002 | DONE | Migrate old profile/high-contrast and legacy connector/model/log fields. | Legacy JSON tests pass; obsolete accessibility fields are not public; cloud and logging fields cannot silently load or persist. |
| SET-003 | DONE | Add pending/completed onboarding and safe reset APIs. | Durable OnboardingStage, combination confirmation/mismatch, legacy resume, and non-destructive onboarding reset tests pass. |

### Phase 2 - onboarding

| ID | Status | Work | Acceptance evidence |
| --- | --- | --- | --- |
| ONB-001 | DONE | Separate spoken guidance and theme while retaining two-launch confirmation. | Migration and confirmation-state tests pass. |
| ONB-002 | IN PROGRESS | Build wizard shell with progress, Back/Continue, validation, live status, and deterministic focus. | Five-step Reader, Theme, Platform, Filtering, Review sequence uses native controls, local tab scopes, and deterministic page focus; static and exact-once contracts pass; real keyboard traversal remains blocked by the host Windows automation helper. |
| ONB-003 | IN PROGRESS | First launch direct Reader Yes/No and Light/Dark/High Contrast choice, then close. | Reader is a direct Yes/No step with Y/N shortcuts, followed by three keyboard-selectable theme cards; pending-state and exact-once guards are tested; human/UIA narration remains. |
| ONB-004 | DONE | Second-launch confirmation, including mismatch/restart. | Matching and changed-combination restart tests pass. |
| ONB-005 | DONE | TikFinity/TikTok selection, consented local auto-detect, manual setup, and result list. | Detection requires consent and is local, bounded, cancellable, side-effect-free, and covered by focused tests. |
| ONB-006 | IN PROGRESS | Verify enhanced-model assets and add truthful consent/install/status. | Bundled hash-verified model and fallback contracts pass; real offline/resumed-Review UI evidence remains. |
| ONB-007 | IN PROGRESS | Review/save/complete plus Settings Run Setup Again. | Schema v5 persists safe connector detection review state; canceling a settings-time rerun restores its complete starting state; real focus-return evidence remains. |

### Phase 3 - themes and shell

| ID | Status | Work | Acceptance evidence |
| --- | --- | --- | --- |
| THEME-001 | IN PROGRESS | Create Light, Dark, High Contrast resource dictionaries and component styles. | Three semantic dictionaries, contrast audit, and shared Calm Clarity component system pass; 100/200/400% screenshots remain. |
| THEME-002 | IN PROGRESS | Follow Windows High Contrast while preserving saved theme. | Runtime override/restore is implemented; automated resource test and manual transition remain. |
| SHELL-001 | IN PROGRESS | Implement polished Live, Safety, Voice, Settings shell. | Four equal page tabs plus persistent Hear Status form one complete header row; Live owns operating state/actions, while Safety, Voice, and Settings follow the supplied compact reference hierarchy at a 720 by 700 growable default; real keyboard pass remains. |
| SHELL-002 | IN PROGRESS | Standardize controls, state cards, labels, lists, focus, empty/error states. | ComboBox shells, arrows, popups, and items now use semantic theme brushes; compact square page components and 65 app contracts pass; rendered Light/Dark screenshot, mouse, keyboard, UIA, and scaling evidence remains. |
| SHELL-003 | IN PROGRESS | Add selection narration for all lists, ComboBoxes, and sliders. | Item/slider guidance and no-auto-preview contracts pass; runtime exactly-once reader evidence remains. |
| SHELL-004 | DONE | Fix audio endpoint display and item-container UIA names. | AudioEndpointInfo.Name and explicit audio/voice ComboBoxItem UIA-name contracts pass. |

### Phase 4 - Live playback and connection

| ID | Status | Work | Acceptance evidence |
| --- | --- | --- | --- |
| LIVE-001 | DONE | Define armed plus PlaybackMode transitions and serialize consumers. | TtsQueue transition/race contracts reject disarmed mode changes and serialize consumers. |
| LIVE-002 | IN PROGRESS | Reject/drop incoming provider events while disarmed and replace conflicting re-arm test. | Intake and generation guards exist, but the final audit/feed commit still needs an atomic UI-thread recheck and an application-level side-effect test. |
| LIVE-003 | DONE | Add Pause, Manual, Resume Auto, Speak Next. | Bounded accumulation, exactly-one manual advance, and concurrent advance tests pass. |
| LIVE-004 | DONE | Add Stop Current Speech. | Pending queue and playback mode preservation tests pass; real engine evidence is tracked by LIVE-010. |
| LIVE-005 | DONE | Add Clear Queue. | Current speech continues while pending count becomes zero in focused tests. |
| LIVE-006 | IN PROGRESS | Add Emergency Stop and Re-arm semantics/focus. | Queue semantics and idempotent re-arm pass; collapsing armed controls now returns focus to Arm; real output evidence remains. |
| LIVE-007 | IN PROGRESS | Build safe activity and approved-queue lists with empty/full states. | Redacted activity list exists; approved-queue list and explicit empty/full states remain. |
| LIVE-008 | DONE | Integrate connector state/retry without arming. | Parser plus 5 loopback lifecycle tests cover malformed, reconnect, oversize, cancellation, remote close/backoff, and disposal. |
| LIVE-009 | IN PROGRESS | Replace Skip hotkey/UI/IPC wording; keep temporary alias only if documented. | Display/hotkeys are clean; IPC skip/panic aliases still need a documented compatibility window and tests. |
| LIVE-010 | IN PROGRESS | Prove real SAPI/Kokoro cancellation latency and idempotent shutdown. | Exact in-flight SAPI wave instance is now cancelled and 4/4 deterministic tests pass; real audible SAPI and Kokoro cancellation/shutdown evidence remains. |
| LIVE-011 | DONE | Add configurable event pause bypass and donor speaker eligibility without bypassing moderation. | A serialized priority lane speaks only selected gift/follow/share/subscription events while ordinary chat remains paused; Emergency Stop clears both lanes; current-session donors pass audience gating only when enabled; focused queue, moderation, and persistence tests pass. |

### Phase 5 - Safety

| ID | Status | Work | Acceptance evidence |
| --- | --- | --- | --- |
| SAFE-001 | NOT STARTED | Present strength and active local filtering layers in plain language. | Levels differ; fallback is written/announced. |
| SAFE-002 | NOT STARTED | Finish accessible banned-term add/browse/duplicate/remove. | Each item announced; built-ins immutable. |
| SAFE-003 | NOT STARTED | Add safe custom patterns or a truthful read-only built-in pattern summary. | Bounds, timeout/non-backtracking, persistence, ReDoS tests. |
| SAFE-004 | IN PROGRESS | Add Test a message and Test Filter with isolated evaluation. | The production pipeline is called with a blank cooldown identity and host-tier sample; contracts exclude queue, feed, connector, and log paths; runtime announcement/focus evidence remains. |
| SAFE-005 | NOT STARTED | Add privacy-safe result card, focus, and one live announcement. | Hostile input never enters UIA names or logs. |

### Phase 6 - Voice

| ID | Status | Work | Acceptance evidence |
| --- | --- | --- | --- |
| VOICE-001 | NOT STARTED | Redesign voice/audio selection and explicit Preview, speed, volume. | Each option announced; preview only on action. |
| VOICE-002 | NOT STARTED | Choose and implement at least one supported pack synthesis backend. | Imported supported pack synthesizes; unsupported type rejects. |
| VOICE-003 | IN PROGRESS | Make pack validation/install atomic with collision rollback. | ZIP import passes 9/9 traversal, bomb, invalid, cancel, collision, rollback, replacement, and cleanup tests; the unexposed CreateAndInstallPackageAsync path is still non-atomic. |
| VOICE-004 | NOT STARTED | Add accessible file picker and import progress/result UI. | Keyboard-only import and predictable focus return. |
| VOICE-005 | NOT STARTED | Refresh and use successful imported voices immediately. | Preview and persisted selection work. |
| VOICE-006 | NOT STARTED | Verify Kokoro source, checksum/signature, cancellation, and cleanup or defer it. | Pinned evidence or secondary-track move. |

### Phase 7 - Settings, logging, and reset

| ID | Status | Work | Acceptance evidence |
| --- | --- | --- | --- |
| SETUI-001 | DONE | Theme selector with exact names plus spoken-guidance control/status. | Immediate apply/persistence contracts pass; Settings now states saved on/off state and whether Windows speech initialized. |
| SETUI-002 | DONE | Keep Stream Deck minimal while exposing configuration in the accessible app. | Stream Deck advertises exactly eight live actions; Settings exposes language, audience, donor, event, pause, and output choices with a local 1-24 tab order; mandatory name and intent moderation are reported as always active. |
| LOG-001 | DONE | Persist explicit logging consent, default off, with privacy warning. | Schema 5 prevents legacy opt-in while explicit current consent round-trips. |
| LOG-002 | DONE | Repair logger writer/session/rotation/drop/shutdown behavior. | 11/11 tests pass for midstream enable/disable, reconnect, disk failure, burst, flush, and bounded close. |
| LOG-003 | DONE | Show path and Open Logs Folder with accessible result. | Exact path is visible/UIA-readable and failure is written and announced without crash. |
| RESET-001 | IN PROGRESS | Separate Run Setup Again and Reset All SafeSpeak Data. | Run Setup Again is non-destructive and cancel-safe with explicit preservation HelpText; Reset All remains intentionally unexposed until RESET-002 exists. |
| RESET-002 | NOT STARTED | Accessible reset confirmation with data categories and safe defaults. | Exact paths verified; no broad delete. |

### Phase 8 - automated and human accessibility verification

| ID | Status | Work | Acceptance evidence |
| --- | --- | --- | --- |
| TEST-001 | IN PROGRESS | Expand Core tests for migration, onboarding, playback, moderation test, patterns, voice, logs, reset, connectors. | 273 deterministic Core tests pass; reset-all, custom pattern, imported synthesis, and real-engine coverage remain. |
| TEST-002 | DONE | Add SafeSpeak.App.Contracts.Tests for XAML names, labels, TabIndex, hidden focus, item names, focus styles, and redaction bindings. | 85/85 contracts pass for onboarding, announcements, themes, shell, settings routing, Stream Deck, voice-install state, redaction, lifecycle wiring, release entry points, Store publisher guards, packaged legal notices, and deterministic hash-verified asset checkout. |
| TEST-003 | NOT STARTED | Add Windows UIA harness for focus, tab order, selection events, state patterns, dialogs, and live regions. | Machine-readable clean-Windows evidence. |
| TEST-004 | NOT STARTED | Keyboard-only script in every theme at 100, 200, and 400 percent. | Checklist/screenshots; no clipped or unreachable action. |
| TEST-005 | NOT STARTED | Run with Narrator, NVDA, and JAWS. | Options stated exactly once; no traps or hostile leaks. |
| TEST-006 | NOT STARTED | Lifecycle tests during model, connector, TTS, import, download, and logging. | Under five seconds; zero orphan processes. |

### Phase 9 - documentation and release candidate

| ID | Status | Work | Acceptance evidence |
| --- | --- | --- | --- |
| DOC-001 | NOT STARTED | Update README, product/UI/release plans, connector, moderation, voice-pack, logging/privacy, and keyboard docs. | No stale Default, Skip, Panic, or false feature claims. |
| REL-001 | NOT STARTED | Versioned zero-warning Release build and full tests. | Logs recorded. |
| REL-002 | NOT STARTED | Package pointer/hash/version and clean-machine x64 smoke. | Paths, versions, SHA-256 recorded. |
| REL-003 | NOT STARTED | Clean install, upgrade from 0.1.0.4, repair, uninstall, migration/preservation/reset. | Checklist complete. |
| REL-004 | IN PROGRESS | Signing, Store, and WACK only after accessibility sign-off. | Repository-side x64/ARM64 bundle builder and protected manual publisher workflow are verified; Partner Center identity, initial live submission, final WACK, signing, and certification remain. |

## P0 acceptance task script

Every release candidate completes this by keyboard, then with Narrator, NVDA, and JAWS:

1. Launch with no settings.
2. Hear or read onboarding purpose and current step.
3. Toggle built-in spoken guidance.
4. Arrow through Light, Dark, High Contrast and hear each option.
5. Close, reopen, and confirm the same accessibility choices.
6. Select TikFinity/TikTok, run local detection, and review every result.
7. Accept or decline enhanced filtering after hearing size/privacy details.
8. Review and save; land on Arm SafeSpeak.
9. Review connection without arming and prove events are discarded.
10. Arm Auto and receive approved/blocked mock events with moderated usernames.
11. Pause while approved items accumulate.
12. Switch to Manual and speak one at a time.
13. Stop current speech without losing pending items.
14. Clear pending queue.
15. Trigger Emergency Stop and verify stopped, cleared, disarmed, no intake.
16. Re-arm explicitly into Auto/unpaused.
17. Change and hear each moderation level.
18. Add, browse, and remove a banned term.
19. Add/browse a valid pattern and reject an unsafe one if editable rules ship.
20. Test allowed and blocked content with zero live side effects.
21. Browse voices, preview explicitly, import a valid pack, and recover from an invalid pack.
22. Change each theme in Settings.
23. Opt into logging after warning, verify path, then opt out.
24. Run setup again without unrelated data loss.
25. Cancel full reset, then perform selected reset categories.
26. Close during active speech/model/connector work and verify termination.

## Standard verification commands

Run from the repository root and record exact output:

    dotnet --version
    git status --short
    dotnet test tests/SafeSpeak.Core.Tests/SafeSpeak.Core.Tests.csproj -c Release
    dotnet build src/SafeSpeak.App/SafeSpeak.App.csproj -c Release -warnaserror
    git diff --check

After adding tests:

    dotnet test tests/SafeSpeak.App.Contracts.Tests/SafeSpeak.App.Contracts.Tests.csproj -c Release
    dotnet test tests/SafeSpeak.App.AccessibilityTests/SafeSpeak.App.AccessibilityTests.csproj -c Release --filter Category=Accessibility

Current portable release checks:

    ./installer/Build-Release.ps1 -Architecture x64 -Format Zip
    ./installer/Start-LatestBuild.ps1 -ResolveOnly
    ./installer/Build-Release.ps1 -Architecture arm64 -Format Zip

Full packaging where Windows SDK tools exist:

    ./installer/Build-Release.ps1 -Architecture x64 -Format Both
    ./installer/Build-Release.ps1 -Architecture arm64 -Format Both

Build-Release.ps1 must eventually run both Core and accessibility-contract tests. Interactive UIA remains a local/release-agent gate unless CI has a reliable interactive desktop.

Useful stale-symbol scans after migration:

    rg -n "Skip|SkipMessage|Emergency Panic|Default theme|UseHighContrastTheme|AccessibilityProfile|PendingAccessibilityProfile" src tests docs installer README.md
    rg -n "PerspectiveApiKey|LocalLlmEndpoint|SelectedIntentEngineId|HeldForManualReview|channel|moderation queue" src tests docs

Matches are not automatically errors. Classify them as current, compatibility, migration, secondary-track documentation, or stale.

## Resume checkpoint

Update this block at the end of every session.

- Checkpoint ID: DEVELOPMENT-CI-TRACK
- Status: COMPLETE; `develop` exists remotely and its first isolated development build is green.
- Last completed work: separated branch-triggered CI into a fast `develop` track and a full `main` release-integration track. Development CI runs both authoritative test suites through Build-Release, creates only an unsigned x64 portable ZIP/report, retains it for seven days, and has no publishing authority. GitHub run 33469553000 proved the branch routing and artifact path; no main-release or Store-publisher run was created for the `develop` push.
- Files changed in the follow-up: development-build workflow; main workflow branch scope; release-entry contract tests; README and packaging/development-track documentation; this checkpoint.
- Tests run: local App.Contracts passed 86/86 and diff integrity passed. GitHub Actions Development build 33469553000 passed in 2m23s and uploaded `SafeSpeak-development-e069bd9dcde89dc1ad47d363e7ec357a2ef7c13e-win-x64`, 240,421,272 bytes, expiring 2026-09-08. The preceding full-release baseline remains Core 273/273 and GitHub Actions run 33468750183 green for x64/ARM64 ZIP/MSIX packaging.
- Known blockers: real Light/Dark/High Contrast 100/200/400% keyboard/UIA evidence; Narrator/NVDA/JAWS passes; real audible SAPI/Kokoro cancellation; Partner Center-assigned app ID/identity/publisher and an initial certified live listing; Entra app registration with Partner Center Manager role; GitHub variables/environment secrets and reviewer; privacy-policy URL and listing/support/age-rating/screenshots; runFullTrust justification; final signed candidate and WACK report.
- Next exact action: restore working Partner Center browser control, inspect the existing submission without changing it, capture the Microsoft-assigned identity values, then prepare a new candidate only after the remaining accessibility and listing requirements are resolved. The 2026-09-01 control attempt failed before browser connection because Codex's Windows sandbox helper could not refresh; no Partner Center field or submission changed.
- Do not do next: do not use the audit bundle for submission; do not commit Partner Center secrets; do not enable push-to-Store publishing; do not commit a Store submission before reviewing its draft; do not run final WACK or submit before the P0 accessibility gates pass.

## Session log

### 2026-09-01 - isolated development CI track

- Added a permanent `develop` branch and a dedicated Development build workflow. Pushes and pull requests targeting `develop` run the authoritative release tests once, create only an unsigned x64 portable ZIP/report under `artifacts/development`, and retain the artifact for seven days.
- Scoped the Main release build's pull-request trigger to `main`; a pull request targeting `develop` can no longer enter the full x64/ARM64 ZIP/MSIX and Stream Deck matrix. Store publishing remains manual-only and protected.
- Added contract coverage that rejects Store commands, credentials, environments, MSIX format, or main-branch triggers in development CI. Local App.Contracts passed 86/86 and diff integrity passed.
- Created and pushed `develop`. GitHub Actions run 33469553000 completed successfully in 2m23s and uploaded the 240,421,272-byte development artifact, expiring 2026-09-08. GitHub reported no Main release build or Microsoft Store publisher run for that branch.
- A later attempt to inspect the signed-in Partner Center session through browser/computer control failed during runtime startup because the Codex Windows sandbox helper could not refresh. The required reset/retry also failed before any browser action, so no Store configuration, package, or submission was changed.

### 2026-08-31 - GitHub push and deterministic tokenizer checkout

- Committed the 115-file composite overhaul as 3fe2274 and pushed it to origin/main. Local and remote commit IDs matched and the working tree was clean. GitHub accepted the 86.76 MiB ONNX model with its expected over-50-MiB advisory warning; the file remains below GitHub's 100 MiB hard limit.
- The first Desktop build run reached both x64 and ARM64 test steps, then correctly failed because Windows checkout converted tokenizer.json from LF to CRLF and the classifier rejected its changed SHA-256. The model itself remained byte-identical.
- Added .gitattributes rules that treat model.onnx as binary and force tokenizer.json to LF on every checkout. Added an application contract so this byte-stability requirement cannot disappear silently.
- Verification: a fresh checkout from the staged Git index produced tokenizer SHA-256 851CA67100D372CA3AE031A6ABD168F53489EEBFD7D89523F35C5C9B4D372C3C; Core passed 273/273; App.Contracts passed 85/85; staged diff integrity passed.
- Pushed the fix as aa78d9d. Replacement Desktop build run 33468750183 completed successfully: x64 passed build/tests/ZIP/MSIX, Stream Deck packaging, and artifact upload in 3m17s; ARM64 passed build/tests/ZIP/MSIX and artifact upload in 4m23s. No Store publisher workflow or Partner Center action ran.

### 2026-08-31 - proprietary source-visible license and packaged legal notices

- Replaced the repository MIT license with a custom proprietary source-visible license owned by Alex Mammen. Official unmodified binaries remain free to install and use, while copying, building, modification, redistribution, derivative/competing use, and AI-training use of current source are not granted.
- Documented two unavoidable boundaries: GitHub's Terms grant limited public viewing/forking through GitHub functionality, and earlier revisions already distributed under MIT retain their prior permissions. The new license applies prospectively to this version and later proprietary work.
- Added THIRD-PARTY-NOTICES.md so the proprietary claim explicitly excludes separately licensed libraries, models, data, and notices. Updated application authorship/copyright metadata from the generic contributor label to Alex Mammen.
- Build-Release.ps1 now packages LICENSE.txt, the third-party summary, Apache-2.0 text, NAudio's license, ONNX Runtime's license, and ONNX Runtime's complete third-party notices. Both pre-package and unpacked-MSIX checks fail the build if any required legal file is absent.
- Verification: production x64 publish succeeded; Core passed 273/273; App.Contracts passed 84/84; ZIP creation and MakeAppx pack/unpack succeeded. The 240,680,910-byte ZIP hash is 5D833F2CE9B5862C02E90ED04A1CAE2BEA33B2F50F80F929C22D328926982955. The 239,922,359-byte unsigned structural-audit MSIX hash is C27CB7060850723D12ECC21F52CD5432CAC53B156DDF15A6D63D315B5392CFAF.

### 2026-08-31 - guarded GitHub-to-Microsoft Store publisher track

- Added Build-StoreBundle.ps1 as the deterministic Store entry point. It rejects placeholder identity values and Store-incompatible versions, invokes the existing release build for x64 and ARM64, runs the full tests once unless explicitly skipped, creates a neutral MSIX bundle, unbundles it, verifies both architectures, and emits provenance, size, signature status, and SHA-256 metadata.
- Added a manual-only Microsoft Store GitHub Actions workflow. Its default mode only builds an artifact; Partner Center access requires an explicit draft-upload input plus the protected microsoft-store-production environment; certification requires a second explicit input. The workflow has read-only repository permissions, serialized Store runs, bounded timeouts, missing-configuration guards, and no push or pull-request publisher trigger.
- Kept non-secret Store identity values in GitHub variables and Partner Center credentials in protected environment secrets. Manual package-version input is passed through an environment variable rather than interpolated into PowerShell syntax.
- Documented the required initial live Partner Center submission, free-product limitation, Entra application/Manager role, exact repository variables/environment secrets, build-only first run, and draft-before-certification sequence. No Partner Center page, account setting, secret, product, or submission was changed.
- Verification at that checkpoint: App.Contracts passed 82/82; PowerShell syntax and Store identity/version guard checks passed; focused diff integrity passed. The non-publishable local audit produced x64 and ARM64 packages plus a structurally verified 475,606,999-byte neutral bundle at artifacts/store-publisher-audit/SafeSpeak_1.0.0.0_neutral.msixbundle. Bundle SHA-256 is 5C37C6889D66EFE6CA9989FC59EC558AFED3CB07A5BBCCCBF8732D668D3FBE70 and signature status is NotSigned.

### 2026-08-31 - Microsoft Store readiness audit and icon v1

- Confirmed SafeSpeak can use the Microsoft Store MSIX route without changing away from WPF. The manifest already uses packagedClassicApp/mediumIL and declares the required restricted runFullTrust capability.
- Confirmed the current repository package is not a submission candidate: version 0.1.0.4, placeholder identity/publisher, unsigned output, no completed WACK evidence, and open P0 manual accessibility gates.
- Generated a transparent 1254 by 1254 icon master with a shield, speech bubble, and three audio bars; copied it to installer/Assets/SafeSpeakIconMaster-v1.png without deleting the generated source.
- Replaced placeholder package graphics with derived 50, 44, 150, and 310 by 150 transparent PNG assets and added a nine-frame 16-256 pixel SafeSpeak.ico embedded in the desktop executable.
- Reworked Generate-Assets.ps1 to deterministically regenerate all package and executable assets from the versioned master. Build-Release.ps1 now copies only the four manifest-referenced PNGs into MSIX staging, excluding editable source art and the separately embedded ICO.
- Verification: isolated Release app build passed with 0 warnings and 0 errors; extracted EXE icon was 32 by 32; makeappx pack and unpack passed for artifacts/store-readiness-audit/SafeSpeak_0.1.0.4_x64.msix; package size 228.75 MB, SHA-256 E998ED64CB95CD49287C44A2485E4D0F2C71C230F16FE9414DC80EB5051EC96F, signature NotSigned. Tests were intentionally skipped for this audit build because the full 353-test pass was already current and only packaging/icon files changed.
- The live SafeSpeak test instance, PID 21344, remained responsive and was not terminated during isolated build and packaging verification.

### 2026-08-31 - five-step onboarding, compact Stream Deck, and TTS routing controls

- Split onboarding accessibility into Step 1 Reader and Step 2 Theme. Reader asks a direct Yes/No question, supports Y and N without modifiers, immediately applies the in-session spoken-guidance state, and moves to Theme. Platform, Filtering, and Review are now Steps 3, 4, and 5; existing two-launch pair confirmation remains intact.
- Reduced the Stream Deck action catalog from 24 configuration/live actions to eight operational controls in Hear Status-first order: Hear Status, Arm/Disarm, Emergency Stop, Playback Mode, Pause/Resume TTS, Speak Next, Stop Current, and Clear Queue. Existing identifiers for those eight were retained, canonical commands replaced old wording, and the plug-in version advanced to 1.1.0.0.
- Moved configuration ownership into the app: Settings now exposes English/Latin filtering, mixed-script protection, audience tier, current-session gift-sender eligibility, seven event announcement toggles, Pause All, four event pause bypasses, broadcast/private routing, logging, guidance, theme, and setup rerun. Moderated viewer names and intent moderation remain mandatory and are reported as always active.
- Added a serialized pause-bypass lane to TtsQueue. Selected event announcements may speak while ordinary chat remains paused; they still use the production moderation/name pipeline and cannot overlap current speech. Clear Queue and Emergency Stop clear both ordinary and bypass lanes.
- Added current-armed-session donor tracking. A gift sender may pass a Followers/Subscriber/Moderator audience restriction only when the explicit donor option is enabled; messages and names remain fully moderated.
- Verification: Release solution build passed with 0 warnings and 0 errors; Core 273/273 and App.Contracts 79/79 passed, total 352/352; Stream Deck JavaScript syntax and manifest parsing passed.
- Launched the exact Release output at `src/SafeSpeak.App/bin/Release/net8.0-windows/SafeSpeak.App.exe`. PID 17352 remained responsive, the emulator reported one active WebSocket client, and the loopback state endpoint reported TikFinity Connected, Disarmed, zero queued messages, and no speech.
- No package was created. The current Release app and emulator remain running for manual Reader/Narrator, Settings traversal, pause routing, visual scaling, and audible output evidence.

### 2026-08-31 - installed neural-voice state and obsolete monitor removal

- Made the Local Neural Voices card state-based. The install button and progress host are collapsed after the manager verifies the model file; the visible/UIA-readable installed status and 27-voice count remain. A direct stale install command now reports the installed state without entering download mode.
- Corrected the neural voice progress range to 0-100 to match the install manager's reported values.
- Removed the redundant Settings audio-routing card. Broadcast output and device selection remain on Voice, their owning page.
- Removed the discontinued private-monitor backend rather than only hiding it: second audio router, queue mirroring, private moderation speech, persisted monitor fields, IPC alias, shutdown paths, and obsolete mirror test are gone. Voice Preview retains its separate default-device preview router, and Hear Status retains current live state plus broadcast-route output.
- Verification: normal close completed through the app shutdown path; Release solution build passed with 0 warnings and 0 errors; Core passed 273/273 and App.Contracts passed 80/80, total 353/353.
- Launched the rebuilt Release output as PID 21344. It remained responsive, connected to the active TikFinity emulator, and reported Disarmed with an empty queue for manual testing.

### 2026-08-31 - Hear Status first navigation and live emulator

- Moved the persistent Hear Status button out of the TabControl template into a true sibling before the navigation control, while preserving its far-left visual segment in the same top row. This avoids WPF entering the parent TabControl before a nested template button.
- Assigned Hear Status logical TabIndex 1, the four-tab navigation hub TabIndex 2, and retained Arm as TabIndex 3. The remaining Live order is Emergency, the applicable pause/automatic control, the applicable manual/speak-next control, Stop Current, Clear Queue, Reconnect, and Live Activity.
- Strengthened the main-shell contract to prove the sibling/XAML order, visual columns, persistent placement outside every TabItem and TabControl, global first three keyboard positions, and the complete mapped Live-action sequence.
- Corrected the runtime startup override in MainWindow: Loaded now focuses HearStatusButton rather than ArmToggle. The armed-controls collapse handler remains separate and still returns focus to Arm after ordinary disarm or Emergency Stop.
- Replaced the incorrect cross-page global TabIndex ranges with two persistent top-row positions plus four independent local page scopes. Live is 1-10, Safety 1-7, Voice 1-7, and Settings 1-6, so hidden pages cannot capture or redirect traversal.
- Added selected-page focus routing at the navigation boundary: Tab from the selected tab enters Arm, Moderation Strength, Active Voice, or Theme Selector according to the selected page; Shift+Tab from that page-entry control returns to the selected tab instead of wrapping to Hear Status.
- Improved the shared built-in focus narrator so list choices include the parent selector, option position, selected state, and the selector's arrow-key instructions. Combo boxes now resolve object DisplayName/Name/Title/Id values, and selector HelpText replaces generic instructions when available.
- Settings exposes Theme selector as the stable control name, the current theme as UIA ItemStatus, and Light/Dark/High Contrast as positions 1/2/3. Safety now names its first control as the Moderation strength selector.
- Focused main-shell contracts passed 34/34; the complete App.Contracts suite passed 74/74; the Release solution build passed with 0 warnings and 0 errors; the focused diff integrity check passed with only the existing line-ending notice.
- Closed the prior SafeSpeak instance through its normal window-close path, rebuilt, and launched the updated Release executable. The TikFinity emulator remained on http://localhost:21213, returned HTTP 200, and confirmed the rebuilt app disconnected and reconnected with one active WebSocket client.
- No release package was created. Rendered screen-reader and scaling acceptance remains open because the Windows app-control helper is still unavailable.

### 2026-08-31 - P0 backend integration and saved spoken-guidance diagnosis

- Confirmed the current user settings at %LOCALAPPDATA%\SafeSpeak\settings.json are schema 4, onboarding stage Complete, Light theme, and built-in spoken guidance Disabled. This explains both the skipped startup questions and current silence; no user settings were changed during diagnosis.
- Kept the product rule that completed onboarding does not repeat on every launch. Run Setup Again remains the supported way to review the choice, and its cancel path now restores every settings field it can mutate plus the completed stage.
- Added a visible/UIA-live Settings status that distinguishes saved spoken guidance Disabled, Enabled with Windows speech ready, and Enabled with Windows speech unavailable.
- Integrated schema 5 explicit logging consent, deterministic connected/midstream/reconnect logging lifecycle, exact path/failure UI, isolated privacy-safe filter testing, atomic ZIP voice-pack rollback, and loopback TikFinity lifecycle coverage.
- Fixed the concrete Windows SAPI cancellation defect by tracking and cancelling the exact in-flight wave synthesizer. Four deterministic cancellation/dispose tests pass; real audible SAPI and Kokoro evidence remains.
- Emergency/disarm now returns keyboard focus to Arm when armed-only controls collapse.
- Verification: Release solution build passed with 0 warnings and 0 errors; Core 269/269 and App.Contracts 71/71 passed, total 340/340; git diff --check passed with line-ending notices only.
- No new release package was created at this checkpoint because LIVE-002 and rendered accessibility evidence remain open.

### 2026-08-30 - persistent header action and reference page layouts

- Replaced the incomplete four-tab header with five equal segments: Live, Safety, Voice, Settings, and persistent Hear Status. Hear Status remains the first button in logical keyboard order and is now available without returning to Live.
- Replaced the Windows-default ComboBox shell, arrow, popup, and item templates with semantic SafeSpeak brushes. Closed selections, open dropdowns, highlighted items, selected items, borders, focus, and disabled state now follow Light, Dark, and High Contrast.
- Rebuilt Safety around the supplied compact design: moderation strength and explanation, concise model/banned/evasion status rows, compact banned-term management, and a production-pipeline Test Filter result card.
- The Test Filter path uses no live sender cooldown identity and never touches the connector, live feed, TTS queue, or audit logger. It announces only a privacy-safe decision summary and never repeats hostile input in the result.
- Rebuilt Voice around current voice plus Preview, inline speed and volume controls, broadcast consent/output, and a compact local-neural-voice install card. Rebuilt Settings around the three visual theme cards, built-in spoken guidance, reader speed, explicit local logging consent/warning, logs folder, and Run Setup Again.
- Did not expose fake Voice Pack Import or Reset All Data actions: package validation is not yet connected to selectable synthesis, and full data reset still lacks its verified path inventory/confirmation backend.
- Added two contracts and revised navigation/tab-order contracts. Release build passed with 0 warnings and 0 errors; Core passed 233/233; App.Contracts passed 65/65; total 298/298; git diff --check exited 0.
- Packaged the current source at artifacts/SafeSpeak-0.1.0.4-win-x64 and artifacts/SafeSpeak-0.1.0.4-win-x64.zip. Resolve-only verification passed and ZIP SHA-256 is 5E22270CD2C165A6EBC38C3A54CB6115F55DB54499C8B0352E3EF99BA2EEC363.

### 2026-08-30 - compact tab-first Live controls and persistent Hear Status

- Reworked the main window to a 720 by 700 growable default with no maximum-size cap, allowing Windows visibility scaling or maximization while keeping the default footprint compact.
- Made the four tabs the shell entry point. Moved the SafeSpeak heading, connection/safety/queue/playback cards, Hear Status, Arm, playback controls, source card, and live activity list into the Live tab; Safety, Voice, and Settings keep their dedicated pages.
- Made Hear Status the first button in logical keyboard order and always available. Its private spoken summary now covers armed and playback state, source connection, queue size, current speech, broadcast route, and private status route.
- Replaced the always-visible eight-button wall with armed and playback-mode visibility: disarmed presents Hear Status and Arm; armed modes reveal only the emergency, pause/resume or mode-change, speak-next, stop-current, and clear-queue actions that apply.
- Reduced button, tab, card, group, and surface sizes while preserving at least 44-DIP primary targets and three-DIP focus visuals; changed the component language to compact square corners.
- Added two regression contracts for the persistent first Hear Status action, state-dependent control bindings, growable sizing, and full spoken-status content. Release build passed with 0 warnings and 0 errors; Core passed 233/233; App.Contracts passed 63/63; total 296/296; git diff --check exited 0.
- Rendered acceptance is still open because the Windows Computer Use helper remains unavailable; use the next real Light/Dark screenshots to correct visual issues before continuing onboarding acceptance.
- Packaged the current source at artifacts/SafeSpeak-0.1.0.4-win-x64 and artifacts/SafeSpeak-0.1.0.4-win-x64.zip. Resolve-only pointer verification passed, ZIP SHA-256 is 9CDC9D72ED7A722C5770E4383B3E2A287D86464B10BC0A3D1D6BD9CC297C0426, and no SafeSpeak process remains.

### 2026-08-30 - screenshot-driven static sizing and Dark theme correction

- Used the user's rendered screenshot as acceptance evidence: the prior 1080 by 820 shell expanded into an oversized control wall, the primary deck scrolled independently, and native control text did not consistently follow Dark theme foregrounds.
- Standardized the main window to an 860 by 760 default with a 720 by 680 minimum and bounded 1120 by 900 maximum. Standardized onboarding to a 720 by 700 default with bounded growth.
- Removed the primary header ScrollViewer. Brand, four status tiles, full-width Arm control, and all eight secondary commands now form a fixed area; only data-heavy tab content remains scrollable.
- Replaced wrapping status/actions with a fixed four-column status row and four-column, two-row action grid so ordinary resizing does not rearrange the operating sequence.
- Added semantic foreground/background styles for TextBlock, Label, RadioButton, CheckBox, Slider, ComboBoxItem, ListBoxItem, and ListViewItem; custom feed, selector, detection, review, and theme-card item styles now inherit those native semantic styles.
- Added ten regression cases for standard/bounded sizing, non-scrolling primary actions, and native semantic foreground coverage. Release build passed with 0 warnings and 0 errors; Core passed 233/233; App.Contracts passed 61/61; total 294/294; git diff --check exited 0.
- Windows Computer Use initialization failed after its prescribed reset/retry because the host sandbox helper reported helper_unknown_error: setup refresh had errors. No alternative UI automation was used; rendered screenshot acceptance remains open.
- Packaged the corrected build at artifacts/SafeSpeak-0.1.0.4-win-x64 and artifacts/SafeSpeak-0.1.0.4-win-x64.zip. Resolve-only verification passed, ZIP SHA-256 is CA2DD0CCD67467D371F3C4FC11CD9C4392DF9A36771FA1E8C6AA535D242B7D71, and no SafeSpeak process remains.

### 2026-08-30 - Calm Clarity interface-first redesign

- Reprioritized work at the user's direction: interface and visual design before the remaining onboarding acceptance work.
- Used the supplied Calm Clarity, Studio Console, Maximum Contrast, and four-step onboarding concepts as the visual product reference.
- Implemented the Calm Clarity main-shell hierarchy in App.xaml and MainWindow.xaml: branded header, compact state cards, full-width primary Arm control, explicit emergency control, grouped playback/queue actions, segmented tab navigation, larger targets, and a persistent live-status footer.
- Implemented the matching onboarding hierarchy in AccessibilitySetupDialog.xaml: step badge, clearer prompt/status surface, native large targets, and three responsive keyboard-selectable Light, Dark, and High Contrast preview cards.
- Preserved all current commands, bindings, native WPF controls, logical TabIndex values, automation names, help text, live regions, redacted feed bindings, and three-DIP focus visuals.
- Kept Light, Dark, and High Contrast on one semantic component layout rather than building three divergent interfaces.
- Release solution build passed with 0 warnings and 0 errors; Core passed 233/233; App.Contracts passed 51/51; total 284/284; git diff --check exited 0.
- Packaged the current visual source as artifacts/SafeSpeak-0.1.0.4-win-x64 and artifacts/SafeSpeak-0.1.0.4-win-x64.zip; resolve-only pointer verification passed, ZIP SHA-256 is 3C86BC947F2DE33CB6B3DDCAB3A395361A08A631B6B060836FF9F6DEDD1B092F, and no SafeSpeak process was left running.
- No logging implementation was replaced. LOG-001 and LOG-002 remain later, narrowly scoped persistence/privacy and lifecycle reliability work.

### 2026-08-30 - parallel accessibility contract and remediation batches

- Ran three disjoint first-batch lanes for onboarding, theme, and main-shell contracts, then reused freed slots for onboarding resume remediation, exact-once announcement contracts, shell XAML remediation, and release-entry contracts.
- Added 51 passing application contracts covering onboarding workflow, announcement deduplication, semantic themes/contrast/Windows override, main-window focus/tab/name/help/item/redaction behavior, and release-script entry points.
- Fixed contract-proven shell defects: removed collapsed audit controls from tab order, added audio/voice ComboBoxItem names, applied shared 3-DIP focus visuals to TabControl/ListBox, split concise Emergency/Hear Status names from HelpText, and documented slider arrow-key behavior.
- Fixed contract-proven onboarding defects: schema v4 persists bounded consent/detection state, automatic connection resumes truthfully, Review performs single-flight model verification before Finish, review rebuilds afterward, and initial/same-page announcements are guarded exactly once.
- Kept all human evidence open: real WPF keyboard traversal, Narrator/NVDA/JAWS speech timing, 100/200/400 percent screenshots, and real Windows High Contrast on/off restoration.
- Coordinator verification: Release solution build passed with 0 warnings and 0 errors; Core 233/233 and App.Contracts 51/51 passed, total 284/284; git diff --check passed.

### 2026-08-30 - SET-001 and ONB-002 automated contracts

- Added SettingsStore behind AppSettings.Load/TrySave, preserving a single persistence API for current callers.
- Writes use a unique same-directory temporary file, explicit disk flush, atomic replacement, and a prior-valid backup. Recovery preserves the valid backup when repairing a corrupt primary.
- Added safe normalization for enum values, onboarding consistency, finite/clamped numeric settings, connector ID, mandatory safety flags, and bounded/deduplicated banned terms.
- Added six SET-001 tests covering custom-path round trip, corrupt-primary backup recovery, corrupt-all defaults, partial-value normalization, replacement backup content, and unsupported future schema.
- Added SafeSpeak.App.Contracts.Tests to the solution and release script. Six onboarding contracts verify native focusable controls, unique logical TabIndex values, automation names, live regions, exact theme option/position text, 44-DIP targets, step-specific focus, and a single interrupting save-error announcement.
- Verification: Release solution build passed with 0 warnings and 0 errors; 228 Core tests and 6 app contracts passed; release script syntax and git diff checks passed.
- Attempted the required real Windows keyboard pass using the computer-control skill. Its node kernel failed three times because the host Windows sandbox helper could not initialize. Per the skill recovery rules, UI input stopped and ONB-002 remains IN PROGRESS.

### 2026-08-30 - CLEAN-004

- Moved only playback observable properties, computed labels, queue event projection, playback commands, and arm/disarm announcements into MainViewModel.Playback.cs.
- Kept TtsQueue as the single armed/mode/queue/speech transition owner; no controller or duplicate state store was introduced.
- Reduced MainViewModel.cs from 1,456 to 1,262 lines; the responsibility-scoped partial is 228 lines.
- Verification: Release solution build passed with 0 warnings and 0 errors; TtsQueue regression tests passed 12/12; git diff --check passed.
- Advanced the required checkpoint to SET-001.

### 2026-08-30 - CLEAN-002, CLEAN-003, onboarding foundation

- Replaced the old combined accessibility profile with independent spoken-guidance and Light, Dark, or High Contrast preferences, including legacy JSON migration and two-launch combination confirmation.
- Added durable onboarding stages and a four-step accessible wizard for accessibility, TikFinity/TikTok connection, bundled local filtering status, and review.
- Added consent-gated local connector detection limited to approved TikFinity process names and local listener port 21213; it does not connect, authenticate, arm, or scan files.
- Added semantic Light, Dark, and High Contrast dictionaries plus Windows High Contrast override/restore behavior.
- Removed the disconnected enhanced-model download surface and made the primary moderation pipeline use the bundled hash-verified ONNX classifier with deterministic fallback.
- Removed primary cloud/local-endpoint and offline-simulator activation while retaining explicitly secondary Core implementations and tests.
- Replaced displayed Skip/Panic/profile terminology and implemented Arm, Pause, Manual, Speak Next, Stop Current Speech, Clear Queue, and Emergency Stop semantics.
- Classified remaining cleanup-scan matches: legacy migration tests/code, Stream Deck UseHighContrastTheme compatibility, installer SkipTests, secondary-track code/tests/docs, tokenizer vocabulary, or historical plan text.
- Verification: full Release build passed with 0 warnings and 0 errors; focused onboarding/settings/connector tests passed 35/35; full Release tests passed 222/222 in 42 seconds; git diff --check passed with line-ending notices only.
- Advanced the required checkpoint to CLEAN-004.

### 2026-08-30 - CLEAN-001

- Classified every one of the 70 status entries without editing production code.
- Final disposition: 35 KEEP, 25 REVISE, 6 SECONDARY, 4 REMOVE.
- Recorded per-file reasons, dependencies, mixed-file secondary fragments, and safe cleanup order in docs/change-classification.md.
- Preserved the bundled local ONNX model, generic connector framework, accessible live-region foundation, release scripts, and bounded shutdown work.
- Marked the disconnected LocalIntentModelManager and its false enhanced-download success surface for removal.
- Marked Google Perspective, arbitrary local endpoint moderation, and the offline simulator as secondary-track only.
- CLEAN-002 started with no ambiguous deletion authorized.

### 2026-08-30 - BASE-004

- Verified executable launched three times through the real WPF window lifecycle.
- Normal close 1: 285.7 ms, exit code 0.
- Normal close 2: 344.2 ms, exit code 0.
- Duplicate-launch primary close: 252.8 ms, exit code 0.
- Start-LatestBuild.ps1 rejected the duplicate while exactly one process remained.
- Final SafeSpeak.App orphan count: 0.
- Final TikFinityEmulator orphan count: 0.
- Machine-readable lifecycle result: passed, command exit 0.
- Production source changes: none.

### 2026-08-30 - BASE-003

- Build-Release.ps1 -Architecture x64 -Format Zip -SkipTests: exit 0.
- Verified executable: artifacts/SafeSpeak-0.1.0.4-win-x64/SafeSpeak.App.exe.
- Pointer: schema 2, package version 0.1.0.4; resolve-only validation passed without launching SafeSpeak.
- Embedded executable FileVersion and ProductVersion: 0.1.0.4.
- EXE SHA-256: 70FC49F38014B153559DC9B0A7750ED15A2E69F05551D840D3DBEFCEDDAD0FA6.
- ZIP SHA-256: C2550B27D6A7A1CF5F7B6B8FBC814BF568DA63A07E571856055D6C4A28194C21.
- Release report SHA-256: A7FD1C5383D428643FDB235E20F1F70E4EDF636F4C4117F6B67F01F3A4576872.
- Pointer SHA-256: 6B1587FE9CF9949E793608522721EE42BA3D6585C7372D5C6D54E418ECA835D1.
- SafeSpeak processes after resolve-only validation: 0.
- Production source changes: none.

### 2026-08-30 - BASE-002

- dotnet build SafeSpeak.sln -c Release -warnaserror: passed in 13.63 seconds with 0 warnings and 0 errors.
- dotnet test tests/SafeSpeak.Core.Tests/SafeSpeak.Core.Tests.csproj -c Release --no-build: 196 passed, 0 failed, 0 skipped in 29 seconds.
- Machine-readable result: artifacts/baseline-test-results/BASE-002.trx.
- git diff --check: exit 0; only informational LF-to-CRLF working-copy warnings were emitted.
- Production source changes: none.

### 2026-08-30 - BASE-001

- Branch: main at d7328e8f69bf4489d76c3dfd6259abfde1d0ecce.
- SDK: .NET 8.0.319.
- Working tree: 39 tracked changes and 26 untracked entries; preserved in place.
- Tracked diff summary: 39 files, 2,638 insertions, 1,155 deletions.
- SafeSpeak.App and TikFinityEmulator processes: 0. The separate Stream Deck SafeSpeak plugin was running and was not disturbed.
- Plan SHA-256 before this checkpoint update: A09C8AA6E06C9CCE87F523767DCB727D7EEDA3E184468E7F91897F0A5E8FAA63.
- Production source changes: none.

### 2026-08-30 - PLAN-001

- User requested a durable plan because long implementation turns may be interrupted by usage limits.
- Paused active implementation.
- Reviewed current plans, status, diffs, onboarding, settings, queue, theme, main UI, moderation, connectors, voice packages, audit logging, and release baseline.
- Incorporated independent UI/accessibility, behavior/services, and test/release audits.
- Recorded exact theme names Light, Dark, High Contrast.
- Recorded disarmed intake, selector announcement, blank audio options, enhanced-model wiring, voice-engine wiring, logger lifecycle, and real cancellation as explicit P0 gaps.
- No production source code was changed.

## Decision and evidence log

| Date | Decision | Reason | Steps |
| --- | --- | --- | --- |
| 2026-08-30 | One living plan is the cross-session source of truth. | Usage limits may interrupt work. | All |
| 2026-08-30 | Themes display as Light, Dark, High Contrast. | Explicit product decision. | ONB-003, THEME-001, SETUI-001 |
| 2026-08-30 | Spoken guidance is independent from theme. | A blind user may use any visual theme. | SET-002, ONB-001 |
| 2026-08-30 | First launch records accessibility/theme; second confirms, then completes remaining onboarding. | Preserves two-launch protection without repeating the whole wizard. | ONB-003 through ONB-007 |
| 2026-08-30 | Disarmed means no provider event reaches moderation, feed, logging, or TTS. | Monitoring is off and old unseen content must not speak after re-arm. | LIVE-001, LIVE-002, LIVE-006 |
| 2026-08-30 | Pause allows current speech to finish; Stop Current cuts it. | The actions remain distinct and predictable. | LIVE-003, LIVE-004 |
| 2026-08-30 | Replace Skip with Stop Current Speech and manual Speak Next. | Skip belonged to an older queue design. | CLEAN-002, LIVE-003, LIVE-004, LIVE-009 |
| 2026-08-30 | Logging defaults off and requires explicit persisted consent. | Logs may contain raw/private content. | SET-002, LOG-001 |
| 2026-08-30 | No enhanced-model or voice-pack success claim until the asset is actually selected and runnable. | Current foundations are disconnected from active execution. | ONB-006, VOICE-002 through VOICE-005 |
| 2026-08-30 | Implement the supplied Calm Clarity interface before completing onboarding acceptance. | The user prioritized the visual shell and supplied Light, Dark, High Contrast, and onboarding references. | THEME-001, SHELL-001, SHELL-002, ONB-002 |
| 2026-08-30 | Retain the existing audit logger and limit later work to consent/persistence and lifecycle reliability. | The logging foundation already exists and does not need replacement. | LOG-001, LOG-002, LOG-003 |
| 2026-08-31 | Keep Stream Deck to essential live actions and keep configuration in SafeSpeak. | The app can provide complete labels, current values, help text, persistence, and screen-reader context that a button grid cannot. | SETUI-002, LIVE-011 |
| 2026-08-31 | Pause may optionally pass selected event announcements, but Emergency Stop always stops everything. | Streamers can retain gifts/follows/shares/subscriptions without allowing ordinary chat to advance; safety and serialized playback remain unchanged. | LIVE-003, LIVE-006, LIVE-011 |
| 2026-08-31 | Do not submit the current MSIX until P0 assistive-technology acceptance and Store identity/policy/certification work are complete. | A structurally valid package is not equivalent to an accessible, certifiable Store release. | TEST-003 through TEST-006, REL-001 through REL-004 |
