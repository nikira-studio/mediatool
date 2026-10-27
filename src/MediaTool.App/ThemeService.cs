using System.Windows;
using MediaTool.Core;
using Wpf.Ui.Appearance;

namespace MediaTool.App;

internal static class ThemeService
{
    public static void Apply(AppTheme theme, Window? window = null)
    {
        if (SystemParameters.HighContrast)
        {
            ApplicationThemeManager.Apply(ApplicationTheme.HighContrast);
            return;
        }

        switch (theme)
        {
            case AppTheme.System:
                ApplicationThemeManager.ApplySystemTheme();
                if (window is not null) SystemThemeWatcher.Watch(window);
                break;
            case AppTheme.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                break;
            case AppTheme.Dark:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                break;
        }
    }
}
