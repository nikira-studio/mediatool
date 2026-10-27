using System.Threading.Channels;

namespace MediaTool.Core;

public sealed class MediaJob
{
    private readonly object gate = new();

    public JobId Id { get; } = JobId.New();
    public IMediaRequest Request { get; }
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public JobState State { get; private set; } = JobState.Queued;
    public JobProgress? Progress { get; private set; }
    public JobResult? Result { get; private set; }
    public event EventHandler<MediaJob>? Changed;

    private MediaJob(IMediaRequest request) => Request = request ?? throw new ArgumentNullException(nameof(request));

    public static MediaJob Create(IMediaRequest request) => new(request);

    public void TransitionTo(JobState next)
    {
        lock (gate)
        {
            JobStateMachine.EnsureTransition(State, next);
            State = next;
            if (next == JobState.Inspecting) StartedAt ??= DateTimeOffset.UtcNow;
            if (JobStateMachine.IsTerminal(next)) FinishedAt = DateTimeOffset.UtcNow;
        }
        Changed?.Invoke(this, this);
    }

    public void Report(JobProgress progress)
    {
        lock (gate) Progress = progress;
        Changed?.Invoke(this, this);
    }

    public void Complete(JobResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        lock (gate)
        {
            JobStateMachine.EnsureTransition(State, result.State);
            State = result.State;
            Result = result;
            FinishedAt = DateTimeOffset.UtcNow;
        }
        Changed?.Invoke(this, this);
    }
}

public static class JobStateMachine
{
    private static readonly IReadOnlyDictionary<JobState, IReadOnlySet<JobState>> Allowed =
        new Dictionary<JobState, IReadOnlySet<JobState>>
        {
            [JobState.Queued] = Set(JobState.Inspecting, JobState.Cancelled, JobState.Interrupted),
            [JobState.Inspecting] = Set(JobState.Ready, JobState.Failed, JobState.Cancelling, JobState.Interrupted),
            [JobState.Ready] = Set(JobState.Processing, JobState.Skipped, JobState.Failed, JobState.Cancelling, JobState.Interrupted),
            [JobState.Processing] = Set(JobState.Retrying, JobState.Completed, JobState.Failed, JobState.Cancelling, JobState.Interrupted),
            [JobState.Retrying] = Set(JobState.Processing, JobState.Failed, JobState.Cancelling, JobState.Interrupted),
            [JobState.Cancelling] = Set(JobState.Cancelled, JobState.Failed),
            [JobState.Cancelled] = Set(),
            [JobState.Completed] = Set(),
            [JobState.Skipped] = Set(),
            [JobState.Interrupted] = Set(),
            [JobState.Failed] = Set()
        };

    public static void EnsureTransition(JobState current, JobState next)
    {
        if (!Allowed[current].Contains(next))
            throw new InvalidOperationException($"Job cannot transition from {current} to {next}.");
    }

    public static bool IsTerminal(JobState state) => Allowed[state].Count == 0;

    private static IReadOnlySet<JobState> Set(params JobState[] states) => new HashSet<JobState>(states);
}

public interface IJobHandler
{
    bool CanHandle(ToolId tool);
    Task<JobResult> ExecuteAsync(MediaJob job, CancellationToken cancellationToken);
}

public interface IJobService : IAsyncDisposable
{
    event EventHandler<MediaJob>? JobChanged;
    IReadOnlyCollection<MediaJob> Jobs { get; }
    ValueTask EnqueueAsync(MediaJob job, CancellationToken cancellationToken = default);
    bool RemoveQueued(JobId id);
    bool CancelActive();
}

public sealed class SequentialJobService : IJobService
{
    private readonly Channel<MediaJob> queue = Channel.CreateUnbounded<MediaJob>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly IReadOnlyList<IJobHandler> handlers;
    private readonly List<MediaJob> jobs = [];
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task worker;
    private CancellationTokenSource? activeCancellation;
    private MediaJob? activeJob;

    public SequentialJobService(IEnumerable<IJobHandler> handlers)
    {
        this.handlers = handlers.ToArray();
        worker = ProcessQueueAsync();
    }

    public event EventHandler<MediaJob>? JobChanged;

    public IReadOnlyCollection<MediaJob> Jobs
    {
        get { lock (gate) return jobs.ToArray(); }
    }

    public async ValueTask EnqueueAsync(MediaJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        lock (gate) jobs.Add(job);
        job.Changed += OnJobChanged;
        JobChanged?.Invoke(this, job);
        await queue.Writer.WriteAsync(job, cancellationToken).ConfigureAwait(false);
    }

    private void OnJobChanged(object? sender, MediaJob job) => JobChanged?.Invoke(this, job);

    public bool RemoveQueued(JobId id)
    {
        lock (gate)
        {
            var job = jobs.FirstOrDefault(candidate => candidate.Id == id && candidate.State == JobState.Queued);
            if (job is null) return false;
            job.Complete(JobResult.Cancelled(new("cancelled", "Removed from the queue.", "Add the file again to retry.")));
            JobChanged?.Invoke(this, job);
            return true;
        }
    }

    public bool CancelActive()
    {
        lock (gate)
        {
            if (activeJob is null || activeCancellation is null || JobStateMachine.IsTerminal(activeJob.State)) return false;
            if (activeJob.State != JobState.Cancelling) activeJob.TransitionTo(JobState.Cancelling);
            activeCancellation.Cancel();
            JobChanged?.Invoke(this, activeJob);
            return true;
        }
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            await foreach (var job in queue.Reader.ReadAllAsync(lifetime.Token).ConfigureAwait(false))
            {
                if (job.State != JobState.Queued) continue;
                var handler = handlers.SingleOrDefault(candidate => candidate.CanHandle(job.Request.Tool));
                if (handler is null)
                {
                    job.Complete(JobResult.Failed(new("handler_unavailable", "This tool is not installed.", "Install or repair MediaTool.")));
                    JobChanged?.Invoke(this, job);
                    continue;
                }

                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                lock (gate) { activeJob = job; activeCancellation = cancellation; }
                try
                {
                    var result = await handler.ExecuteAsync(job, cancellation.Token).ConfigureAwait(false);
                    if (!JobStateMachine.IsTerminal(job.State)) job.Complete(result);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    if (!JobStateMachine.IsTerminal(job.State))
                        job.Complete(JobResult.Cancelled(new("cancelled", "The job was cancelled.", "Start it again when ready.")));
                }
                catch (Exception exception)
                {
                    if (!JobStateMachine.IsTerminal(job.State))
                        job.Complete(JobResult.Failed(new("unexpected_error", "The job could not finish.", "Open details, then retry.", exception.ToString())));
                }
                finally
                {
                    lock (gate) { activeJob = null; activeCancellation = null; }
                    JobChanged?.Invoke(this, job);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        queue.Writer.TryComplete();
        lifetime.Cancel();
        try { await worker.ConfigureAwait(false); } catch (OperationCanceledException) { }
        lifetime.Dispose();
    }
}
