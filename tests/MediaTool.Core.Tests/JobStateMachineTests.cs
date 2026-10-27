using MediaTool.Core;

namespace MediaTool.Core.Tests;

public sealed class JobStateMachineTests
{
    [Theory]
    [InlineData(JobState.Queued, JobState.Inspecting)]
    [InlineData(JobState.Inspecting, JobState.Ready)]
    [InlineData(JobState.Ready, JobState.Processing)]
    [InlineData(JobState.Processing, JobState.Retrying)]
    [InlineData(JobState.Retrying, JobState.Processing)]
    [InlineData(JobState.Processing, JobState.Completed)]
    [InlineData(JobState.Cancelling, JobState.Cancelled)]
    public void AllowsRequiredTransitions(JobState current, JobState next)
    {
        JobStateMachine.EnsureTransition(current, next);
    }

    [Theory]
    [InlineData(JobState.Queued, JobState.Completed)]
    [InlineData(JobState.Completed, JobState.Processing)]
    [InlineData(JobState.Cancelled, JobState.Queued)]
    public void RejectsInvalidTransitions(JobState current, JobState next)
    {
        Assert.Throws<InvalidOperationException>(() => JobStateMachine.EnsureTransition(current, next));
    }

    [Theory]
    [InlineData(JobState.Completed)]
    [InlineData(JobState.Skipped)]
    [InlineData(JobState.Cancelled)]
    [InlineData(JobState.Failed)]
    [InlineData(JobState.Interrupted)]
    public void IdentifiesTerminalStates(JobState state)
    {
        Assert.True(JobStateMachine.IsTerminal(state));
    }
}
