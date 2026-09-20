# Speech and moderation performance audit

Measured 16 September 2026 on this machine, using eight logical processors. CPU percentages use only the benchmark process's accumulated CPU time, normalized across those processors. Other applications are excluded; their activity can still affect latency. These are short workload comparisons, not whole-app steady-state measurements.

## Measured speech generation

Four repetitions of the same short announcement, using four Kokoro voices. The model is warmed before measurement. Kokoro output totals approximately 18.8 seconds. Windows SAPI uses its default voice and totals approximately 21.3 seconds; this is a resource comparison, not a voice-quality equivalence test.

| Configuration | Synthesis elapsed | CPU during synthesis | Accumulated CPU time |
|---|---:|---:|---:|
| Kokoro existing library defaults | 17.23 s | 62.44% | 86.06 s |
| Kokoro 4 threads, spinning disabled | 8.31 s | 36.09% | 23.98 s |
| Kokoro 2 threads, spinning disabled | 9.75 s | 22.48% | 17.53 s |
| Kokoro 1 thread, spinning disabled | 14.72 s | 13.05% | 15.36 s |
| Windows SAPI, warmed | 0.18–0.20 s | 14–16% for this brief burst | 0.20–0.25 s |

Cached WAV playback alone measured 0.54% CPU for SAPI output and 0.65% for Kokoro output. This used the real shared-mode Windows audio router at zero volume. Kokoro stages used approximately 1 GB working set, including the loaded MiniLM model. SAPI measured before Kokoro loads used approximately 214–223 MB, also including MiniLM. The first SAPI trial ran after Kokoro warmed, so its larger working set cannot be attributed to SAPI.

Raw reports are under ignored `artifacts/performance`, dated 20260916: 143903 (defaults/playback), 143953 (two threads), 144025 (one thread), 144057 (four threads).

## Recommendations

The production TTS queue test with two threads and spinning disabled completed four announcements using Heart, including one-message prefetch and actual muted playback, while MiniLM moderated chat at one message per second. The 25-second window averaged **10.84% process CPU**, 21.69 seconds accumulated CPU time, approximately 1,064 MB peak working set, and 44 intent calls. A repeat two-thread synthesis stage measured 22.47% CPU and 10.03 seconds elapsed. This excludes WPF, connectors and Qwen, and is not an indefinite continuous speech test. Report: `artifacts/performance/load-simulation-20260916-144223.json`.

1. Trial two Kokoro threads with spinning disabled as a balanced profile. On this machine it preserves useful generation headroom with substantially less CPU time. One thread is a candidate economy profile, but has less headroom for longer messages and slower computers. Validate longer text, speech rate changes, cancellation and OBS/game coexistence before changing production defaults.
2. Keep the shared Kokoro model and single-message prefetch. The existing implementation serializes synthesis across voices, and buffers only the next queued message. Removing prefetch would increase gaps; increasing it would create additional unnecessary work when messages are skipped or cleared.
3. Cache repeated name classifications in bounded memory. Each approved chat currently can classify both its message and its author's display name; each mention can add another classification. Returning viewers can avoid the repeated name inference. Two calls can become one for an approved message with no mentions; this does not imply a 50% reduction in total app CPU.
4. Re-run cheap deterministic name checks against current rules, and cache model scores rather than a permanently trusted name verdict. Use the exact normalized model input and classifier identity/version as the key. Apply current thresholds to cached scores, expire entries, cap their count and discard them when replacing/reconfiguring the classifier. Do not cache cancellation or inference failures. Preserve exact display-name cleanup separately; changed names must be checked again.
5. Cache generated audio for short, frequently repeated application prompts in a small byte-limited cache keyed by exact text, engine/model, voice and rate. Apply volume during playback as today. Avoid caching arbitrary live chat to disk. A separate username-audio fragment cache would require testing joins/prosody and the reported clipped-name issue; name safety-score caching is safer to prioritize.
6. Keep Windows voices available as a low-resource choice. They were dramatically cheaper in this test. Offer explicit user choice and preview; do not silently switch away from a selected neural voice.
7. Examine MiniLM worker spinning next. The existing model uses up to four ONNX threads and does not explicitly disable spinning. Its paced CPU cost may include waiting spins; that is a hypothesis requiring a controlled comparison, not an attribution established by the current speech results.

ONNX documents that worker spinning trades faster response for additional CPU and power: [ONNX threading documentation](https://onnxruntime.ai/docs/performance/tune-performance/threading.html). KokoroSharp accepts session options: [KokoroWavSynthesizer source](https://github.com/Lyrcaxis/KokoroSharp/blob/main/Utilities/KokoroWavSynthesizer.cs). The project pins ONNX 1.22.0 and KokoroSharp 0.8.4; newer documentation defaults should not be assumed for those pinned versions.

## Changes made for this audit

The simulator now supports speech-only tests, explicit Kokoro thread/spinning experiments, separately measured muted playback, and optional production TTS queue/prefetch with paced moderation. Speech stages report failures instead of terminating the test, and sampler tasks are cleaned up even when an operation fails. Kokoro accepts optional session-tuning parameters for these experiments; application defaults remain unchanged.

Initial sandbox runs failed on application-data voice staging and Windows speech registry access. Those are excluded from performance results. The successful device/SAPI test runs used normal host access. The application's observed close was logged as exit code 0, with connector shutdown, rather than an unhandled crash.
