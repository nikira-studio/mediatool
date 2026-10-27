using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MediaTool.Core;

namespace MediaTool.App;

public partial class AudioExtractView : UserControl
{
    private readonly IJobService jobs;
    private readonly IMediaInspector inspector;
    private readonly SettingsController settings;
    public ObservableCollection<InputRow> Inputs { get; } = [];
    public ObservableCollection<JobRow> Jobs { get; } = [];

    public AudioExtractView(IJobService jobs, IMediaInspector inspector, SettingsController settings)
    {
        this.jobs = jobs;
        this.inspector = inspector;
        this.settings = settings;
        InitializeComponent();
        DataContext = this;
        jobs.JobChanged += Jobs_JobChanged;
        settings.Changed += Settings_Changed;
        ApplyDefaults(settings.Current.AudioExtraction);
    }

    public void AddDroppedFiles(IEnumerable<string> paths) => _ = AddDroppedFilesAndStartAsync(paths);

    private async Task AddDroppedFilesAndStartAsync(IEnumerable<string> paths)
    {
        var dropped = await AddFilesAsync(paths);
        if (dropped.Count > 0) await StartExtractionAsync(dropped);
    }

    private void Settings_Changed(object? sender, AppSettings value) => Dispatcher.Invoke(() => ApplyDefaults(value.AudioExtraction));

    private void ApplyDefaults(AudioExtractionDefaults defaults)
    {
        BitRateComboBox.SelectedIndex = defaults.Mp3KilobitsPerSecond switch { 128 => 0, 256 => 2, 320 => 3, _ => 1 };
        TrackModeComboBox.SelectedIndex = defaults.AudioTrackMode == AudioTrackMode.All ? 1 : 0;
        OutputFolderTextBox.Text = defaults.OutputFolderPath ?? string.Empty;
    }

    private void InputDropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void InputDropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddDroppedFiles(paths);
        e.Handled = true;
    }

    private void ChooseFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = "Choose media", Filter = "Media files|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.m4a;*.wav;*.flac;*.mp3|All files|*.*" };
        if (dialog.ShowDialog() == true) _ = AddFilesAsync(dialog.FileNames);
    }

    private async Task<IReadOnlyList<InputRow>> AddFilesAsync(IEnumerable<string> paths)
    {
        var selected = new List<InputRow>();
        foreach (var path in paths.Where(File.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var existing = Inputs.FirstOrDefault(input => input.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                if (existing.Inspection is not null) selected.Add(existing);
                continue;
            }
            var row = new InputRow(path, "Checking media");
            Inputs.Add(row);
            try
            {
                var inspection = await inspector.InspectAsync(MediaInput.FromPath(path), CancellationToken.None);
                var audio = inspection.Streams.Where(stream => stream.Kind == MediaStreamKind.Audio).ToArray();
                if (audio.Length == 0) throw new InvalidOperationException();
                row.Inspection = inspection;
                row.Status = $"Ready, {audio.Length} audio track{(audio.Length == 1 ? string.Empty : "s")}";
                selected.Add(row);
            }
            catch { row.Status = "No readable audio stream found."; }
        }
        return selected;
    }

    private void RemoveInput_Click(object sender, RoutedEventArgs e) { if (sender is Button { Tag: InputRow row }) Inputs.Remove(row); }
    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose output folder" };
        if (dialog.ShowDialog() == true) OutputFolderTextBox.Text = dialog.FolderName;
    }

    private async void Extract_Click(object sender, RoutedEventArgs e)
        => await StartExtractionAsync(Inputs.Where(input => input.Inspection is not null).ToArray());

    private async Task StartExtractionAsync(IReadOnlyCollection<InputRow> ready)
    {
        // Keep sources available for another bitrate, track, or output-folder choice.
        if (ready.Count == 0) { MessageTextBlock.Text = "Add at least one media file with audio."; return; }
        var kbps = BitRateComboBox.SelectedIndex switch { 0 => 128, 2 => 256, 3 => 320, _ => 192 };
        var mode = TrackModeComboBox.SelectedIndex == 1 ? AudioTrackMode.All : AudioTrackMode.Default;
        var folder = string.IsNullOrWhiteSpace(OutputFolderTextBox.Text) ? null : OutputFolderTextBox.Text.Trim();
        var wasBusy = jobs.Jobs.Any(candidate => !JobStateMachine.IsTerminal(candidate.State));
        foreach (var input in ready)
        {
            var job = MediaJob.Create(new AudioExtractionRequest(MediaInput.FromPath(input.FullPath), new(folder), BitRate.FromKilobitsPerSecond(kbps), mode, []));
            Jobs.Add(new JobRow(job));
            await jobs.EnqueueAsync(job);
        }
        CancelButton.IsEnabled = true;
        MessageTextBlock.Text = wasBusy
            ? $"Added {ready.Count} file{(ready.Count == 1 ? string.Empty : "s")} to the queue."
            : ready.Count == 1
                ? "Audio extraction started."
                : $"Added {ready.Count} files. MediaTool will extract them one at a time.";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { if (jobs.CancelActive()) MessageTextBlock.Text = "Cancelling the active job."; }
    private void Jobs_JobChanged(object? sender, MediaJob job) => Dispatcher.BeginInvoke(() =>
    {
        Jobs.FirstOrDefault(row => row.Job.Id == job.Id)?.Refresh();
        var active = jobs.Jobs.Any(candidate => !JobStateMachine.IsTerminal(candidate.State));
        CancelButton.IsEnabled = active;
        if (job.State == JobState.Completed && job.Result?.Outputs.FirstOrDefault() is { } output)
            MessageTextBlock.Text = $"Finished — saved {Path.GetFileName(output.Path)} ({output.Size.Megabytes:0.00} MB).";
        else if (job.State == JobState.Failed)
            MessageTextBlock.Text = job.Result?.Error?.Message ?? "Audio extraction could not finish.";
    });

    private async void OpenResult_Click(object sender, RoutedEventArgs e) => await OpenPathAsync(sender, false);
    private async void ShowResultFolder_Click(object sender, RoutedEventArgs e) => await OpenPathAsync(sender, true);
    private async Task OpenPathAsync(object sender, bool showFolder)
    {
        if (sender is not Button { Tag: string path }) return;
        if (!await ShellFileActions.OpenAsync(path, showFolder))
            MessageTextBlock.Text = "Windows could not open that output. The file is still saved.";
    }
    private void CopyResultPath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path } || !File.Exists(path)) return;
        Clipboard.SetText(path);
        MessageTextBlock.Text = "Output file path copied.";
    }
}
