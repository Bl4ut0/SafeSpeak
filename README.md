# SafeSpeak

SafeSpeak is an accessibility-focused application that turns approved livestream chat into speech. The Windows application remains the first release target; isolated Android and iOS development tracks now provide an early mobile test foundation. SafeSpeak filters banned terms and hostile intent before audio and always starts disarmed.

> [!WARNING]
> SafeSpeak reduces TTS abuse risk but cannot guarantee that every hostile message will be detected. It always starts disarmed. Disable any independent source-platform TTS and keep the platform's own moderation and trusted moderators active.

## Main release target

SafeSpeak is now available from the [Microsoft Store](https://apps.microsoft.com/detail/9MTFGCPQCQ86). GitHub release candidates provide separate portable ZIP, MSI, and MSIX downloads after their Authenticode signatures and SHA-256 checksums pass the release workflow.

The redesign below is in progress. Items described as targets are not release claims; the living [implementation and execution plan](docs/implementation-execution-plan.md) records the verified checkpoint and acceptance evidence.

- .NET 8 WPF desktop application for Windows 10 build 19041 or later.
- First launch asks independently whether to use built-in spoken guidance and which visual theme to use: **Light**, **Dark**, or **High Contrast**. After closing and reopening SafeSpeak, the user confirms those pending choices before continuing through platform and filtering setup. Completed setup can be run again from Settings.
- Tab/Shift+Tab navigation, arrow-key tabs and sliders, access keys, large targets, visible focus, real UI Automation live-region events, and Windows High Contrast support.
- Automatic TikFinity connection and reconnection through a platform-neutral source-connector contract. SafeSpeak remains disarmed after connecting.
- Local MiniLM/ONNX toxicity classification with four understandable strengths: Relaxed, Balanced, Strong, and Maximum. The bundled model and deterministic fallback are the current moderation foundation; onboarding must report the model actually selected instead of claiming that a disconnected download improved filtering. Chat does not leave the computer for the main moderation path.
- Non-optional severe-abuse and anti-evasion rules plus a keyboard-manageable custom banned-terms list.
- Every approved chat is attributed as **moderated viewer name says: message**. Names are filtered separately and unsafe names become **A viewer**.
- Rejected-content redaction for both the visible activity feed and external screen-reader summaries.
- **Arm SafeSpeak** enables event intake and approved-message playback. The target Live controls are Pause TTS, Manual Mode, Speak Next Approved Message, Stop Current Speech, Clear Queue, and Emergency Stop. Disarming discards new events before moderation, activity, logging, or TTS; Emergency Stop cancels current speech, clears pending speech, disarms, and requires an explicit re-arm.
- Closing the main window immediately disarms speech, stops accepting source events, and starts cleanup off the interface thread. Connector, speech, audio, and local-model resources get a five-second cleanup window before the app exits; shutdown errors never open a blocking dialog.
- Installed Windows voices are available now. Kokoro and imported voice packs remain development paths until their assets, real synthesis backends, cancellation, trust, and accessible install flows pass the release gates.
- A single main output, voice, test action, rate, and volume in the primary interface.
- Repeatable self-contained ZIP, MSI, and MSIX builds for x64 and arm64.

The repository retains some advanced audio, event-routing, simulator, and Stream Deck infrastructure for compatibility, but those controls are outside the release-critical interface. See [main and later tracks](docs/release-tracks.md).

## Build and test

```powershell
dotnet build src/SafeSpeak.App/SafeSpeak.App.csproj -c Release
dotnet test tests/SafeSpeak.Core.Tests/SafeSpeak.Core.Tests.csproj -c Release
```

Build self-contained ZIP, MSI, and MSIX candidates:

```powershell
./installer/Build-Release.ps1 -Architecture x64 -Format All
```

Repository work uses two separate CI tracks. Pushes and pull requests targeting
`develop` run the non-publishing development workflow and retain an unsigned x64
portable ZIP for seven days. Pushes and pull requests targeting `main` run the
full x64/ARM64 release packaging workflow. After a successful push build on
`main`, the exact verified commit triggers the Microsoft Store publisher. The
protected `microsoft-store-production` environment requires reviewer approval
before CI can verify Partner Center access, upload the Store bundle, and commit
the update for certification. Development CI cannot contact Partner Center.
Pushing a stable `v<version>` tag or prerelease `v<version>-rc.N` tag publishes
the verified desktop packages as permanent GitHub Release downloads. Tagged
releases fail closed unless the executable, MSI, and MSIX have valid
Authenticode signatures. The MSI supplies Windows-native upgrade, repair, and
uninstall; repair also resets the current user's SafeSpeak Local AppData.
Its setup screens support Windows Narrator and show the **Windows+Ctrl+Enter**
shortcut needed to start spoken setup.
See the [development track guide](docs/development-track.md).

Mobile work is planned but dormant. Its isolated branches and workflows remain
outside the desktop promotion path and are not part of this release.

The current four-part desktop version comes from `Directory.Build.props`. To test, build the self-contained x64 executable, and launch that exact verified build in one command, run `./installer/Build-And-Run.ps1`. The launcher validates the app host, managed code, runtime manifests, and pinned moderation assets before starting. This workflow does not start or stop the TikFinity emulator. The MSIX path requires Windows SDK packaging tools. Store submissions also require the identity assigned by Partner Center. See the [packaging guide](installer/README.md).

## Repository layout

- `src/SafeSpeak.App` — keyboard-first WPF interface
- `src/SafeSpeak.Mobile` — shared Android/iOS .NET MAUI test application
- `src/SafeSpeak.Core` — moderation, speech, normalized events, connectors, accessibility, and audio
- `tests/SafeSpeak.Core.Tests` — deterministic safety and regression tests
- `tests/SafeSpeak.Mobile.Foundation.Tests` — portable mobile safety and connector-contract tests
- `docs/store-submission-readiness.md` — Google Play and App Store account, signing, privacy, and CI handoff
- `local-deployment/safespeak-web` — ignored, upload-ready privacy/support website created locally when preparing store submissions
- `tools/website-deploy` — allowlisted, certificate-verified FTPS helper for the dedicated SafeSpeak website account
- `docs` — product tracks, accessibility gates, connector rules, and voice details
- `streamdeck` — optional secondary Stream Deck plug-in
- `installer` — ZIP/MSI/MSIX and WinGet build tooling
- `tools` — TikFinity emulator and development utilities

## Important limitations

- The bundled 23M-parameter MiniLM classifier is a specialized local text model, not a generative chatbot or a guarantee. Deterministic rules and a heuristic fallback remain active around it. The separate enhanced-filter onboarding/download flow is not complete and must not report success unless the selected model is verified and runnable.
- Generic profanity can score as toxic even in positive context; the strength slider controls the tradeoff, and release decisions require a representative livestream corpus.
- TikFinity payloads can change; release versions require privacy-safe compatibility fixtures and parser regression tests.
- The development Kokoro package is roughly 326 MB and still needs a pinned source, checksum or signature, cancellation, and cleanup verification before it can be a release option.
- Custom voice-package archive validation exists, but upload/import is not a usable release feature until at least one supported synthesis backend, atomic install and rollback, consent, licensing, progress, cancellation, preview, persistence, and deletion are complete.
- Narrator, NVDA, JAWS, 200%/400% scaling, physical Stream Deck, clean-install, and direct-download signing remain human release gates. Microsoft Store submission 1 passed certification on September 2, 2026.
- Closing during active Windows and Kokoro speech is a manual release gate: input must stop immediately and the app must exit at the five-second cleanup deadline without a dialog or focus trap.
- Built-in SafeSpeak guidance currently uses the Windows default playback device. Streamers who capture desktop audio must ensure that device is not included in the broadcast mix.

See the [moderation model details](docs/moderation-model.md), [product plan](docs/product-plan.md), [accessibility roadmap](docs/ui-accessibility-roadmap.md), [connector guide](docs/connector-development.md), and [voice engine details](docs/voice-engines.md).

## License

SafeSpeak is proprietary source-visible software; it is not open source.
Official unmodified SafeSpeak binary releases are free of charge to install
and use. Free does not mean public domain or open source: outside authorized
store installation and sharing mechanisms, the binaries may not be repackaged,
mirrored, or redistributed, and the source code and original assets may not be
copied, modified, built, redistributed, sublicensed, sold, or used to create a
competing product without prior written permission. Public GitHub hosting still
permits the limited viewing and forking rights supplied directly by GitHub's
Terms of Service. See the [SafeSpeak Proprietary Source-Visible License](LICENSE).

Apple App Store copies use Apple's applicable App Store usage rules and end-user
license terms for the installed binary. Those store rights do not turn the
publicly visible SafeSpeak source or original assets into open-source material.
The new license applies prospectively; SafeSpeak revisions already published
under MIT remain available under the license that accompanied those revisions.

Third-party libraries, models, and data remain under their respective terms.
See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and the model-specific
notices distributed with SafeSpeak.
