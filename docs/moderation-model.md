# Local moderation model

SafeSpeak uses a layered local moderation path. No chat text, viewer name, or
classification result is sent to Google, Hugging Face, or another moderation
API.

1. Validate length, audience, cooldown, and writing-system rules.
2. Normalize invisible characters, repeated letters, spaced letters,
   diacritics, common homoglyphs, full-width forms, and leetspeak.
3. Apply mandatory severe-abuse rules and the user's custom banned terms.
4. Run a local MiniLM ONNX classifier over the normalized text.
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

## Runtime lifetime

Classifier instances hold leases on one shared local ONNX runtime, and each
inference holds its own temporary lease. Disposing the last classifier releases
the native model session. Window close begins that release in background cleanup
so model disposal cannot hold the interface open; the app exits after the
five-second shutdown deadline and does not show a blocking cleanup dialog.

## Failure and limitations

If model files are absent, altered, or fail at runtime, SafeSpeak keeps the
deterministic rules and hostile-language heuristic active. A display-name model
failure resolves to `A viewer`. An unexpected message-classifier failure rejects
the message rather than allowing unchecked speech.

This is a specialized classifier, not a generative local LLM. It was trained
primarily on English comments. It can miss novel context and can classify
friendly profanity as toxic. Release calibration must therefore use a
privacy-safe livestream corpus and measure both false negatives and false
positives at every moderation level. Platform moderation and trusted human
moderators remain necessary.
