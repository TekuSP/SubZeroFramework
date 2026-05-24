namespace SubZeroFramework.Service.Services;

public static class FrameworkServiceConfigurationPaths
{
    public static string GetPersistentConfigurationPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var commonApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var baseDirectory = string.IsNullOrWhiteSpace(commonApplicationData)
                ? AppContext.BaseDirectory
                : commonApplicationData;

            return Path.Combine(baseDirectory, "SubZeroFramework", "service-settings.json");
        }

        if (OperatingSystem.IsLinux())
        {
            return "/etc/subzeroframework/service-settings.json";
        }

        return Path.Combine(AppContext.BaseDirectory, "service-settings.json");
    }

    public static string GetUserPreferencesPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var commonApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var baseDirectory = string.IsNullOrWhiteSpace(commonApplicationData)
                ? AppContext.BaseDirectory
                : commonApplicationData;

            return Path.Combine(baseDirectory, "SubZeroFramework", "user-preferences.json");
        }

        if (OperatingSystem.IsLinux())
        {
            return "/etc/subzeroframework/user-preferences.json";
        }

        return Path.Combine(AppContext.BaseDirectory, "user-preferences.json");
    }
}
