namespace DictateTray.Core.Configuration;

public static class AppPaths
{
    public static string AppDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DictateTray");

    public static string SettingsFilePath => Path.Combine(AppDataRoot, "settings.json");

    public static string LogDirectoryPath => Path.Combine(AppDataRoot, "logs");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(LogDirectoryPath);
    }
}
