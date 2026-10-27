using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MediaTool.Core;
using MediaTool.Infrastructure;

namespace MediaTool.App;

public partial class TranscribeView : UserControl
{
    private readonly IJobService jobs;
    private readonly WhisperModelManager models;
    private readonly SettingsController settings;
    private string? inputPath;
    private CancellationTokenSource? modelDownloadCancellation;
    private readonly HashSet<JobId> shownResults = [];
    public ObservableCollection<JobRow> Jobs { get; } = [];

    public TranscribeView(IJobService jobs, WhisperModelManager models, SettingsController settings)
    {
        this.jobs = jobs;
        this.models = models;
        this.settings = settings;
        InitializeComponent();
        DataContext = this;
        ModelComboBox.ItemsSource = WhisperModelCatalog.Available;
        ModelComboBox.SelectedItem = WhisperModelCatalog.BaseEnglish;
        jobs.JobChanged += Jobs_JobChanged;
        settings.Changed += Settings_Changed;
        UpdateModelStatus();
    }

    private void Settings_Changed(object? sender, AppSettings value) => Dispatcher.Invoke(() => ApplyDefaults(value.Transcription));

    private void ApplyDefaults(TranscriptionDefaults defaults)
    {
        ModelComboBox.SelectedItem = WhisperModelCatalog.Available.FirstOrDefault(model => model.Id == defaults.ModelId) ?? WhisperModelCatalog.BaseEnglish;
        LanguageComboBox.SelectedIndex = defaults.Language switch { "en" => 1, "es" => 2, "fr" => 3, "de" => 4, _ => 0 };
        TextCheckBox.IsChecked = defaults.Formats.Contains(TranscriptFormat.Text);
        SrtCheckBox.IsChecked = defaults.Formats.Contains(TranscriptFormat.Srt);
        VttCheckBox.IsChecked = defaults.Formats.Contains(TranscriptFormat.WebVtt);
        IncludeTimestampsCheckBox.IsChecked = defaults.IncludeTimestamps;
        OutputFolderTextBox.Text = defaults.OutputFolderPath ?? string.Empty;
    }

    private void UpdateModelStatus()
    {
        var model = SelectedModel;
        var installed = File.Exists(models.GetModelPath(model));
        ModelStatusTextBlock.Text = installed
            ? $"{model.DisplayName} is installed. Everything else in this tool runs with the network off."
            : $"{model.DisplayName} is not installed ({model.DownloadSize.Megabytes:0} MB). Model downloads are the only network activity in MediaTool.";
        DownloadModelButton.Content = installed ? "Installed" : $"Download {model.DownloadSize.Megabytes:0} MB model";
        DownloadModelButton.IsEnabled = !installed;
        LanguageComboBox.IsEnabled = !model.EnglishOnly;
    }

    private WhisperModel SelectedModel => (WhisperModel)ModelComboBox.SelectedItem;

    private void ModelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (modelDownloadCancellation is null && ModelComboBox.SelectedItem is not null) UpdateModelStatus();
    }

    private async void DownloadModel_Click(object sender, RoutedEventArgs e)
    {
        if (modelDownloadCancellation is not null)
        {
            modelDownloadCancellation.Cancel();
            ModelStatusTextBlock.Text = "Canceling model download…";
            return;
        }

        var model = SelectedModel;
        modelDownloadCancellation = new();
        SetModelDownloadState(true);
        var progress = new Progress<ModelDownloadProgress>(value =>
        {
            if (value.IsVerifying)
            {
                ModelDownloadProgressBar.IsIndeterminate = true;
                ModelStatusTextBlock.Text = $"Download complete. Verifying {model.DisplayName}…";
            }
            else if (value.Percentage is double percentage)
            {
                ModelDownloadProgressBar.IsIndeterminate = false;
                ModelDownloadProgressBar.Value = percentage;
                var receivedMb = value.BytesReceived / 1_000_000d;
                var totalMb = value.TotalBytes!.Value / 1_000_000d;
                ModelStatusTextBlock.Text = $"Downloading {model.DisplayName} — {percentage:0}% ({receivedMb:0} of {totalMb:0} MB)";
            }
            else ModelStatusTextBlock.Text = $"Downloading {model.DisplayName} — {value.BytesReceived / 1_000_000d:0} MB received";
        });
        try
        {
            ModelStatusTextBlock.Text = $"Starting {model.DisplayName} download…";
            await models.DownloadAsync(model, WhisperModelCatalog.GetDownloadUri(model), modelDownloadCancellation.Token, progress);
        }
        catch (OperationCanceledException) { ModelStatusTextBlock.Text = $"{model.DisplayName} download canceled. You can retry whenever you’re ready."; }
        catch (Exception exception) { ModelStatusTextBlock.Text = $"Model download failed: {exception.Message}"; }
        finally
        {
            modelDownloadCancellation.Dispose();
            modelDownloadCancellation = null;
            SetModelDownloadState(false);
            if (File.Exists(models.GetModelPath(model))) UpdateModelStatus();
        }
    }

    private void SetModelDownloadState(bool downloading)
    {
        ModelComboBox.IsEnabled = !downloading;
        ImportModelButton.IsEnabled = !downloading;
        DownloadModelButton.IsEnabled = true;
        DownloadModelButton.Content = downloading ? "Cancel download" : $"Download {SelectedModel.DownloadSize.Megabytes:0} MB model";
        ModelDownloadProgressBar.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
        ModelDownloadProgressBar.IsIndeterminate = downloading;
        if (!downloading) ModelDownloadProgressBar.Value = 0;
    }

    private async void ImportModel_Click(object sender, RoutedEventArgs e)
    {
        var model = SelectedModel;
        var dialog = new OpenFileDialog { Title = $"Import {model.DisplayName} model", Filter = "Whisper model|*.bin" };
        if (dialog.ShowDialog() != true) return;
        try { await models.ImportAsync(model, dialog.FileName, CancellationToken.None); UpdateModelStatus(); }
        catch (Exception exception) { ModelStatusTextBlock.Text = $"Model import failed: {exception.Message}"; }
    }

    private void ChooseInput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose audio or video", Filter = "Media files|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.m4a;*.wav;*.mp3;*.flac|All files|*.*" };
        if (dialog.ShowDialog() == true) { inputPath = dialog.FileName; InputTextBlock.Text = Path.GetFileName(inputPath); }
    }

    public void AddDroppedFiles(IEnumerable<string> paths) => _ = AddDroppedFileAndStartAsync(paths);

    private async Task AddDroppedFileAndStartAsync(IEnumerable<string> paths)
    {
        var path = paths.FirstOrDefault(File.Exists);
        if (path is null) return;
        inputPath = Path.GetFullPath(path);
        InputTextBlock.Text = Path.GetFileName(inputPath);
        await StartTranscriptionAsync();
    }

    private void InputDropZone_DragOver(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void InputDropZone_Drop(object sender, DragEventArgs e) { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddDroppedFiles(paths); e.Handled = true; }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose output folder" };
        if (dialog.ShowDialog() == true) OutputFolderTextBox.Text = dialog.FolderName;
    }

    private void MeetingNotesPreset_Click(object sender, RoutedEventArgs e)
    {
        TextCheckBox.IsChecked = true; SrtCheckBox.IsChecked = false; VttCheckBox.IsChecked = false; IncludeTimestampsCheckBox.IsChecked = true;
        MessageTextBlock.Text = "Meeting notes creates readable timestamped text for review or AI.";
    }

    private void CaptionsPreset_Click(object sender, RoutedEventArgs e)
    {
        TextCheckBox.IsChecked = false; SrtCheckBox.IsChecked = true; VttCheckBox.IsChecked = false; IncludeTimestampsCheckBox.IsChecked = false;
        MessageTextBlock.Text = "Video captions creates a timed SRT subtitle file.";
    }

    private async void Transcribe_Click(object sender, RoutedEventArgs e)
        => await StartTranscriptionAsync();

    private async Task StartTranscriptionAsync()
    {
        if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath)) { MessageTextBlock.Text = "Choose an audio or video file."; return; }
        var model = SelectedModel;
        if (!File.Exists(models.GetModelPath(model))) { MessageTextBlock.Text = $"Download or import {model.DisplayName} first."; return; }
        var formats = new HashSet<TranscriptFormat>();
        if (TextCheckBox.IsChecked == true) formats.Add(TranscriptFormat.Text);
        if (SrtCheckBox.IsChecked == true) formats.Add(TranscriptFormat.Srt);
        if (VttCheckBox.IsChecked == true) formats.Add(TranscriptFormat.WebVtt);
        if (formats.Count == 0) { MessageTextBlock.Text = "Choose at least one output format."; return; }
        var language = model.EnglishOnly ? "en" : (LanguageComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "auto";
        var folder = string.IsNullOrWhiteSpace(OutputFolderTextBox.Text) ? null : OutputFolderTextBox.Text.Trim();
        var wasBusy = jobs.Jobs.Any(candidate => !JobStateMachine.IsTerminal(candidate.State));
        var job = MediaJob.Create(new TranscriptionRequest(MediaInput.FromPath(inputPath), new(folder), model, language, formats, IncludeTimestampsCheckBox.IsChecked == true));
        Jobs.Add(new JobRow(job));
        await jobs.EnqueueAsync(job);
        CancelButton.IsEnabled = true;
        MessageTextBlock.Text = wasBusy ? "Transcription added to the queue." : "Transcription started.";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) { jobs.CancelActive(); }
    private void Jobs_JobChanged(object? sender, MediaJob job)
    {
        Dispatcher.BeginInvoke(() =>
        {
            Jobs.FirstOrDefault(row => row.Job.Id == job.Id)?.Refresh();
            var active = jobs.Jobs.Any(candidate => !JobStateMachine.IsTerminal(candidate.State));
            CancelButton.IsEnabled = active;
        });
        if (job.State == JobState.Completed && shownResults.Add(job.Id)) _ = ShowTranscriptResultAsync(job);
    }

    private async Task ShowTranscriptResultAsync(MediaJob job)
    {
        var outputs = job.Result?.Outputs ?? [];
        var previewPath = outputs.FirstOrDefault(output => Path.GetExtension(output.Path).Equals(".txt", StringComparison.OrdinalIgnoreCase))?.Path ?? outputs.FirstOrDefault()?.Path;
        var preview = previewPath is not null && File.Exists(previewPath) ? await File.ReadAllTextAsync(previewPath) : "No preview is available for this result.";
        await Dispatcher.InvokeAsync(() =>
        {
            TranscriptFilesTextBlock.Text = outputs.Count == 0 ? "No output files were created." : "Saved: " + string.Join(", ", outputs.Select(output => output.Path));
            TranscriptPreviewTextBox.Text = preview;
            TranscriptResultPanel.Visibility = Visibility.Visible;
            MessageTextBlock.Text = outputs.Count == 1 ? "Transcription saved as 1 file." : $"Transcription saved as {outputs.Count} files.";
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
