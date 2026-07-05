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
}
