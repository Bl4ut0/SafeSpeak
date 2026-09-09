# Local moderation model

SafeSpeak uses a layered local moderation path. No chat text, viewer name, or
classification result is sent to Google, Hugging Face, or another moderation
API.

1. Validate length, audience, cooldown, and writing-system rules.
2. Normalize invisible characters, repeated letters, spaced letters,
   diacritics, common homoglyphs, full-width forms, and leetspeak.
3. Apply mandatory severe-abuse rules and the user's custom banned terms.
4. Run the selected local classifier over the normalized text. The bundled
   MiniLM ONNX model is the default.
5. Combine its result with a deterministic hostile-language fallback.
6. Compare the shared score with Relaxed, Balanced, Strong, or Maximum.
7. Clean approved text for speech.

Viewer display names go through their own script, rule, model, and speech-cleanup
pass. An unsafe or invalid name is replaced with `A viewer`; it is never copied
into approved speech first. Approved chat is spoken as `name says: message`.

## Bundled model

- Model: `navodPeiris/minilm-toxic-classifier`
- Revision: `4831179af569756699fdd6132a520dcdbfe07f03`
- Architecture: MiniLMv2-L6-H384, about 23 million parameters
- Format/runtime: ONNX, CPU inference through Microsoft ONNX Runtime
- Size: about 91 MB
- Labels: toxic, severe toxicity, obscene, threat, insult, identity hate
- Model license declared by its model card: Apache-2.0
- Training source: Jigsaw Toxic Comment Classification dataset
- Model SHA-256: `935BA953C9D4478D809DB1A2FA40181F42BF1670D1E69261478B2137C1FBACC5`
- Tokenizer SHA-256: `851CA67100D372CA3AE031A6ABD168F53489EEBFD7D89523F35C5C9B4D372C3C`

SafeSpeak verifies both hashes before loading the model. The six published
per-label decision thresholds are calibrated to one shared score so the four
product levels behave consistently across common toxicity and rarer threats.

## Optional Qwen3Guard 0.6B compressed model

The Safety page offers `Qwen3Guard 0.6B compressed (optional)` as a second
local contextual model. SafeSpeak packages an architecture-matched, CPU-only
Ollama command-line runtime under its own application directory. It does not
install a service, modify `PATH`, add another application, or use a separately
installed Ollama instance. Selecting Qwen3Guard never starts a download. The
user must activate the accessible **Install optional model** button, which
reports progress and can be canceled. The adjacent remove button deletes the
optional model from SafeSpeak local data.

The managed runtime binds a random Windows loopback port for the current
SafeSpeak process, disables Ollama cloud features, stores model data under
`%LOCALAPPDATA%\SafeSpeak\Models\Qwen3Guard`, and is stopped as part of the
app's non-blocking shutdown. Non-loopback endpoints are rejected. The model is
based on the Apache-2.0
[Qwen3Guard-Gen-0.6B](https://huggingface.co/Qwen/Qwen3Guard-Gen-0.6B), which
produces native `Safe`, `Controversial`, and `Unsafe` verdicts. SafeSpeak maps
those verdicts to 0%, 55%, and 95% contextual scores and combines them with the
bundled local result. A controversial verdict alone therefore passes Strong
(level 3) and remains blockable at Maximum (level 4); an unsafe verdict blocks
at every level. Deterministic severe-abuse and anti-evasion rules always run
first, and the bundled classifier can independently raise the fused score.

The current compressed artifact is `sileader/qwen3guard:0.6b`, approximately
484 MB. SafeSpeak pins and verifies its model blob SHA-256 as
`2a53e0ce1cdd156b4dcf023e4f7285d4cc1d0d32bbbb0ffa9baed8f5c617f4ad`.
The runtime is Ollama 0.33.3 from the official project release. Release builds
verify the full x64 or Arm64 archive hash before retaining only its CPU runtime
and accompanying license files.

If the managed runtime is missing, the model is not installed, a request times
out, or a response is malformed, SafeSpeak uses the bundled classifier result. The UI
does not call Qwen3Guard active until it receives a valid response. This option
can improve multilingual and novel-context coverage, but it costs more CPU,
memory, and per-message latency. The English-only and mixed-writing-system
switches still run before either contextual model.

## Runtime lifetime

Classifier instances hold leases on one shared local ONNX runtime, and each
inference holds its own temporary lease. Disposing the last classifier releases
the native model session. Window close begins that release in background cleanup
so model disposal cannot hold the interface open; the app exits after the
five-second shutdown deadline and does not show a blocking cleanup dialog.

## Failure and limitations

If bundled model files are absent, altered, or fail at runtime, SafeSpeak keeps
the deterministic rules and hostile-language heuristic active. If the optional
Qwen3Guard service fails, SafeSpeak uses that same bundled path. A display-name
model failure resolves to `A viewer`. An unexpected message-classifier failure
rejects the message rather than allowing unchecked speech.

This is a specialized classifier, not a generative local LLM. It was trained
primarily on English comments. It can miss novel context and can classify
friendly profanity as toxic. Release calibration must therefore use a
privacy-safe livestream corpus and measure both false negatives and false
positives at every moderation level. Platform moderation and trusted human
moderators remain necessary.
