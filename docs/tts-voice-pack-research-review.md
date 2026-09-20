# Offline TTS Voice Packs for SafeSpeak

## Executive assessment

The supplied research identifies the right product direction: keep synthesis local, add voices without enlarging every application update, and place every speech engine behind the existing moderation and playback boundaries. Its strongest recommendations are the use of a compact neural engine, explicit package metadata, on-demand assets, lazy initialization, and integrity checks.[^1]

SafeSpeak should build the next voice release around its existing Kokoro implementation. It already uses KokoroSharp and ONNX Runtime, downloads a hash-pinned Kokoro model only after the user requests it, exposes 27 English voices, and routes generated audio through the application's selected output device. KokoroSharp is MIT licensed; its documentation identifies the Kokoro model and voices as Apache-licensed and supports multilingual voices, voice mixing, WAV synthesis, and custom voice loading.[^2] Replacing that working path with Sherpa-ONNX now would add native integration and Store packaging risk without immediately improving English chat speech.

The next valuable work is therefore:

1. Make imported voice packs truthful and executable.
2. Add a curated Kokoro voice catalog and accessible voice mixing.
3. Add signed manifests, per-file hashes, licenses, compatibility metadata, and rollback.
4. Benchmark synthesis under stream load and enforce a small CPU budget.
5. Evaluate Piper through a separate engine prototype only after those foundations are complete.

Custom voice cloning, a public community marketplace, and a library of thousands of voices should remain later projects. They introduce consent, impersonation, licensing, moderation, security, and support concerns that are much larger than the report suggests.

## Material corrections to the supplied research

| Claim in the research | Finding | Consequence for SafeSpeak |
|---|---|---|
| A Kokoro voice embedding is roughly 27–50 MB. | The embeddings present in SafeSpeak's current KokoroSharp package are 522,368 bytes each, about 510 KiB. The official Kokoro v1 voice directory is approximately 28.3 MB in total.[^3] | Additional Kokoro voices are inexpensive. An LRU cache for individual embeddings is unnecessary at the present scale. |
| Kokoro provides rapid zero-shot cloning from a few minutes of audio. | The official Kokoro model card describes a decoder-only release with no encoder.[^4] Neither the model card nor KokoroSharp documents extraction of a new speaker from reference audio. KokoroSharp does document mixing and saving existing voices.[^2] | Implement voice blending if desired. Do not advertise arbitrary voice cloning as a Kokoro capability. |
| Kokoro can supply hundreds or thousands of voices. | Kokoro v1.0 documents 54 voices across eight language groups; quality varies considerably, and several voices receive low grades from the publisher.[^5] | Use a curated catalog with quality and language metadata. Do not promise an effectively unlimited library. |
| ONNX models are inherently portable and safe to accept from the community. | ONNX portability still depends on supported operators, runtime versions, native libraries, tokenizer and phonemizer behavior, and target architecture. ONNX Runtime explicitly warns that a malicious model can consume excessive memory or compute and says untrusted models must be inspected and tested safely.[^6] ONNX also documents path and resource risks involving external tensor data.[^7] | Treat imported models as untrusted. A ZIP traversal check and hash are necessary but insufficient. Validate and smoke-test them in a constrained helper process. |
| Sherpa-ONNX makes engines interchangeable with almost no integration cost. | Sherpa-ONNX has a useful Apache-2.0 C API and supports Kokoro and Piper/VITS, including Windows x64 and arm64.[^8] Its configurations still require engine-specific assets such as token files, espeak-ng data, model settings, and voice binaries.[^9] | Sherpa is a credible future provider boundary, not an immediate drop-in replacement for SafeSpeak's working KokoroSharp path. |
| Piper is an uncomplicated established dependency. | Current Piper development is in `OHF-Voice/piper1-gpl`, which is GPL-3.0.[^10] Piper also tells distributors to inspect each voice's model card because voice licenses can differ and may be restrictive.[^11] | A Microsoft Store release needs a documented licensing decision for the runtime and each distributed voice. Loading Piper-compatible VITS through Apache-licensed Sherpa may reduce runtime-license coupling, but it does not resolve model licenses. |
| Python should be bundled with PyInstaller or Nuitka. | SafeSpeak is a .NET 8 WPF application with a native C# Kokoro wrapper. | Do not add Python to the desktop release. Keep model execution in-process only for trusted built-in assets; use a constrained native or .NET helper for imported assets. |

## Current SafeSpeak implementation audit

### Existing strengths

- `KokoroModelManager` pins the downloaded model to a SHA-256 digest and uses a temporary file before committing the download.
- Kokoro synthesis is serialized, so concurrent chat events cannot re-enter one shared inference session.
- The model is loaded lazily, and packaged voice data is copied to application-owned storage to work with protected Microsoft Store installations.
- `VoicePackageManager` already limits archives to 64 entries and 512 MB expanded size, blocks path traversal, stages imports, replaces atomically, and restores the prior package after a failed replacement.
- `ModularTtsEngine`, `ITtsEngine`, and `IAudioRouter` provide most of the abstraction needed for another trusted engine later.

### Release-blocking defect in custom packs

The current custom-package synthesis method never runs the imported ONNX model. When `sample.wav` exists, it copies that same sample to the output for every chat message. Without a sample, it uses the normal Windows wave synthesizer and ignores the imported model and configuration. The present test asserts this sample-copy behavior rather than synthesized speech.

This creates a misleading state: an archive can be installed, listed as a neural voice, selected, and announced as ready even though its model has never loaded. The user interface currently labels Level 4 as upcoming, which is the accurate state. The import action should remain unavailable in release builds until a real provider validates and synthesizes the selected package.

### Package contract gaps

The current manifest contains useful identity fields but needs a versioned contract before outside packages are accepted. It currently permits engine labels such as `XTTS` and `WebEndpoint`, although the product is local-first and neither backend is implemented. It also lacks:

- schema version and minimum SafeSpeak version;
- exact engine and model-format versions;
- SHA-256 and byte size for every asset;
- SPDX license identifier, license text, source URL, and source revision;
- redistribution and commercial-use status;
- supported languages and phonemizer requirements;
- expected input/output names, tensor types, and sample rate;
- quality tier and a curated test phrase;
- whether ONNX external data is used;
- model memory and synthesis-time limits.

The importer also needs a real smoke test. A valid installation must synthesize new text, produce a valid mono WAV with an approved sample rate and bounded duration, contain non-silent audio, respect cancellation, and release all files and native resources afterward.

## Recommended implementation

### 1. Complete the Kokoro catalog first

Expose additional voices already supported by the pinned KokoroSharp version, beginning with the best documented English voices and then one language at a time. The official Kokoro voice card warns that some voices perform poorly on very short inputs and that quality varies by language and training data.[^5] SafeSpeak should therefore display language, publisher quality grade, installed state, download size, and a short description, while keeping preview behind an explicit button.

For a screen-reader user, each voice row should announce a compact sequence such as: “Bella, American English, Kokoro, quality A minus, installed.” Filtering controls should cover language, engine, quality, and installed state. Arrowing through the list must not automatically speak full samples.

The application can either ship all approximately 28 MB of official v1 embeddings or download language groups on demand. Both are reasonable. Because the core model is about 320 MB and each current embedding is only about 510 KiB, per-voice downloading brings little benefit unless the catalog later includes several model families.

### 2. Add accessible Kokoro voice mixing

Voice mixing is a real feature in the wrapper SafeSpeak already uses.[^2] A restrained implementation would let a user choose two installed Kokoro voices and set a percentage, preview the result, name it, and save only the blend metadata or resulting embedding. It should enforce weights that sum to 100 percent and always offer Reset to original voice.

This is a safer and much smaller project than voice cloning. The interface should describe it as blending existing synthetic voices and avoid implying that it reproduces a real person.

### 3. Introduce a curated, signed catalog

Do not query arbitrary Hugging Face repositories when the app launches. Publish a small SafeSpeak catalog whose entries have already passed license, quality, compatibility, and performance review. The client should cache the last valid catalog, refresh only on an explicit user action or a quiet bounded schedule, and continue offline when the network is unavailable.

Each catalog release should be signed with an offline-held publisher key. Each asset entry should include its SHA-256 digest and exact byte length. Downloads should use a temporary file, enforce compressed and expanded limits, verify the signature and digest before parsing, smoke-test in isolation, and then commit atomically. The current import transaction provides a good base for the final commit and rollback stages.

### 4. Execute imported models in a constrained worker

Community ONNX files should not load directly into the main SafeSpeak process. Use a small helper process with:

- no network access;
- a read-only view of the staged package and a dedicated output directory;
- a Windows Job Object with memory, process-count, and shutdown limits;
- a hard initialization timeout and per-utterance timeout;
- a fixed thread limit;
- validated output length and format;
- immediate termination when SafeSpeak closes or invokes emergency stop.

The helper protocol can expose `Probe`, `Synthesize`, `Cancel`, and `Shutdown`. Only packages that pass `Probe` should become selectable. A crash or timeout should disable that pack, preserve the previous voice, and announce a short actionable message.

### 5. Enforce a streaming performance budget

The research correctly prioritizes CPU use, but “CPU-only” does not automatically mean low impact. ONNX Runtime normally creates intra-operation workers based on physical cores, and worker spinning can consume CPU while waiting.[^12] SafeSpeak should start neural synthesis at two inference threads, keep sequential graph execution, and benchmark spinning disabled or tightly bounded.

Record these metrics by engine and voice:

- cold model-load time;
- time to first audio;
- total synthesis time;
- generated audio duration and real-time factor;
- process CPU percentage during synthesis;
- working-set change;
- queue wait time and queue depth;
- cancellations, timeouts, and fallback count.

The acceptance target should be based on concurrent OBS and game load rather than an idle benchmark. A reasonable initial gate is median real-time factor below 0.5, 95th-percentile time to first audio below 750 ms for a typical chat message, bounded memory after repeated messages, and no sustained idle CPU after synthesis. Those numbers are proposed product targets and should be adjusted from measured low-end hardware results.

#### Measured development baseline

A repeatable simulator was run on a 4-core/8-thread Intel i7-11375H laptop with 16 GB RAM. The results are a development baseline rather than minimum-hardware certification:

| Scenario | Result | Process CPU | Peak working set |
|---|---:|---:|---:|
| MiniLM intent, sequential | 4.4 ms per inference | 53% during the burst | 197 MB |
| MiniLM intent, four callers | 2.5 ms per inference | 68% during the burst | 199 MB |
| Normal moderation pipeline | 6.2 ms per message | 59% during the burst | 207 MB |
| Moderation with one mention | 8.0 ms per message | 57% during the burst | 217 MB |
| Kokoro, four voices | 18.8 seconds of audio in 8.8 seconds; RTF 0.47 | 88% during synthesis | 1,000 MB |
| Kokoro, four simultaneous requests | 18.8 seconds of audio in 8.4 seconds; RTF 0.45 | 90% during synthesis | 1,024 MB |
| MiniLM moderation while Kokoro runs | 8.6 seconds total | 92% during the combined burst | 1,024 MB |
| Qwen3Guard 0.6B, CPU-only | 3.0-second cold request; 668 ms warm | 54% during warm requests | 1,023 MB across two runtime processes |

SafeSpeak serializes live incoming events, and its shared Kokoro manager serializes inference even when the main and alert queues request speech concurrently. Multiple voice selections therefore do not load multiple 325 MB Kokoro models. The simultaneous test represents queued callers to one shared model and offers no material throughput gain.

The more immediate moderation inefficiency is repeated classification. In the measured corpus, 100 normal messages caused 175 MiniLM calls, while 100 messages with one mention caused 275. SafeSpeak classifies message content, display names, and each mention. When Qwen is selected, its classifier first runs MiniLM and can then run the generative guard model for each of those strings. A bounded cache for normalized display-name and mention decisions, plus a dedicated fast name-safety path, should precede any attempt to parallelize live moderation.

### 6. Prototype Piper after the catalog foundation

Piper is worthwhile mainly for language coverage and its existing voice library. Its official documentation confirms the normal two-file voice structure and the current training/export workflow.[^11][^13] The prototype should use one redistributable voice and compare two approaches:

1. a Sherpa-ONNX provider using a converted Piper/VITS package;
2. an out-of-process Piper executable, if a licensing review supports distribution.

Measure cold start, first audio, real-time factor, RAM, cancellation latency, package size, x64/arm64 behavior, and Store packaging. Promote Piper only if it adds a language or voice quality unavailable in Kokoro and remains within the stream-load budget.

## Work to defer

- **Voice cloning from user recordings:** This is not a built-in Kokoro capability, and it needs consent, identity, deletion, abuse-reporting, and provenance design.
- **Open community submissions:** Begin with developer-curated packages. Automated ONNX checks cannot establish voice ownership or redistribution rights.
- **Thousands of catalog entries:** Fifty well-tested, well-labeled voices are more useful and navigable than a large uncurated list.
- **MeloTTS:** SafeSpeak already has a working compact neural engine. Add another family only after a measured gap is identified.
- **Replacing KokoroSharp with Sherpa-ONNX:** Revisit when SafeSpeak needs multiple non-Kokoro model families or a shared desktop/mobile runtime.
- **A voice per chatter:** Constant voice switching can slow comprehension and increase cognitive load. Start with one active stream voice, optional per-platform voices, and a small event-voice set.

## Proposed delivery order

| Order | Deliverable | Completion evidence |
|---|---|---|
| 1 | Truthful Level 4 state and versioned package manifest | Unsupported packs cannot be selected or announced as ready. |
| 2 | Kokoro catalog expansion | Every listed voice synthesizes a new phrase, previews explicitly, persists, and works in a Store package. |
| 3 | Performance gates | Results captured under idle and simulated stream load; thread and timeout defaults documented. |
| 4 | Kokoro voice mixing | Saved blends survive restart and produce distinguishable output without additional models. |
| 5 | Signed catalog and transactional downloads | Offline cache, signature/hash failure, interrupted download, rollback, and screen-reader flows pass. |
| 6 | Constrained custom-model worker | Malformed, oversized, slow, external-data, crash, cancel, and shutdown tests fail safely. |
| 7 | Piper/Sherpa prototype | Licensing record and benchmark show a concrete language or quality benefit. |
| 8 | Curated custom packs | First approved packages pass provenance, license, security, accessibility, and performance gates. |

## Decision

SafeSpeak should implement the report's catalog, manifest, validation, and local inference ideas, but narrow the first release to Kokoro. The highest-value product improvement is expanding and organizing the voices the application can already synthesize. The highest-risk technical gap is the current custom-pack path, which presents package installation without executing the model. Fixing that contract and building an isolated provider are prerequisites for Piper packs or community distribution.

Sherpa-ONNX is worth a later prototype because it supports both Kokoro and Piper/VITS behind an Apache-licensed, cross-platform C API.[^8][^9] It should earn adoption through measurements and Store packaging evidence rather than replace the current engine on architectural promise alone.

## Sources

[^1]: “Architecture and Implementation of Low-Resource Offline TTS Voice Packs for Streamer Integration,” supplied research document, local attachment, reviewed September 2026.
[^2]: Lyrcaxis. “[KokoroSharp README](https://github.com/Lyrcaxis/KokoroSharp/blob/main/README.md).” Accessed September 2026.
[^3]: hexgrad. “[Kokoro-82M voice files](https://huggingface.co/hexgrad/Kokoro-82M/tree/main/voices).” Accessed September 2026.
[^4]: hexgrad. “[Kokoro-82M model card](https://huggingface.co/hexgrad/Kokoro-82M/blob/main/README.md).” Accessed September 2026.
[^5]: hexgrad. “[Kokoro-82M Voices](https://huggingface.co/hexgrad/Kokoro-82M/blob/main/VOICES.md).” Accessed September 2026.
[^6]: Microsoft. “[ONNX Runtime: Model Validation](https://onnxruntime.ai/docs/).” Accessed September 2026.
[^7]: ONNX. “[External Data Security](https://onnx.ai/onnx/repo-docs/ExternalDataSecurity.html).” Accessed September 2026.
[^8]: k2-fsa. “[sherpa-onnx repository](https://github.com/k2-fsa/sherpa-onnx).” Accessed September 2026.
[^9]: k2-fsa. “[sherpa-onnx C API: Text-to-Speech Models](https://k2-fsa.github.io/sherpa/onnx/c-api/html/tts.html).” Accessed September 2026.
[^10]: Open Home Foundation Voice. “[piper1-gpl](https://github.com/OHF-Voice/piper1-gpl).” Accessed September 2026.
[^11]: Open Home Foundation Voice. “[Piper Voices](https://github.com/OHF-Voice/piper1-gpl/blob/main/docs/VOICES.md).” Accessed September 2026.
[^12]: Microsoft. “[ONNX Runtime Thread Management](https://onnxruntime.ai/docs/performance/tune-performance/threading.html).” Accessed September 2026.
[^13]: Open Home Foundation Voice. “[Piper Training](https://github.com/OHF-Voice/piper1-gpl/blob/main/docs/TRAINING.md).” Accessed September 2026.
