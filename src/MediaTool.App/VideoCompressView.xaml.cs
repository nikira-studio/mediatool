using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MediaTool.Core;
using MediaTool.Infrastructure;

namespace MediaTool.App;

public partial class VideoCompressView : UserControl
{
    private readonly IJobService jobs;
    private readonly IMediaInspector inspector;
    private readonly FfmpegCapabilityDetector capabilities;
    private readonly SettingsController settings;
    private bool initialized;

    public ObservableCollection<InputRow> Inputs { get; } = [];
    public ObservableCollection<JobRow> Jobs { get; } = [];

    public VideoCompressView(IJobService jobs, IMediaInspector inspector, FfmpegCapabilityDetector capabilities, SettingsController settings)
    {
        this.jobs = jobs;
        this.inspector = inspector;
        this.capabilities = capabilities;
        this.settings = settings;
        InitializeComponent();
        DataContext = this;
        jobs.JobChanged += Jobs_JobChanged;
        settings.Changed += Settings_Changed;
    }

    public void AddDroppedFiles(IEnumerable<string> paths) => _ = AddDroppedFilesAndStartAsync(paths);

    private async Task AddDroppedFilesAndStartAsync(IEnumerable<string> paths)
    {
        var dropped = await AddFilesAsync(paths);
        if (dropped.Count > 0) await StartCompressionAsync(dropped);
    }

    private void Settings_Changed(object? sender, AppSettings value) => Dispatcher.Invoke(() => ApplyDefaults(value.Compression));

    private void ApplyDefaults(CompressionDefaults defaults)
    {
        MaximumSizeTextBox.Text = defaults.MaximumMegabytes.ToString(CultureInfo.CurrentCulture);
        CodecComboBox.SelectedIndex = defaults.Codec == VideoCodec.H265 && CodecComboBox.Items.Count > 1 ? 1 : 0;
        ResolutionComboBox.SelectedIndex = (int)defaults.MaximumResolution;
        AudioComboBox.SelectedIndex = defaults.AudioTrackMode switch { AudioTrackMode.All => 1, AudioTrackMode.None => 2, _ => 0 };
        OutputFolderTextBox.Text = defaults.OutputFolderPath ?? string.Empty;
    }

    private async void View_Loaded(object sender, RoutedEventArgs e)
    {
        if (initialized) return;
        initialized = true;
        try
        {
            var available = await capabilities.DetectAsync(CancellationToken.None);
            if (available.Any(capability => capability.Codec == VideoCodec.H265 && capability.IsAvailable)) CodecComboBox.Items.Add(new ComboBoxItem { Content = "H.265" });
            ApplyDefaults(settings.Current.Compression);
        }
        catch
        {
            MessageTextBlock.Text = "Codec check failed. H.264 will be used if FFmpeg is available.";
        }
    }

    private void ChooseFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = "Choose videos", Filter = "Media files|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.m4v|All files|*.*" };
        if (dialog.ShowDialog() == true) _ = AddFilesAsync(dialog.FileNames);
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
                if (!inspection.Streams.Any(stream => stream.Kind == MediaStreamKind.Video && !stream.IsAttachedPicture)) throw new InvalidOperationException("No video stream was found.");
                row.Inspection = inspection;
                row.Status = $"Ready, {inspection.Duration.Value:mm\\:ss}";
                selected.Add(row);
            }
            catch (Exception exception)
            {
                row.Status = exception is MediaInspectionException media ? media.Message : "This file cannot be read as video.";
            }
        }
        MessageTextBlock.Text = Inputs.Any(input => input.Inspection is not null) ? string.Empty : "Choose one or more readable videos.";
        return selected;
    }

    private void RemoveInput_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: InputRow row }) Inputs.Remove(row);
    }

    private void SizePreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value }) MaximumSizeTextBox.Text = value;
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose output folder" };
        if (dialog.ShowDialog() == true) OutputFolderTextBox.Text = dialog.FolderName;
        OutputFolderTextBox.Focus();
    }

    private async void Compress_Click(object sender, RoutedEventArgs e)
        => await StartCompressionAsync(Inputs.Where(input => input.Inspection is not null).ToArray());

    private async Task StartCompressionAsync(IReadOnlyCollection<InputRow> ready)
    {
        if (!decimal.TryParse(MaximumSizeTextBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var megabytes) || megabytes < 5 || megabytes > 1_000)
        {
            MessageTextBlock.Text = "Enter a maximum size from 5 MB to 1,000 MB.";
            MaximumSizeTextBox.Focus();
            return;
        }
        // The same source may be intentionally compressed more than once with different settings.
        // The output service assigns a fresh name for every completed version.
        if (ready.Count == 0) { MessageTextBlock.Text = "Add at least one ready video."; return; }
        var outputFolder = string.IsNullOrWhiteSpace(OutputFolderTextBox.Text) ? null : OutputFolderTextBox.Text.Trim();
        var codec = CodecComboBox.SelectedIndex == 1 ? VideoCodec.H265 : VideoCodec.H264;
        var resolution = (MaximumResolution)ResolutionComboBox.SelectedIndex;
        var audio = AudioComboBox.SelectedIndex switch { 1 => AudioTrackMode.All, 2 => AudioTrackMode.None, _ => AudioTrackMode.Default };
        var wasBusy = jobs.Jobs.Any(candidate => !JobStateMachine.IsTerminal(candidate.State));

        foreach (var input in ready)
        {
            var request = new CompressionRequest(MediaInput.FromPath(input.FullPath), new(outputFolder), FileSize.FromMegabytes(megabytes), codec, resolution, audio, []);
            var job = MediaJob.Create(request);
            Jobs.Add(new JobRow(job));
            await jobs.EnqueueAsync(job);
        }
        CancelButton.IsEnabled = true;
        MessageTextBlock.Text = wasBusy
            ? $"Added {ready.Count} video{(ready.Count == 1 ? string.Empty : "s")} to the queue."
            : ready.Count == 1
                ? "Compression started. MediaTool will create the smaller video in one pass."
                : $"Added {ready.Count} videos. MediaTool will process them one at a time.";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (jobs.CancelActive()) MessageTextBlock.Text = "Cancelling the active job.";
        CancelButton.Focus();
    }

    private void Jobs_JobChanged(object? sender, MediaJob job)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var row = Jobs.FirstOrDefault(candidate => candidate.Job.Id == job.Id);
            row?.Refresh();
            var active = jobs.Jobs.Any(candidate => !JobStateMachine.IsTerminal(candidate.State));
            CancelButton.IsEnabled = active;
            if (job.State == JobState.Completed && job.Result?.Outputs.FirstOrDefault() is { } output)
                MessageTextBlock.Text = $"Finished — saved {Path.GetFileName(output.Path)} ({output.Size.Megabytes:0.00} MB).";
            else if (job.State == JobState.Skipped)
                MessageTextBlock.Text = $"{job.Request.Input.FileName} is already smaller than the selected limit, so no new copy was needed.";
            else if (job.State == JobState.Failed)
                MessageTextBlock.Text = job.Result?.Error?.Message ?? "Compression could not finish.";
        });
    }

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

public sealed class InputRow(string fullPath, string status) : NotifyObject
{
    public string FullPath { get; } = fullPath;
    public string FileName => Path.GetFileName(FullPath);
    public MediaInspection? Inspection { get; set; }
    public bool Submitted { get; set; }
    private string status = status;
    public string Status { get => status; set => SetField(ref status, value); }
}

public sealed class JobRow : NotifyObject
{
    public JobRow(MediaJob job)
    {
        Job = job;
        Refresh();
    }
    public MediaJob Job { get; }
    public string FileName => Job.Request.Input.FileName;
    private string state = string.Empty;
    private string progress = string.Empty;
    private string detail = string.Empty;
    private string? resultPath;
    public string State { get => state; private set => SetField(ref state, value); }
    public string Progress { get => progress; private set => SetField(ref progress, value); }
    public string Detail { get => detail; private set => SetField(ref detail, value); }
    public string? ResultPath { get => resultPath; private set => SetField(ref resultPath, value); }

    public void Refresh()
    {
        State = Job.State switch
        {
            JobState.Queued => "Waiting",
            JobState.Inspecting => "Checking",
            JobState.Ready => "Ready",
            JobState.Processing => "Working",
            JobState.Retrying => "Adjusting size",
            JobState.Cancelling => "Cancelling",
            JobState.Cancelled => "Cancelled",
            JobState.Completed => "Finished",
            JobState.Skipped => "Not needed",
            JobState.Interrupted => "Interrupted",
            JobState.Failed => "Could not finish",
            _ => Job.State.ToString()
        };
        Progress = Job.State == JobState.Completed
            ? "100%"
            : Job.Progress?.Percent is { } percent ? $"{percent.Value:0}%" : string.Empty;
        ResultPath = Job.Result?.Outputs.FirstOrDefault()?.Path;
        Detail = Job.Result?.Error?.Message ?? (ResultPath is null ? Job.Progress?.Phase ?? string.Empty : $"{Job.Result!.Outputs[0].Size.Megabytes:0.00} MB");
    }
}

public abstract class NotifyObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
