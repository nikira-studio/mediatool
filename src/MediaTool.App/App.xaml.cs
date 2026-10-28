using System.Windows;
using System.IO;
using System.Windows.Controls;
using MediaTool.Core;
using MediaTool.Infrastructure;

namespace MediaTool.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var paths = new LocalDataPaths();
        paths.EnsureDirectories();
        _ = new TemporaryFileCleaner(paths).DeleteAbandoned(DateTimeOffset.UtcNow);
        var applicationFiles = ResolveApplicationFilesDirectory();
        var status = new TextBlock { Text = "Checking FFmpeg…", Margin = new Thickness(20, 20, 20, 10), TextWrapping = TextWrapping.Wrap };
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 8, Margin = new Thickness(20, 0, 20, 20), IsIndeterminate = true };
        var panel = new StackPanel();
        panel.Children.Add(status); panel.Children.Add(bar);
        var setup = new Window { Title = "MediaTool setup", Width = 480, Height = 150, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = panel };
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        setup.Show();
        string ffmpegDirectory;
        try
        {
            status.Text = "Preparing FFmpeg. On first use, MediaTool downloads it directly from its publisher (about 111 MB).";
            var progress = new Progress<double>(value => { bar.IsIndeterminate = false; bar.Value = value; status.Text = $"Downloading FFmpeg from its publisher: {value:0}%"; });
            ffmpegDirectory = await new FfmpegInstaller().EnsureInstalledAsync(Path.Combine(applicationFiles, "sidecars", "ffmpeg.json"), paths.Root, progress, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or System.Net.Http.HttpRequestException or UnauthorizedAccessException or InvalidDataException or TaskCanceledException)
        {
            MessageBox.Show(setup, $"MediaTool could not prepare FFmpeg: {exception.Message}\n\nCheck your connection and open MediaTool again to retry.", "MediaTool", MessageBoxButton.OK, MessageBoxImage.Error);
            setup.Close();
            Shutdown(-1);
            return;
        }
        var sidecars = new FfmpegPaths(Path.Combine(ffmpegDirectory, "ffmpeg.exe"), Path.Combine(ffmpegDirectory, "ffprobe.exe"));
        var runner = new ProcessRunner();
        var inspector = new FfprobeMediaInspector(sidecars.Ffprobe, runner);
        var capabilityDetector = new FfmpegCapabilityDetector(sidecars, runner);
        var compressionHandler = new CompressionJobHandler(sidecars, runner, inspector, new OutputFileService(), capabilityDetector);
        var audioHandler = new AudioExtractionJobHandler(sidecars, runner, inspector, new OutputFileService());
        var whisper = WhisperPaths.FromApplicationDirectory(applicationFiles);
        var transcriptionHandler = new TranscriptionJobHandler(sidecars, whisper, paths, runner, inspector, new OutputFileService());
        var jobService = new SequentialJobService([compressionHandler, audioHandler, transcriptionHandler]);
        var window = new MainWindow(new JsonSettingsStore(paths), paths, e.Args, jobService, inspector, capabilityDetector, new WhisperModelManager(paths));
        MainWindow = window;
        window.Show();
        setup.Close();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
    }

    private static string ResolveApplicationFilesDirectory()
    {
        var nativeSearchDirectories = AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string;
        if (!string.IsNullOrWhiteSpace(nativeSearchDirectories))
        {
            foreach (var directory in nativeSearchDirectories.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Directory.Exists(Path.Combine(directory, "sidecars"))) return directory;
            }
        }
        return AppContext.BaseDirectory;
    }
}
