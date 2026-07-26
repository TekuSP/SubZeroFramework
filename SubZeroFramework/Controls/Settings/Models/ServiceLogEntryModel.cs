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

    public ServiceLogEntryModel(ServiceLogEntry entry, ServiceLogEntrySource source)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entry = entry;
        Source = source;
        SourceLabel = source == ServiceLogEntrySource.App ? "APP" : "SVC";

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

    /// <summary>Which process logged this — the two are interleaved in one list.</summary>
    public ServiceLogEntrySource Source { get; }

    /// <summary>Short chip text for <see cref="Source"/>, sized to sit next to the level chip.</summary>
    public string SourceLabel { get; }

    /// <summary>Sort key for interleaving app and service entries. UTC, as carried on the wire.</summary>
    public DateTimeOffset ObservedAt => _entry.ObservedAt;

    /// <summary>The raw severity, for filtering. <see cref="Level"/> is the three-letter display form.</summary>
    public LogLevel Severity => _entry.Level;

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

    /// <summary>
    /// One line per entry, with the FULL category — this is what gets pasted into a bug report. The source is
    /// included because app and service entries are interleaved, and "which process said this" is usually the
    /// first question asked of the paste.
    /// </summary>
    public string ToClipboardLine()
    {
        var line = $"{_entry.ObservedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff} [{SourceLabel}] [{Level}] {_entry.Category}: {Message}";
        return string.IsNullOrEmpty(Exception) ? line : $"{line}{Environment.NewLine}{Exception}";
    }
}
