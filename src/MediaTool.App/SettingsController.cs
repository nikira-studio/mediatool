using MediaTool.Core;

namespace MediaTool.App;

/// <summary>Owns the in-memory settings snapshot and is the only UI-layer writer to the settings store.</summary>
public sealed class SettingsController(ISettingsStore store)
{
    public AppSettings Current { get; private set; } = AppSettings.Default;
    public event EventHandler<AppSettings>? Changed;

    public async Task LoadAsync()
    {
        Current = await store.LoadAsync();
        Changed?.Invoke(this, Current);
    }

    public async Task UpdateAsync(Func<AppSettings, AppSettings> update)
    {
        Current = update(Current);
        await store.SaveAsync(Current);
        Changed?.Invoke(this, Current);
    }

    public async Task ResetAsync()
    {
        Current = await store.ResetAsync();
        Changed?.Invoke(this, Current);
    }
}
