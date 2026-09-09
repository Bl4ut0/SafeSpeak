# SafeSpeak private model runtime

SafeSpeak packages the command-line/server portion of Ollama 0.33.3 to run the
optional Qwen3Guard moderation model locally. It does not package or launch the
separately licensed Ollama desktop application.

Source: https://github.com/ollama/ollama/tree/v0.33.3

License: MIT. See `LICENSE.txt` in this directory. Dependency license files
from the official standalone archive are retained under `lib/ollama`.

The x64 package intentionally omits CUDA and Vulkan libraries. SafeSpeak's
managed Qwen3Guard service therefore uses the CPU and does not install drivers,
services, Start-menu entries, environment variables, or system-wide software.
