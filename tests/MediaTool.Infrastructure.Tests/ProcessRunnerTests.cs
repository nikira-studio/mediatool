using System.Diagnostics;
using System.Text;
using MediaTool.Core;
using MediaTool.Infrastructure;

namespace MediaTool.Infrastructure.Tests;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task PassesArgumentsWithoutShellInterpolation()
    {
        var runner = new ProcessRunner();
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var value = "a path with spaces & symbols";
        var command = $"[Console]::Out.WriteLine('{value.Replace("'", "''")}')";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        var result = await runner.RunAsync(new(executable, ["-NoProfile", "-EncodedCommand", encoded]), CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(value, Assert.Single(result.StandardOutput));
    }

    [Fact]
    public async Task CancellationKillsTheFullProcessTree()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "MediaToolProcessTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        try
        {
            var marker = Path.Combine(testDirectory, "child-survived.txt");
            var childCommand = $"Start-Sleep -Seconds 2; [IO.File]::WriteAllText('{marker.Replace("'", "''")}', 'survived')";
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(childCommand));
            var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            var childPid = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var parentCommand = $"$child = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList @('-NoProfile','-EncodedCommand','{encoded}') -PassThru; Write-Output $child.Id; Start-Sleep -Seconds 30";
            using var cancellation = new CancellationTokenSource();
            var runTask = new ProcessRunner().RunAsync(new(executable, ["-NoProfile", "-Command", parentCommand], StandardOutputLine: line =>
                {
                    if (int.TryParse(line, out var pid)) childPid.TrySetResult(pid);
                }), cancellation.Token);
            var pid = await childPid.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
            await Task.Delay(TimeSpan.FromSeconds(3));

            Assert.False(File.Exists(marker));
            Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
        }
        finally
        {
            Directory.Delete(testDirectory, true);
        }
    }
}
