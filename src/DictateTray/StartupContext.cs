using DictateTray.Core.Configuration;
using DictateTray.Core.Logging;

namespace DictateTray;

internal sealed class StartupContext : ApplicationContext
{
    private readonly IAppLogger _logger;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;

    public StartupContext(IAppLogger logger, SettingsService settingsService, AppSettings settings)
    {
        _logger = logger;
        _settingsService = settingsService;
        _settings = settings;

        _logger.Info($"Settings loaded from {AppPaths.SettingsFilePath}");
    }
}
