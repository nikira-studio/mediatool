using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace MediaTool.App;

internal static class ShellFileActions
{
    public static Task<bool> OpenAsync(string? path, bool showInFolder)
    {
        if (string.IsNullOrWhiteSpace(path)) return Task.FromResult(false);
        return Task.Run(() =>
        {
            try
            {
                if (!File.Exists(path)) return false;
                if (showInFolder)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{path}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                else
                {
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                }
                return true;
            }
            catch (Exception exception) when (exception is IOException or Win32Exception)
            {
                return false;
            }
        });
    }

    public static Task<bool> OpenFolderAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Task.FromResult(false);
        return Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(path)) return false;
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                return true;
            }
            catch (Exception exception) when (exception is IOException or Win32Exception)
            {
                return false;
            }
        });
    }
}
