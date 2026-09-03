# SafeSpeak mixed-change classification

This ledger is the CLEAN-001 acceptance record for the composite working tree on
2026-08-30. It classifies every one of the 70 entries reported by git status
--porcelain=v1 -uall before cleanup. No classification authorizes a whole-tree
reset or deletion of unrelated user work.

## Meaning

- KEEP: approved foundation for the primary release.
- REVISE: useful work that must be changed to meet the approved product contract.
- SECONDARY: retain only outside the primary UI, settings, activation, and focus path.
- REMOVE: obsolete or misleading implementation; an existing deletion is finalized.

## Totals

| Area | KEEP | REVISE | SECONDARY | REMOVE | Total |
| --- | ---: | ---: | ---: | ---: | ---: |
| SafeSpeak.App | 2 | 10 | 0 | 0 | 12 |
| SafeSpeak.Core | 17 | 6 | 4 | 3 | 30 |
| Support, tests, and documentation | 16 | 9 | 2 | 1 | 28 |
| **All entries** | **35** | **25** | **6** | **4** | **70** |

## SafeSpeak.App

| Git | File | Class | Cleanup decision |
| --- | --- | --- | --- |
| M | src/SafeSpeak.App/App.xaml | REVISE | Keep native accessible styles; split the palette into semantic Light, Dark, and High Contrast resources. |
| M | src/SafeSpeak.App/App.xaml.cs | REVISE | Preserve deterministic startup/shutdown; replace the partial profile gate with resumable two-launch onboarding. |
| M | src/SafeSpeak.App/Converters/AppConverters.cs | REVISE | Preserve state converters; replace hard-coded disposition colors with semantic theme resources. |
| M | src/SafeSpeak.App/MainWindow.xaml | REVISE | Preserve native controls and UIA foundation; replace Skip/Panic, add approved controls, fix blank audio names, and remove primary secondary-feature targets. |
| M | src/SafeSpeak.App/MainWindow.xaml.cs | REVISE | Preserve bounded cleanup and initial focus; migrate command names, shortcuts, narration, and focus restoration. |
| M | src/SafeSpeak.App/SafeSpeak.App.csproj | KEEP | The root version source and verified packaging contract are correct. |
| M | src/SafeSpeak.App/ThemeManager.cs | REVISE | Preserve Windows High Contrast handling; implement independent Light, Dark, and High Contrast preferences. |
| M | src/SafeSpeak.App/ViewModels/AccessibilitySetupViewModel.cs | REVISE | Preserve rollback and cross-launch confirmation; separate spoken guidance from theme and add the remaining onboarding steps. |
| M | src/SafeSpeak.App/ViewModels/MainViewModel.cs | REVISE | Preserve bounded event processing, safe attribution, connector, moderation, and shutdown foundations; replace contradictory intake/playback/logger/model state. |
| M | src/SafeSpeak.App/Views/AccessibilitySetupDialog.xaml | REVISE | Preserve native controls/live regions; replace profile buttons with a keyboard-native, narrated multi-step wizard. |
| M | src/SafeSpeak.App/Views/AccessibilitySetupDialog.xaml.cs | REVISE | Preserve deterministic focus and narrator lifetime; make navigation step-aware and restart-safe. |
| ?? | src/SafeSpeak.App/Accessibility/LiveRegion.cs | KEEP | Generic sanitized UI Automation live-region behavior required by onboarding and the main window. |

Secondary fragments inside MainWindow.xaml and MainViewModel.cs remain whole-file
REVISE items. CLEAN-003 removes their primary UI, persistence, and activation
without deleting approved behavior from the same files.

## SafeSpeak.Core

| Git | File | Class | Cleanup decision |
| --- | --- | --- | --- |
| M | src/SafeSpeak.Core/AI/HeuristicIntentClassifier.cs | KEEP | Required deterministic fallback and hostile-language signal source. |
| M | src/SafeSpeak.Core/AI/IIntentClassifier.cs | KEEP | Required validated category-score contract and disposal boundary. |
| ?? | src/SafeSpeak.Core/AI/GooglePerspectiveClassifier.cs | SECONDARY | Cloud processing requires later consent, credential, privacy, and failover work. |
| ?? | src/SafeSpeak.Core/AI/IntentClassifierFactory.cs | SECONDARY | Retain only as later-track routing; the primary path is the bundled local model. |
| ?? | src/SafeSpeak.Core/AI/LocalEndpointIntentClassifier.cs | SECONDARY | Loopback endpoint support remains outside the primary release path. |
| ?? | src/SafeSpeak.Core/AI/LocalIntentModelManager.cs | REMOVE | Its download is never selected by the classifier and validation is not cryptographic; remove the false success surface. |
| ?? | src/SafeSpeak.Core/AI/LocalOnnxIntentClassifier.cs | KEEP | Verified bundled offline classifier with fail-closed inference and bounded lifetime. |
| ?? | src/SafeSpeak.Core/AI/Models/LocalModeration/LICENSE.apache-2.0.txt | KEEP | Required redistributed model license. |
| ?? | src/SafeSpeak.Core/AI/Models/LocalModeration/MODEL-NOTICE.md | KEEP | Required pinned provenance, hashes, and limitations. |
| ?? | src/SafeSpeak.Core/AI/Models/LocalModeration/config.json | KEEP | Required pinned model metadata. |
| ?? | src/SafeSpeak.Core/AI/Models/LocalModeration/model.onnx | KEEP | Required bundled offline model; preserve hash and release validation. |
| ?? | src/SafeSpeak.Core/AI/Models/LocalModeration/thresholds.json | KEEP | Required published calibration metadata; protect against runtime drift. |
| ?? | src/SafeSpeak.Core/AI/Models/LocalModeration/tokenizer.json | KEEP | Required hash-verified tokenizer. |
| M | src/SafeSpeak.Core/Accessibility/GlobalHotkeyService.cs | REVISE | Preserve registration reporting; replace Skip/Panic names and mappings with approved actions. |
| M | src/SafeSpeak.Core/Accessibility/IScreenReaderBridge.cs | REVISE | Preserve truthful routing; migrate the stale EmergencyPanic cue name. |
| D | src/SafeSpeak.Core/Accessibility/ReaderSetupConfirmation.cs | REMOVE | Finalize deletion of the obsolete in-memory confirmation duplicate. |
| M | src/SafeSpeak.Core/Accessibility/ScreenReaderAnnouncer.cs | KEEP | Required safe built-in guidance and screen-reader live-region bridge. |
| ?? | src/SafeSpeak.Core/Accessibility/AccessibilityProfileConfirmation.cs | REVISE | Preserve cross-launch matching; separate guidance and theme and use exact theme names. |
| M | src/SafeSpeak.Core/Audio/TtsQueue.cs | REVISE | Replace invalid Boolean combinations and disarmed enqueue behavior with serialized playback state. |
| D | src/SafeSpeak.Core/Connectors/ITikFinityConnector.cs | REMOVE | Finalize deletion of the provider-specific interface superseded by ISourceConnector. |
| M | src/SafeSpeak.Core/Connectors/OfflineEventSimulator.cs | SECONDARY | Keep for tests and the mock connector; never activate it in the primary release. |
| M | src/SafeSpeak.Core/Connectors/TikFinityWebSocketClient.cs | KEEP | Required normalized, bounded, cancellable production connector foundation. |
| ?? | src/SafeSpeak.Core/Connectors/ISourceConnector.cs | KEEP | Required provider-neutral connector and event contract. |
| ?? | src/SafeSpeak.Core/Connectors/SourceConnectorRegistry.cs | KEEP | Required side-effect-free connector discovery/factory registry. |
| M | src/SafeSpeak.Core/Models/AppSettings.cs | REVISE | Add schema migration and atomic persistence; remove coupled profile state and secondary engine fields from primary settings. |
| M | src/SafeSpeak.Core/Models/ChatMessage.cs | KEEP | Required safely moderated spoken attribution style. |
| M | src/SafeSpeak.Core/Models/ModerationConfig.cs | KEEP | Required always-on rules/model/name moderation and meaningful strength mapping. |
| M | src/SafeSpeak.Core/Moderation/ModerationPipeline.cs | KEEP | Required local-first, fail-closed moderation and independently moderated attribution. |
| ?? | src/SafeSpeak.Core/Logging/StreamAuditLogger.cs | REVISE | Promote only after consent, session lifecycle, bounds, failure, rotation, and shutdown behavior are correct. |
| M | src/SafeSpeak.Core/SafeSpeak.Core.csproj | KEEP | Required pinned ONNX runtime and copied model assets. |

## Support, tests, and documentation

| Git | File | Class | Cleanup decision |
| --- | --- | --- | --- |
| M | .github/workflows/desktop-build.yml | KEEP | Valid multi-architecture CI using the hardened release script and central version. |
| M | README.md | REVISE | Replace obsolete accessibility profiles, Default theme, and incomplete playback/onboarding copy. |
| ?? | Directory.Build.props | KEEP | Required version 1.0.2.0 source of truth. |
| M | docs/product-plan.md | REVISE | Reconcile obsolete completion, Skip/profile, and voice assumptions with the living plan. |
| M | docs/ui-accessibility-roadmap.md | REVISE | Replace obsolete controls/profile names and add selector narration plus expanded workflow. |
| M | docs/voice-engines.md | REVISE | Align with the approved validated, runnable custom voice-pack path. |
| ?? | docs/connector-development.md | KEEP | Approved provider-neutral connector contract and accessibility guidance. |
| ?? | docs/implementation-execution-plan.md | KEEP | Authoritative cross-session plan and checkpoint. |
| ?? | docs/moderation-model.md | KEEP | Accurate pinned local-model, fallback, attribution, and lifetime documentation. |
| ?? | docs/release-tracks.md | REVISE | Remove Skip/profile assumptions and promote approved logging/voice work to the main track. |
| M | installer/Build-Release.ps1 | KEEP | Baseline-verified version, test, runtime/model, hash, report, and pointer hardening. |
| M | installer/README.md | KEEP | Accurate hardened build and verified-launch instructions. |
| M | installer/Start-LatestBuild.ps1 | KEEP | Baseline-verified pointer/hash/version/model validation and duplicate guard. |
| ?? | installer/Build-And-Run.ps1 | KEEP | Correct current-version build-and-launch entry point. |
| M | tests/SafeSpeak.Core.Tests/AppSettingsTests.cs | REVISE | Replace the non-persistent logging expectation and add versioned migration coverage. |
| M | tests/SafeSpeak.Core.Tests/AttackCorpusTests.cs | KEEP | Required deterministic and model-backed safety regressions. |
| D | tests/SafeSpeak.Core.Tests/ReaderSetupConfirmationTests.cs | REMOVE | Finalize deletion of obsolete same-session confirmation tests. |
| M | tests/SafeSpeak.Core.Tests/TikFinityParserTests.cs | KEEP | Required normalized connector parser coverage. |
| M | tests/SafeSpeak.Core.Tests/TtsQueueTests.cs | REVISE | Replace old Panic naming and the contradictory disarmed-enqueue expectation. |
| ?? | tests/SafeSpeak.Core.Tests/AccessibilityProfileConfirmationTests.cs | REVISE | Preserve two-launch coverage while separating guidance and theme with exact names. |
| ?? | tests/SafeSpeak.Core.Tests/GooglePerspectiveClassifierTests.cs | SECONDARY | Retain only with the secondary cloud classifier. |
| ?? | tests/SafeSpeak.Core.Tests/IntentModerationLevelTests.cs | KEEP | Required strength, banned-term, threat, evasion, and clean-text behavior coverage. |
| ?? | tests/SafeSpeak.Core.Tests/IntentScoreValidationTests.cs | KEEP | Required malformed-score fail-closed and safe-attribution coverage. |
| ?? | tests/SafeSpeak.Core.Tests/LocalEndpointIntentClassifierTests.cs | SECONDARY | Retain only with the secondary loopback classifier. |
| ?? | tests/SafeSpeak.Core.Tests/LocalOnnxIntentClassifierTests.cs | KEEP | Required model load, hostility, attribution, evasion, score, and lifetime coverage. |
| ?? | tests/SafeSpeak.Core.Tests/ModerationPipelineLifetimeTests.cs | KEEP | Required idempotent disposal and post-disposal rejection coverage. |
| ?? | tests/SafeSpeak.Core.Tests/SourceConnectorRegistryTests.cs | KEEP | Required registry, normalization, duplicate-ID, and simulator coverage. |
| ?? | tests/SafeSpeak.Core.Tests/StreamAuditLoggerTests.cs | REVISE | Expand for consent, mid-session transitions, bounds, rotation, failure, privacy, and shutdown. |

## Safe cleanup order

1. Replace the disconnected enhanced-model download surface with truthful bundled-model status.
2. Migrate accessibility confirmation, queue state, action names, and settings with compatibility coverage.
3. Remove primary construction, focus targets, and persistence for the cloud classifier, local endpoint, and simulator.
4. Reconcile documentation and tests after each behavior replacement.
5. Preserve the pinned ONNX assets, generic connector contract, release scripts, and shutdown foundation throughout.
