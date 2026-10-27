# MediaTool architecture

## Usage

The WPF shell creates a typed request and submits it to `IJobService`:

```csharp
var job = MediaJob.Create(request);
await jobs.EnqueueAsync(job, cancellationToken);
```

The shell never builds an FFmpeg argument or starts a process. A handler in the
infrastructure layer inspects the media, prepares a unique temporary output, runs the
sidecar through `IProcessRunner`, validates the result, and atomically finalizes it.

## Ownership

- `MediaTool.App` owns the window, navigation, focus, dialogs, theme, and view state.
- `MediaTool.Core` owns validated values, requests, results, job states, transitions,
  queue order, and infrastructure contracts. It has no WPF reference.
- `MediaTool.Infrastructure` owns sidecar processes, FFprobe JSON parsing, output files,
  `%LOCALAPPDATA%` storage, cleanup, and diagnostic redaction. It has no WPF reference.
- Tests call Core and Infrastructure directly without a WPF test host.

## Module flow

```text
MediaTool.App
    -> MediaTool.Core <- MediaTool.Infrastructure
                            -> ffprobe / ffmpeg / whisper-cli
                            -> local settings, history, temp files
```

## Main failure path

The handler writes only to a job-specific temporary path. Cancellation kills the full
process tree. Failure and cancellation delete temporary files. Success requires an
output validator to accept the temporary artifact before `IOutputFileService` moves it
to a conflict-safe destination. Existing files are never opened for replacement.

## Alternatives considered

A single WPF project would take less setup, but would make process, parsing, and file
safety tests depend on a UI host. A generic plug-in framework would add contracts that
the three v1 tools do not need. The selected three-project shape keeps the required
boundary without inventing a framework.

## Assumptions and risks

- Jobs are sequential in v1, so the queue has one reader and isolated job state.
- The app uses the pinned sidecars only. Packaging metadata and binaries arrive in the
  tool implementation phases.
- `Process.Kill(entireProcessTree: true)` is the process-tree cancellation mechanism.
- Atomic finalization requires the temporary and destination paths to share a volume.
  Temporary outputs therefore live in the selected destination directory.
