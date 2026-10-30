using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MediaTool.Core;
using MediaTool.Infrastructure;

namespace MediaTool.App;

public partial class MainWindow : Window
{
    private readonly ISettingsStore settingsStore;
    private readonly LocalDataPaths dataPaths;
    private readonly IReadOnlyList<string> launchPaths;
    private readonly IJobService jobService;
    private readonly IJobHistoryStore historyStore;
    private readonly SettingsController settingsController;
    private readonly VideoCompressView videoCompressView;
    private readonly AudioExtractView audioExtractView;
    private readonly TranscribeView transcribeView;
    private readonly SettingsView settingsView;
    private readonly HashSet<JobId> handledCompletionActions = [];
    private AppSettings settings = AppSettings.Default;
    internal bool HasActiveJob { get; set; }

    public MainWindow(ISettingsStore settingsStore, LocalDataPaths dataPaths, IEnumerable<string> launchArguments, IJobService jobService, IMediaInspector inspector, FfmpegCapabilityDetector capabilities, WhisperModelManager models)
    {
        this.settingsStore = settingsStore;
        this.dataPaths = dataPaths;
        this.jobService = jobService;
        historyStore = new JsonJobHistoryStore(dataPaths);
        settingsController = new SettingsController(settingsStore);
        launchPaths = launchArguments.Where(Path.IsPathFullyQualified).Select(Path.GetFullPath).ToArray();
        InitializeComponent();
        videoCompressView = new VideoCompressView(jobService, inspector, capabilities, settingsController);
        audioExtractView = new AudioExtractView(jobService, inspector, settingsController);
        transcribeView = new TranscribeView(jobService, models, settingsController);
        settingsView = new SettingsView(settingsController, dataPaths, historyStore);
        jobService.JobChanged += JobService_JobChanged;
        settingsController.Changed += SettingsController_Changed;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await settingsController.LoadAsync();
        settings = settingsController.Current;
        ThemeService.Apply(settings.Global.Theme, this);
        RestoreWindow(settings.Global.WindowPlacement);
        ShowTool(ToolId.VideoCompress);
        if (launchPaths.Count > 0) videoCompressView.AddDroppedFiles(launchPaths);
    }

    private void CompressNavigationButton_Click(object sender, RoutedEventArgs e) => ShowTool(ToolId.VideoCompress);
    private void ExtractNavigationButton_Click(object sender, RoutedEventArgs e) => ShowTool(ToolId.AudioExtract);
    private void TranscribeNavigationButton_Click(object sender, RoutedEventArgs e) => ShowTool(ToolId.Transcribe);
    private void SettingsNavigationButton_Click(object sender, RoutedEventArgs e) => ShowSettings();

    private void ShowTool(ToolId tool)
    {
        if (tool == ToolId.VideoCompress)
        {
            WorkPanel.Content = videoCompressView;
            videoCompressView.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            return;
        }
        if (tool == ToolId.AudioExtract)
        {
            WorkPanel.Content = audioExtractView;
            audioExtractView.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            return;
        }
        if (tool == ToolId.Transcribe)
        {
            WorkPanel.Content = transcribeView;
            transcribeView.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            return;
        }
        throw new ArgumentOutOfRangeException(nameof(tool));
    }

    private void ShowSettings()
    {
        WorkPanel.Content = settingsView;
        settingsView.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private async void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AppTheme theme }) return;
        settings = settings with { Global = settings.Global with { Theme = theme } };
        ThemeService.Apply(theme, this);
        await settingsStore.SaveAsync(settings);
        ((Button)sender).Focus();
    }

    private async void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        settings = await settingsStore.ResetAsync();
        ThemeService.Apply(settings.Global.Theme, this);
        ((Button)sender).Focus();
    }

    private void SettingsController_Changed(object? sender, AppSettings value)
    {
        settings = value;
        ThemeService.Apply(value.Global.Theme, this);
    }

    private async void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        dataPaths.EnsureDirectories();
        if (!await ShellFileActions.OpenFolderAsync(dataPaths.Root))
            MessageBox.Show(this, "Windows could not open the data folder.", "MediaTool", MessageBoxButton.OK, MessageBoxImage.Information);
        ((Button)sender).Focus();
    }

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Clear the saved job history? Output files will not be deleted.", "MediaTool", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await historyStore.ClearAsync();
        ((Button)sender).Focus();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (WorkPanel.Content is VideoCompressView video) video.AddDroppedFiles(paths);
        if (WorkPanel.Content is AudioExtractView audio) audio.AddDroppedFiles(paths);
        if (WorkPanel.Content is TranscribeView transcribe) transcribe.AddDroppedFiles(paths);
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        HasActiveJob = jobService.Jobs.Any(job => !JobStateMachine.IsTerminal(job.State));
        if (HasActiveJob)
        {
            var choice = MessageBox.Show(this, "A job is still running. Cancel it and exit?", "MediaTool", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (choice != MessageBoxResult.Yes) { e.Cancel = true; return; }
        }
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        settings = settings with { Global = settings.Global with { WindowPlacement = new(bounds.Left, bounds.Top, bounds.Width, bounds.Height, WindowState == WindowState.Maximized) } };
        await settingsStore.SaveAsync(settings);
        await jobService.DisposeAsync();
    }

    private void RestoreWindow(WindowPlacement? placement)
    {
        if (placement is null || placement.Width < MinWidth || placement.Height < MinHeight) return;
        var saved = new Rect(placement.Left, placement.Top, placement.Width, placement.Height);
        var virtualScreen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!saved.IntersectsWith(virtualScreen)) return;
        Left = placement.Left; Top = placement.Top; Width = placement.Width; Height = placement.Height;
        if (placement.IsMaximized) WindowState = WindowState.Maximized;
    }

    private void JobService_JobChanged(object? sender, MediaJob job)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var unfinished = jobService.Jobs.Where(candidate => !JobStateMachine.IsTerminal(candidate.State)).ToArray();
            var active = unfinished.FirstOrDefault(candidate => candidate.State != JobState.Queued) ?? unfinished.FirstOrDefault();
            HasActiveJob = active is not null;
            ActivityIndicator.Visibility = HasActiveJob ? Visibility.Visible : Visibility.Collapsed;
            CompressActivityDot.Visibility = unfinished.Any(candidate => candidate.Request.Tool == ToolId.VideoCompress) ? Visibility.Visible : Visibility.Collapsed;
            ExtractActivityDot.Visibility = unfinished.Any(candidate => candidate.Request.Tool == ToolId.AudioExtract) ? Visibility.Visible : Visibility.Collapsed;
            TranscribeActivityDot.Visibility = unfinished.Any(candidate => candidate.Request.Tool == ToolId.Transcribe) ? Visibility.Visible : Visibility.Collapsed;
            if (active is null) return;
            ActivityToolTextBlock.Text = active.Request.Tool switch { ToolId.VideoCompress => "Compressing", ToolId.AudioExtract => "Extracting audio", ToolId.Transcribe => "Transcribing", _ => "Working" };
            var waiting = unfinished.Count(candidate => candidate.State == JobState.Queued);
            var phase = active.Progress?.Phase ?? (active.State == JobState.Queued ? "Waiting" : active.State.ToString());
            if (active.Request.Tool == ToolId.Transcribe && active.Progress?.Percent is { } percent && percent.Value < 100 && phase.StartsWith("Transcribing locally", StringComparison.Ordinal))
                phase = $"{phase} · {percent.Value:0}%";
            ActivityPhaseTextBlock.Text = waiting == 0 ? phase : $"{phase} · {waiting} waiting";
        });
        if (job.State == JobState.Completed && !jobService.Jobs.Any(candidate => !JobStateMachine.IsTerminal(candidate.State)) && handledCompletionActions.Add(job.Id))
            _ = RunCompletionActionAsync(job);
    }

    private async Task RunCompletionActionAsync(MediaJob job)
    {
        var action = job.Request.Tool switch
        {
            ToolId.VideoCompress => settings.Compression.CompletionAction,
            ToolId.AudioExtract => settings.AudioExtraction.CompletionAction,
            ToolId.Transcribe => settings.Transcription.CompletionAction,
            _ => CompletionAction.StayInApp
        };
        var path = job.Result?.Outputs.FirstOrDefault()?.Path;
        if (action == CompletionAction.StayInApp || path is null || !File.Exists(path)) return;
        if (!await ShellFileActions.OpenAsync(path, action == CompletionAction.ShowInFolder))
            await Dispatcher.BeginInvoke(() => MessageBox.Show(this, "The result was saved, but Windows could not open it automatically.", "MediaTool", MessageBoxButton.OK, MessageBoxImage.Information));
    }
}
