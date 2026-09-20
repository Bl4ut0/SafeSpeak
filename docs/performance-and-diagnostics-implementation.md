# Performance caching and diagnostics implementation

## App behavior

Name-score caching lasts across arm/disarm within the running application. It is
memory-only, bounded to 1,024 names and 15 minutes per result. Names are not permanently
trusted: deterministic rules run against current configuration and toxicity thresholds
apply to cached scores. The normalized input and classifier instance identify entries;
classifier replacement clears the cache. Failed/unavailable inference is not cached.

Each playback queue retains up to 32 short speech WAVs, with a total 4 MB payload
limit, ten-minute expiry and 512 KB per entry. Exact text, voice and rate identify
entries. This reuses repeated prompts and repeated short chat only when speech is
identical. The cache stays in memory and survives arm/disarm, while new content
still goes through moderation. Voice-package imports/deletions invalidate audio,
including prefetch settings via a generation marker. Shutdown drops both caches.

The app's Kokoro manager and MiniLM use at most two inference threads with ONNX
waiting spins disabled. Kokoro remains serialized and the existing one-message
prefetch is retained. This bounds model parallelism, **not total app CPU percent**.
SAPI, UI, connectors and optional Ollama/Qwen have their own execution paths.
Kokoro's approximately 1 GB loaded-model footprint remains.

`NameScoreCacheHit` and `TtsAudioCacheHit` operation counts appear in existing
subsystem performance snapshots. In the ten-messages/second moderation test on
this machine, process CPU measured 2.18% with average 6.69 ms processing, compared
with 25.62% and 7.07 ms in the prior test. Both caching and thread changes are
included; these are short simulator measurements, not a universal CPU guarantee.
Report: `artifacts/performance/load-simulation-20260916-145451.json`.

## Diagnostics

Settings offers local ZIP export and explicit upload/cancel controls. It packages
the last ten days, automatically includes raw stream audit logs alongside sanitized diagnostic
logs, and includes controlled system information.

`DiagnosticHostId` is a random GUID persisted in app settings, not a hardware
identifier or login credential. It correlates reports from that installation.
`system-info.json` includes versions, OS/runtime/architecture, logical processor
count, selected model/voice, thread configuration and the current performance/state
snapshot. Full settings, computer names, serials and saved credentials are not exported.

Uploads use HTTPS to `https://safespeak.bl4ut0.dev/diagnostics/index.php`. A
developer-issued single-use code is exchanged for a five-minute, single-use ticket
bound to package size and SHA-256. Codes are not saved in app settings. There is no
reusable developer secret inside the application. Cancellation/ambiguous failures
require a new code. Success requires a valid server receipt. Closing the app cancels
and awaits the upload; temporary ZIPs are removed on completion/failure.

Client upload size is capped at 25 MB, responses at 4 KB. The PHP receiver validates
ZIP paths, text-only entries, checksums, compression/encryption, expansion limits
and duplicates, and stores archives without extracting them, outside the public
document root. Host metadata also gets a private receipt index. Validated text
remains untrusted; format checks do not constitute antivirus scanning.

The private receiver site is intentionally Git-ignored:
`local-deployment/safespeak-diagnostics/`. Its README contains Virtualmin/PHP-FPM
storage/environment, request limits and one-time-code issuance instructions.
It has now been deployed using the website helper's explicit `--receiver` profile
and the user-authorized portfolio credentials. Its private storage is beside
the site's public_html; authorization, stored hash and receipts were verified
with a synthetic ZIP, then test artifacts were removed. Payload format version
1 and application version are required. Codes are host-bound, files are grouped
by host ID and UTC timestamp, and server limits are 60 requests/minute per
connecting IP, three accepted uploads/hour/host and ten/day/host. The receiver
accepts a dedicated ticket header for Apache/FastCGI compatibility.

Diagnostics intake now defaults OFF and was left OFF after deployment tests.
The receiver runs independently on the website. Edit the private server PHP
configuration at `/domains/safespeak.bl4ut0.dev/diagnostics-private/config.php`:
set `enabled` to true to open intake or false to close it. No local command or
process is required. Future deployments preserve the configuration. Optional
expiry and source-IP restrictions default to null. Host-bound one-use codes,
rate limits and private storage remain enforced. Receipt metadata records
handshake/upload IP addresses for correlation, not proof of executable identity.

## Validation

Release app build succeeded (existing nullable warnings remain). Full Core suite:
573 passed with host speech/IPC access. App accessibility contracts: 147 passed.
Final targeted cache/ZIP/upload/settings/queue checks: 67 passed, including host ID persistence. PHP lint passed;
local receiver integration exercised valid uploads, metadata, replay/expiry,
authorization, hash binding, traversal/executable/binary/duplicate rejection,
missing manifests, expanded size limits and failed-upload cleanup. No production
uploads or website changes were performed.

Current upload flow: one Upload diagnostics button, no support-code entry. The app signs host ID, ZIP size and SHA-256 using an embedded, clonable HMAC marker (version 1). The server checks it and issues a short-lived one-use transfer ticket automatically. This is not proof of app authenticity. Uploads include all available .log files from the last 10 days, including raw StreamAudit chat history, plus system/version/performance details automatically without requiring confirmation prompts. PHP intake configuration, rate limits and ZIP validation remain enforced. Legacy support-code handshakes are accepted for compatibility.

Speech diagnostics now count enqueue/start/complete/cancel/failure, stale drops, capacity/disarmed rejections, cache misses and prefetch failures. Periodic state includes queue capacity, oldest pending message age and active speech duration. Synthesis over three seconds emits a warning with voice, character count, bytes and queue depth. Actual synthesis/playback exceptions are logged while queue processing continues; expected cancellation is counted separately from failures. These records contain no chat text. Queue freshness/capacity and playback behavior remain unchanged pending long-session evidence.
