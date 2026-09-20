# SafeSpeak Load Simulator

Speech experiments compare warmed Windows SAPI and Kokoro generation, then separately measure real audio playback at zero volume:

```powershell
dotnet run --project tools\SafeSpeak.LoadSimulator -c Release -- --speech-only
dotnet run --project tools\SafeSpeak.LoadSimulator -c Release -- --speech-only --speech-threads=2 --no-spin --no-playback
```

`--speech-threads` and `--no-spin` affect only the simulator's Kokoro instance, leaving application defaults unchanged. `--no-playback` skips device playback. Speech stages have a 60-second cancellation deadline; Kokoro native inference must finish before cancellation releases its lock. Failed stages are recorded in report notes. Memory includes the loaded MiniLM model; compare SAPI before Kokoro loads. Run without the filesystem/registry sandbox when measuring installed SAPI voices and audio devices.

For process-only MiniLM measurements paced at 1, 5 and 10 messages per second, including a ten-second idle baseline:

```powershell
dotnet run --project tools\SafeSpeak.LoadSimulator -c Release -- --paced-only
```

Each scenario lasts ten seconds. Average and p95 processing latency exclude pacing; the CPU measurement includes it. This exercises production moderation code, but excludes the WPF interface, connectors and speech. It is not a measurement of the full running application.

To measure the actual running application without including any other process:

```powershell
.\tools\SafeSpeak.LoadSimulator\Measure-AppProcess.ps1
```

This samples SafeSpeak for 30 seconds and saves JSON under `artifacts/performance`. Use `-ProcessId` to select a specific instance. Qwen/Ollama child processes are excluded and must be measured separately if used.

This tool measures the real bundled MiniLM intent model, the complete moderation pipeline, the installed Kokoro model, multiple Kokoro voice requests, and combined moderation plus synthesis load. It writes a timestamped JSON report under `artifacts/performance`.

Run from the repository root:

```powershell
dotnet run --project tools\SafeSpeak.LoadSimulator\SafeSpeak.LoadSimulator.csproj -c Release
```

To measure moderation without loading Kokoro:

```powershell
dotnet run --project tools\SafeSpeak.LoadSimulator\SafeSpeak.LoadSimulator.csproj -c Release -- --skip-kokoro
```

To use a Kokoro installation outside the normal Local App Data directory, pass its model directory as the first argument. The directory must contain `kokoro.onnx` and a `voices` subdirectory.

The reported CPU percentage is normalized across every logical processor available to the process. A result of 50 percent on an eight-thread machine means the process consumed approximately four logical processors for the measured interval. Short benchmark bursts intentionally show active inference cost; they do not represent SafeSpeak's idle CPU usage.

Run the simulator on representative low-end and streaming machines while OBS and the intended game are active. Preserve the generated JSON files outside the ignored `artifacts` directory when comparing release candidates.
