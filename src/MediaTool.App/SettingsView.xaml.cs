using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MediaTool.Core;
using MediaTool.Infrastructure;

namespace MediaTool.App;

public partial class SettingsView : UserControl
{
    private readonly SettingsController settings;
    private readonly LocalDataPaths dataPaths;
    private readonly IJobHistoryStore history;
    private bool loadingControls;

    public SettingsView(SettingsController settings, LocalDataPaths dataPaths, IJobHistoryStore history)
    {
        this.settings = settings; this.dataPaths = dataPaths; this.history = history;
        InitializeComponent();
        TranscriptModelComboBox.ItemsSource = WhisperModelCatalog.Available;
        DataPathTextBlock.Text = dataPaths.Root;
        settings.Changed += Settings_Changed;
        LoadControls(settings.Current);
    }

    private void Settings_Changed(object? sender, AppSettings value) => Dispatcher.BeginInvoke(() => LoadControls(value));

    private void LoadControls(AppSettings value)
    {
        loadingControls = true;
        ThemeComboBox.SelectedIndex = (int)value.Global.Theme;
        VideoSizeTextBox.Text = value.Compression.MaximumMegabytes.ToString(CultureInfo.CurrentCulture);
        VideoCodecComboBox.SelectedIndex = value.Compression.Codec == VideoCodec.H265 ? 1 : 0;
        VideoResolutionComboBox.SelectedIndex = (int)value.Compression.MaximumResolution;
        VideoAudioComboBox.SelectedIndex = value.Compression.AudioTrackMode switch { AudioTrackMode.All => 1, AudioTrackMode.None => 2, _ => 0 };
        VideoOutputTextBox.Text = value.Compression.OutputFolderPath ?? string.Empty;
        VideoCompletionComboBox.SelectedIndex = (int)value.Compression.CompletionAction;
        AudioBitrateComboBox.SelectedIndex = value.AudioExtraction.Mp3KilobitsPerSecond switch { 128 => 0, 256 => 2, 320 => 3, _ => 1 };
        AudioTrackComboBox.SelectedIndex = value.AudioExtraction.AudioTrackMode == AudioTrackMode.All ? 1 : 0;
        AudioOutputTextBox.Text = value.AudioExtraction.OutputFolderPath ?? string.Empty;
        AudioCompletionComboBox.SelectedIndex = (int)value.AudioExtraction.CompletionAction;
        TranscriptModelComboBox.SelectedItem = WhisperModelCatalog.Available.FirstOrDefault(model => model.Id == value.Transcription.ModelId) ?? WhisperModelCatalog.BaseEnglish;
        TranscriptLanguageComboBox.SelectedIndex = value.Transcription.Language switch { "en" => 1, "es" => 2, "fr" => 3, "de" => 4, _ => 0 };
        TranscriptTextCheckBox.IsChecked = value.Transcription.Formats.Contains(TranscriptFormat.Text);
        TranscriptSrtCheckBox.IsChecked = value.Transcription.Formats.Contains(TranscriptFormat.Srt);
        TranscriptVttCheckBox.IsChecked = value.Transcription.Formats.Contains(TranscriptFormat.WebVtt);
        TranscriptTimestampsCheckBox.IsChecked = value.Transcription.IncludeTimestamps;
        TranscriptOutputTextBox.Text = value.Transcription.OutputFolderPath ?? string.Empty;
        TranscriptCompletionComboBox.SelectedIndex = (int)value.Transcription.CompletionAction;
        loadingControls = false;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(VideoSizeTextBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var size) || size is < 5 or > 1_000) { StatusTextBlock.Text = "Video size must be between 5 and 1,000 MB."; return; }
        var formats = new List<TranscriptFormat>();
        if (TranscriptTextCheckBox.IsChecked == true) formats.Add(TranscriptFormat.Text);
        if (TranscriptSrtCheckBox.IsChecked == true) formats.Add(TranscriptFormat.Srt);
        if (TranscriptVttCheckBox.IsChecked == true) formats.Add(TranscriptFormat.WebVtt);
        if (formats.Count == 0) { StatusTextBlock.Text = "Choose at least one transcript format."; return; }
        var model = (WhisperModel)TranscriptModelComboBox.SelectedItem;
        var language = (TranscriptLanguageComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "auto";
        await settings.UpdateAsync(_ => new AppSettings(
            new((AppTheme)ThemeComboBox.SelectedIndex, false, settings.Current.Global.WindowPlacement),
            new(size, VideoCodecComboBox.SelectedIndex == 1 ? VideoCodec.H265 : VideoCodec.H264, (MaximumResolution)VideoResolutionComboBox.SelectedIndex, VideoAudioComboBox.SelectedIndex switch { 1 => AudioTrackMode.All, 2 => AudioTrackMode.None, _ => AudioTrackMode.Default }, BlankToNull(VideoOutputTextBox.Text), (CompletionAction)VideoCompletionComboBox.SelectedIndex),
            new(AudioBitrateComboBox.SelectedIndex switch { 0 => 128, 2 => 256, 3 => 320, _ => 192 }, AudioTrackComboBox.SelectedIndex == 1 ? AudioTrackMode.All : AudioTrackMode.Default, BlankToNull(AudioOutputTextBox.Text), (CompletionAction)AudioCompletionComboBox.SelectedIndex),
            new(model.Id, formats, language, BlankToNull(TranscriptOutputTextBox.Text), TranscriptTimestampsCheckBox.IsChecked == true, (CompletionAction)TranscriptCompletionComboBox.SelectedIndex)));
        StatusTextBlock.Text = "Defaults saved.";
    }

    private void BrowseVideoOutput_Click(object sender, RoutedEventArgs e) => ChooseOutputFolder(VideoOutputTextBox, "Choose the default video output folder");
    private void BrowseAudioOutput_Click(object sender, RoutedEventArgs e) => ChooseOutputFolder(AudioOutputTextBox, "Choose the default audio output folder");
    private void BrowseTranscriptOutput_Click(object sender, RoutedEventArgs e) => ChooseOutputFolder(TranscriptOutputTextBox, "Choose the default transcript output folder");

    private async void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loadingControls || ThemeComboBox.SelectedIndex < 0) return;
        var theme = (AppTheme)ThemeComboBox.SelectedIndex;
        if (settings.Current.Global.Theme == theme) return;
        await settings.UpdateAsync(current => current with { Global = current.Global with { Theme = theme } });
        StatusTextBlock.Text = "Appearance changed and saved.";
    }

    private static void ChooseOutputFolder(TextBox target, string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (Directory.Exists(target.Text)) dialog.InitialDirectory = target.Text;
        if (dialog.ShowDialog() == true) target.Text = dialog.FolderName;
        target.Focus();
    }

    private async void Reset_Click(object sender, RoutedEventArgs e) { await settings.ResetAsync(); StatusTextBlock.Text = "Defaults reset."; }
    private async void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        dataPaths.EnsureDirectories();
        if (!await ShellFileActions.OpenFolderAsync(dataPaths.Root))
            MessageBox.Show("Windows could not open the data folder.", "MediaTool", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private async void ClearHistory_Click(object sender, RoutedEventArgs e) { if (MessageBox.Show("Clear the saved job history? Output files will not be deleted.", "MediaTool", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) await history.ClearAsync(); }
    private static string? BlankToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
