using System.Text.Json;
using System.Text.Json.Serialization;
using MediaTool.Core;

namespace MediaTool.Infrastructure;

public sealed class LocalDataPaths
{
    public string Root { get; }
    public string SettingsPath => Path.Combine(Root, "settings.json");
    public string HistoryPath => Path.Combine(Root, "history.json");
    public string TemporaryDirectory => Path.Combine(Root, "temp");
    public string ModelsDirectory => Path.Combine(Root, "models");

    public LocalDataPaths(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediaTool");
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(TemporaryDirectory);
        Directory.CreateDirectory(ModelsDirectory);
    }
}

public sealed class JsonSettingsStore(LocalDataPaths paths) : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureDirectories();
        if (!File.Exists(paths.SettingsPath)) return AppSettings.Default;
        try
        {
            await using var stream = File.OpenRead(paths.SettingsPath);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, Options, cancellationToken).ConfigureAwait(false) ?? AppSettings.Default;
        }
        catch (JsonException) { return AppSettings.Default; }
        catch (IOException) { return AppSettings.Default; }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        paths.EnsureDirectories();
        var temporary = paths.SettingsPath + ".new";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            await JsonSerializer.SerializeAsync(stream, settings, Options, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, paths.SettingsPath, true);
    }

    public async Task<AppSettings> ResetAsync(CancellationToken cancellationToken = default)
    {
        await SaveAsync(AppSettings.Default, cancellationToken).ConfigureAwait(false);
        return AppSettings.Default;
    }

    internal static JsonSerializerOptions CreateOptions() => new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

public sealed class JsonJobHistoryStore(LocalDataPaths paths, int maximumRecords = 100) : IJobHistoryStore
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<IReadOnlyList<JobHistoryRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureDirectories();
        if (!File.Exists(paths.HistoryPath)) return [];
        try
        {
            await using var stream = File.OpenRead(paths.HistoryPath);
            return await JsonSerializer.DeserializeAsync<List<JobHistoryRecord>>(stream, JsonSettingsStore.CreateOptions(), cancellationToken).ConfigureAwait(false) ?? [];
        }
        catch (JsonException) { return []; }
        catch (IOException) { return []; }
    }

    public async Task AppendAsync(JobHistoryRecord record, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = (await LoadAsync(cancellationToken).ConfigureAwait(false)).ToList();
            records.Insert(0, record);
            if (records.Count > maximumRecords) records.RemoveRange(maximumRecords, records.Count - maximumRecords);
            await SaveAsync(records, cancellationToken).ConfigureAwait(false);
        }
        finally { Gate.Release(); }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await SaveAsync([], cancellationToken).ConfigureAwait(false); }
        finally { Gate.Release(); }
    }

    private async Task SaveAsync(IReadOnlyList<JobHistoryRecord> records, CancellationToken cancellationToken)
    {
        paths.EnsureDirectories();
        var temporary = paths.HistoryPath + ".new";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            await JsonSerializer.SerializeAsync(stream, records, JsonSettingsStore.CreateOptions(), cancellationToken).ConfigureAwait(false);
        File.Move(temporary, paths.HistoryPath, true);
    }
}

public sealed class TemporaryFileCleaner(LocalDataPaths paths)
{
    public int DeleteAbandoned(DateTimeOffset now)
    {
        paths.EnsureDirectories();
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(paths.TemporaryDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) > now.UtcDateTime.AddHours(-24)) continue;
                File.Delete(file);
                removed++;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return removed;
    }
}
