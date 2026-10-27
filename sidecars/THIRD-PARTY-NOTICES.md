# Third-party notices

MediaTool includes third-party software under separate licenses. The license files
and the FFmpeg build information accompany the portable package in this folder.

- FFmpeg and ffprobe: Gyan's 9.0.1 essentials build, GPL-3.0-or-later. The
  upstream build README identifies FFmpeg source revision `bf1b838f2a` and
  lists the configuration and external libraries. See `FFmpeg-README.txt`
  and `FFmpeg-LICENSE.txt`.
- whisper.cpp and ggml: build `b4938`, MIT license. See `whisper-LICENSE.txt`.
- WPF UI: version 4.3.0, MIT license. See `WPF-UI-LICENSE.md` and
  `WPF-UI-THIRD-PARTY-NOTICES.txt`.
- .NET runtime and Windows Desktop runtime: see the accompanying .NET license
  and third-party notices.

The upstream FFmpeg source revision is
https://github.com/FFmpeg/FFmpeg/commit/bf1b838f2a. The static FFmpeg build
also contains external libraries listed in the upstream build README. This link
alone is not a complete corresponding-source offer for that build.

Before distributing this package, the distributor must arrange access to the
complete corresponding source and build materials for the exact FFmpeg build,
including its statically linked components, and comply with the applicable
licenses. The availability of those materials has not yet been verified.
