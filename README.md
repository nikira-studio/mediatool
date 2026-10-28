# MediaTool

MediaTool is a Windows desktop app for local video compression, audio extraction, and transcription. It uses FFmpeg and whisper.cpp to process media on your computer.

## Features

- Compress videos to MP4 with a target size, codec, resolution, and audio-track options.
- Extract audio as MP3.
- Transcribe audio or video to timestamped text, SRT, or WebVTT. Review the transcript in the app.
- Set defaults for each tool and queue jobs across tools.
- Drag files into a tool to start a job using its current settings.

Transcription models are downloaded separately when requested. Transcription runs locally after a model is installed. Large models can be slow on a CPU. Speaker identification is not currently available. Review transcripts for errors.

Settings, downloaded models, FFmpeg, and temporary files are stored under `%LOCALAPPDATA%\MediaTool`. Output files are saved in the selected folder or beside the source file. The first launch downloads FFmpeg directly from its publisher and requires an internet connection. Model downloads also require an internet connection. Media processing runs locally afterward.

## Build on Windows

The project targets 64-bit Windows and .NET 10. The bootstrap script installs the pinned .NET SDK locally and downloads the pinned FFmpeg and whisper.cpp binaries. PowerShell and internet access are required.

```powershell
.\scripts\bootstrap.ps1
.\scripts\verify.ps1 -SkipLaunch
```

To create a self-contained portable executable and ZIP under `releases`:

```powershell
.\scripts\publish-portable.ps1
```

The portable build includes the runtime, whisper.cpp, and license notices. FFmpeg is downloaded on first launch. A speech model must be downloaded or imported separately in the app. The build scripts and dependency manifests identify pinned versions and checksums.

## License and third-party components

MediaTool's own code is released under [The Unlicense](LICENSE). Third-party components retain their own licenses. whisper.cpp is MIT licensed. FFmpeg is downloaded separately from its publisher under GPL-3.0-or-later and is not included in the portable package. See [third-party notices](sidecars/THIRD-PARTY-NOTICES.md) and the manifests in that directory.
