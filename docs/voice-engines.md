# Voice engines and privacy

SafeSpeak presents only voices backed by an implemented synthesis path. A selected Kokoro voice is synthesized by Kokoro locally; it is never silently substituted with a Windows voice.

## Windows voices

Windows SAPI desktop voices are available immediately and require no model download. The exact list depends on voices installed for the current Windows user. SafeSpeak does not copy or modify protected OneCore/SAPI registry entries.

## Kokoro local neural voices

The Audio tab offers one explicit **Install Kokoro voices** action. It downloads the compatible `kokoro.onnx` model to `%LOCALAPPDATA%\SafeSpeak\Models\Kokoro`. The download is written to a temporary file, checked for an obviously incomplete size, and moved into place atomically. After installation, the voice selector exposes 27 American and British English voices bundled as embeddings with KokoroSharp.CPU.

- Synthesis engine: [KokoroSharp](https://github.com/Lyrcaxis/KokoroSharp) (MIT)
- Model source: [KokoroSharpBinaries v2.0.0](https://github.com/Lyrcaxis/KokoroSharpBinaries/releases/tag/v2.0.0)
- Upstream model: [hexgrad/Kokoro-82M](https://huggingface.co/hexgrad/Kokoro-82M) (Apache-2.0 model card)
- Processing: on-device ONNX inference; chat text is not sent to a voice API
- Installed size: approximately 326 MB for the model, in addition to runtime and embedded voice data

Windows speech remains usable if the user declines the model or the download fails. Model checksum enforcement is a release-hardening milestone; Store and public-release builds must pin and verify the approved asset before claiming supply-chain verification.

## Output routes

The broadcast route and private-monitor route can use different active WASAPI render devices. Approved messages can be mirrored to the private monitor. Optional private moderation notices contain only a safety-filtered author label and rejection category; rejected message text is never spoken. A virtual cable is optional and is only needed when the streaming setup requires SafeSpeak to appear as an isolated mixer source.
