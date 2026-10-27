using System.Collections.Concurrent;
using MediaTool.Core;

namespace MediaTool.Core.Tests;

public sealed class SequentialJobServiceTests
{
    [Fact]
    public async Task ProcessesJobsInQueueOrderAndContinuesAfterFailure()
    {
        var order = new ConcurrentQueue<string>();
        var handler = new RecordingHandler(order);
        await using var service = new SequentialJobService([handler]);
        var first = MediaJob.Create(Request("first.mp4"));
        var second = MediaJob.Create(Request("fail.mp4"));
        var third = MediaJob.Create(Request("third.mp4"));

        await service.EnqueueAsync(first);
        await service.EnqueueAsync(second);
        await service.EnqueueAsync(third);
        await WaitUntilAsync(() => JobStateMachine.IsTerminal(third.State));

        Assert.Equal(["first.mp4", "fail.mp4", "third.mp4"], order);
        Assert.Equal(JobState.Completed, first.State);
        Assert.Equal(JobState.Failed, second.State);
        Assert.Equal(JobState.Completed, third.State);
    }

    [Fact]
    public async Task UsesOneQueueAcrossDifferentTools()
    {
        var order = new ConcurrentQueue<string>();
        await using var service = new SequentialJobService(
        [
            new RecordingHandler(order, ToolId.VideoCompress),
            new RecordingHandler(order, ToolId.AudioExtract)
        ]);
        var video = MediaJob.Create(Request("video.mp4"));
        var audio = MediaJob.Create(new AudioExtractionRequest(
            MediaInput.FromPath(Path.Combine(Path.GetTempPath(), "meeting.mp4")),
            new(null), BitRate.FromKilobitsPerSecond(192), AudioTrackMode.Default, []));

        await service.EnqueueAsync(video);
        await service.EnqueueAsync(audio);
        await WaitUntilAsync(() => JobStateMachine.IsTerminal(audio.State));

        Assert.Equal(["video.mp4", "meeting.mp4"], order);
        Assert.Equal(JobState.Completed, video.State);
        Assert.Equal(JobState.Completed, audio.State);
    }

    private static CompressionRequest Request(string name) => new(
        MediaInput.FromPath(Path.Combine(Path.GetTempPath(), name)),
        new(null), FileSize.FromMegabytes(20), VideoCodec.H264, MaximumResolution.Auto, AudioTrackMode.Default, []);

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }

    private sealed class RecordingHandler(ConcurrentQueue<string> order, ToolId tool = ToolId.VideoCompress) : IJobHandler
    {
        public bool CanHandle(ToolId candidate) => candidate == tool;

        public async Task<JobResult> ExecuteAsync(MediaJob job, CancellationToken cancellationToken)
        {
            job.TransitionTo(JobState.Inspecting);
            await Task.Delay(15, cancellationToken);
            order.Enqueue(job.Request.Input.FileName);
            job.TransitionTo(JobState.Ready);
            job.TransitionTo(JobState.Processing);
            return job.Request.Input.FileName == "fail.mp4"
                ? JobResult.Failed(new("test_failure", "Expected failure.", "Continue."))
                : JobResult.Completed();
        }
    }
}
