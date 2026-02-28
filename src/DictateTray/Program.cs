using DictateTray.Core.Configuration;
using DictateTray.Core.Logging;

namespace DictateTray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        AppPaths.EnsureDirectories();
        var logger = new RollingFileLogger(AppPaths.LogDirectoryPath);
        var settingsService = new SettingsService();
        var settings = settingsService.LoadOrCreate();

        logger.Info("Application starting.");
        Application.Run(new StartupContext(logger, settingsService, settings));
    }
}
