# Voice engines and privacy

Release rule: SafeSpeak presents a voice as usable only when its real synthesis path is installed, selected, runnable, cancellable, and testable. It never silently substitutes a Windows voice for a failed neural or imported voice. Windows speech is the verified baseline; Kokoro and custom pack work remain development paths until the gates below pass.

## Windows voices

Windows SAPI desktop voices are available immediately and require no model download. The exact list depends on voices installed for the current Windows user. SafeSpeak does not copy or modify protected OneCore/SAPI registry entries.

## Kokoro local neural voices — development path

The current code contains a Kokoro download/synthesis foundation, but Kokoro is not yet a release-ready option. Before it can be promoted, SafeSpeak must pin and verify the approved asset, prove that the downloaded model is the model actually selected, test cancellation and bounded shutdown with the real engine, and pass the keyboard and screen-reader install flow. If those checks do not pass, Kokoro stays outside the main release.

The target Voice tab uses one explicit install action and reports source, download size, disk use, privacy, progress, cancellation, verification, and failure in written text and through UI Automation. A successful install may expose the 27 American and British English embeddings included by KokoroSharp.CPU only after real synthesis succeeds.

The selected stream voice, rate, and volume apply to moderated chat and explicit **Preview selected voice**. Preview must not start merely by arrowing through a list. The built-in spoken guidance uses a separate low-latency Windows voice for focus and application status, so keyboard navigation does not wait for neural inference. Narrator, NVDA, and JAWS remain independent and use their own configured voices through UI Automation.

- Synthesis engine: [KokoroSharp](https://github.com/Lyrcaxis/KokoroSharp) (MIT)
- Model source: [KokoroSharpBinaries v2.0.0](https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/tag/v2.0.0)
- Upstream model: [hexgrad/Kokoro-82M](https://huggingface.co/hexgrad/Kokoro-82M) (Apache-2.0 model card)
- Processing: on-device ONNX inference; chat text is not sent to a voice API
- Installed size: approximately 326 MB for the model, in addition to runtime and embedded voice data

Windows speech must remain usable if the user declines the model or the download fails. Store and public-release builds must pin and verify the approved Kokoro asset before claiming supply-chain verification.

## Custom voice packages

`VoicePackageManager` validates archive paths, entry counts, expanded size, IDs, and manifest filenames. This is a packaging foundation, not a usable upload or synthesis feature. No imported pack may appear as selectable merely because its archive passed validation.

The release-target upload flow must support at least one explicitly named synthesis backend end to end. It must stage and validate the archive, block traversal and oversized expansion, verify the manifest/assets, handle ID collisions, smoke-test synthesis, install atomically or roll back, and expose accessible progress, cancellation, result, preview, persistence, and deletion. It also needs explicit voice-owner consent, license metadata, and performance limits. Other formats and remote endpoints remain later-track work.

## Output routes

The main release target exposes one primary output route. A retained advanced audio layer can support separate broadcast/private-monitor devices, mirroring, and virtual-cable setups, but those controls remain outside the primary interface until they pass the feature-graduation rules. Any moderation notice may contain only a safety-filtered author label and rejection category; rejected message text is never spoken.
