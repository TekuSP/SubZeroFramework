namespace SubZeroFramework.Services;

public static class FrameworkServiceAutorunStateParser
{
    public static bool? ParseWindowsScQcOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        if (output.Contains("AUTO_START", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (output.Contains("DELAYED_AUTO_START", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (output.Contains("DEMAND_START", StringComparison.OrdinalIgnoreCase)
            || output.Contains("DISABLED", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    public static bool? ParseLinuxSystemctlIsEnabledOutput(string output, int exitCode)
    {
        var normalizedOutput = output?.Trim();

        if (exitCode == 0)
        {
            return true;
        }

        if (string.Equals(normalizedOutput, "disabled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedOutput, "masked", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedOutput, "static", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedOutput, "indirect", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedOutput, "generated", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedOutput, "transient", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedOutput, "bad", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return exitCode == 1 ? false : null;
    }
}
