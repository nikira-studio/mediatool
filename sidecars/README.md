# Packaged sidecars

MediaTool does not read FFmpeg from `PATH`. `scripts/fetch-ffmpeg.ps1` downloads the
pinned archive recorded in `ffmpeg.json`, checks its SHA-256, and copies `ffmpeg.exe`,
`ffprobe.exe`, and the upstream notices to `.tools/ffmpeg`.

The selected Gyan release essentials build is static, 64-bit, and GPLv3. It includes
`libx264`, `libx265`, and `libmp3lame`. Anyone redistributing a MediaTool package that
contains these binaries must comply with the GPL and make the corresponding sources
available. MediaTool's source repository should keep this fetch script and manifest,
not the downloaded binaries.
