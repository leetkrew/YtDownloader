# LeetkrewYtDownloader

A lightweight YouTube downloader built with .NET and Avalonia UI.

Currently tested only on **macOS ARM64** — other platforms (Windows/Linux) are expected to work but remain **untested**.

---

## 🖥️ Features

- ✅ macOS ARM64 support (tested)
- 🧪 Windows/Linux compatibility (untested)
- 📥 Download YouTube videos using [YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode)
- 🎞️ Converts to playable formats with [FFmpeg](https://ffmpeg.org/)
- 🧩 Self-contained build — no need to install .NET separately
- 🍎 `.app` and `.dmg` packaging scripts included

---

## 📦 Build Instructions

### 🧰 Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download)
- [FFmpeg](https://ffmpeg.org/download.html)
- macOS with Apple Silicon (tested on ARM64)

### 🔨 Publish for macOS (ARM64)

```bash
dotnet publish -f net8.0-macos-arm64 -c Release \
  -p:PublishSingleFile=true \
  -p:SelfContained=true \
  -p:Trim=true \
  -o ./publish
