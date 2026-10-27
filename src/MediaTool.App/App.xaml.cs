using System.Windows;
using System.IO;
using MediaTool.Core;
using MediaTool.Infrastructure;

namespace MediaTool.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var paths = new LocalDataPaths();
        paths.EnsureDirectories();
        _ = new TemporaryFileCleaner(paths).DeleteAbandoned(DateTimeOffset.UtcNow);
        var applicationFiles = ResolveApplicationFilesDirectory();
        try
        {
            new SidecarIntegrityVerifier().VerifyFfmpegAsync(Path.Combine(applicationFiles, "sidecars")).GetAwaiter().GetResult();
        }
        catch (SidecarIntegrityException exception)
        {
            MessageBox.Show(exception.Message, "MediaTool", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }
        var sidecars = FfmpegPaths.FromApplicationDirectory(applicationFiles);
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
