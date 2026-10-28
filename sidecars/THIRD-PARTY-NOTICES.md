# Third-party notices

MediaTool includes third-party software under separate licenses. The license files
accompany the portable package in this folder.

- whisper.cpp and ggml: build `b4938`, MIT license. See `whisper-LICENSE.txt`.
- WPF UI: version 4.3.0, MIT license. See `WPF-UI-LICENSE.md` and
  `WPF-UI-THIRD-PARTY-NOTICES.txt`.
- .NET runtime and Windows Desktop runtime: see the accompanying .NET license
  and third-party notices.

FFmpeg and ffprobe are not included in the MediaTool package. On first use,
MediaTool downloads Gyan's FFmpeg 9.0.1 essentials package directly from
https://www.gyan.dev/ffmpeg/builds/. That independently distributed package is
licensed under GPL-3.0-or-later and contains its own license and build information.
The download's archive and binaries are checked against pinned SHA-256 hashes.
