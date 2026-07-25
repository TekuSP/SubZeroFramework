using System.Globalization;

using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Models;
using SubZeroFramework.Themes;

namespace SubZeroFramework.Controls.Settings.Models;

/// <summary>
/// One line on the Service logs page: the timestamp, a severity chip, the logger category, and the message
/// (plus exception detail when the entry carried one).
/// </summary>
public sealed class ServiceLogEntryModel
{
    private readonly ServiceLogEntry _entry;

    public ServiceLogEntryModel(ServiceLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entry = entry;

        // Local time: the user is reading this against their own clock and their own recollection of when
        // something went wrong. The wire carries UTC.
        Timestamp = entry.ObservedAt.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.CurrentCulture);
        Level = entry.Level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "—",
        };

        // Only the type name — the full namespace pushes the message itself off the row.
        var category = entry.Category;
        var lastDot = category.LastIndexOf('.');
        Category = lastDot >= 0 && lastDot < category.Length - 1 ? category[(lastDot + 1)..] : category;

        Message = entry.Message;
        Exception = entry.Exception;
        ExceptionVisibility = string.IsNullOrEmpty(entry.Exception) ? Visibility.Collapsed : Visibility.Visible;
    }

    public string Timestamp { get; }

    public string Level { get; }

    public string Category { get; }

    public string Message { get; }

    public string Exception { get; }

    public Visibility ExceptionVisibility { get; }

    /// <summary>Severity colour for the level chip. Everything below Warning stays neutral so it does not shout.</summary>
    public Brush LevelForeground => new SolidColorBrush(_entry.Level switch
    {
        LogLevel.Critical or LogLevel.Error => AppThemeBrushes.SeverityCriticalColor,
        LogLevel.Warning => AppThemeBrushes.StatusWarningColor,
        _ => AppThemeBrushes.ChartSubtleAxisLabelColor,
    });

    /// <summary>One line per entry, with the FULL category and UTC — this is what gets pasted into a bug report.</summary>
    public string ToClipboardLine()
    {
        var line = $"{_entry.ObservedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff} [{Level}] {_entry.Category}: {Message}";
        return string.IsNullOrEmpty(Exception) ? line : $"{line}{Environment.NewLine}{Exception}";
    }
}
