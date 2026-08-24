using System.Globalization;

using Microsoft.UI.Xaml.Data;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Converters;

/// <summary>
/// Converts a canonical quantity to the user's display unit as a NUMBER, for the properties that take a
/// value rather than text — chart axis limits and steps above all.
/// </summary>
/// <remarks>
/// <para>
/// The numeric sibling of <see cref="UnitFormatConverter"/>. That one returns a formatted string and must
/// never be bound to a <c>MinLimit</c> / <c>MaxLimit</c> / <c>MinStep</c>, which are <c>double</c>: an axis
/// limit is a coordinate in a plotting space, not display text.
/// </para>
/// <code>
/// MaxLimit="{x:Bind ViewModel.PeakRpm, Mode=OneWay,
///            Converter={StaticResource UnitValue}, ConverterParameter=FanSpeed}"
/// </code>
/// <para>
/// <b>Only usable where the source raises PropertyChanged.</b> A converter cannot re-run on its own, so
/// binding one to a static constant would freeze the axis at whatever unit was selected when the page
/// loaded. For a fixed bound — a curve editor's visible window, say — convert in the VIEW MODEL and let
/// <c>RefreshUnitFormatting</c> reassign it. This converter earns its place where the limit already tracks
/// an observable canonical value.
/// </para>
/// <para>
/// <b>Steps and widths need <see cref="IsDelta"/>.</b> A temperature scale has an offset, so a 10 °C axis
/// step is 18 °F — not the 50 °F an absolute conversion produces. Every other quantity scales without an
/// offset and is unaffected, which is exactly why the mistake survives review: it is invisible in Celsius
/// and in every non-temperature chart.
/// </para>
/// </remarks>
public sealed class UnitValueConverter : IValueConverter
{
    private readonly IUnitFormattingService _unitFormattingService;

    public UnitValueConverter(IUnitFormattingService unitFormattingService)
        => _unitFormattingService = unitFormattingService;

    /// <summary>
    /// Treats the value as a DIFFERENCE rather than a point on the scale — for an axis step or a band width.
    /// </summary>
    public bool IsDelta { get; init; }

    /// <summary>Returned when the value is not a number, so a null source leaves the axis to auto-range.</summary>
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (ToNullableDouble(value) is not { } quantity)
        {
            return null;
        }

        var kind = ParseKind(parameter);

        return kind switch
        {
            // The only quantity whose scale carries an offset, hence the only one where a delta differs.
            UnitQuantityKind.Temperature => IsDelta
                ? _unitFormattingService.ConvertTemperatureDelta(quantity)
                : _unitFormattingService.ConvertTemperature(quantity),

            UnitQuantityKind.FanSpeed => _unitFormattingService.ConvertFanSpeed(quantity),
            UnitQuantityKind.ClockFrequency => _unitFormattingService.ConvertClockFrequencyMegahertz(quantity),
            UnitQuantityKind.RefreshRate => _unitFormattingService.ConvertRefreshRateHertz(quantity),
            UnitQuantityKind.Voltage => _unitFormattingService.ConvertVoltage(quantity),
            UnitQuantityKind.Current => _unitFormattingService.ConvertCurrent(quantity),
            UnitQuantityKind.ElectricChargeCapacity => _unitFormattingService.ConvertChargeCapacity(quantity),
            UnitQuantityKind.Energy => _unitFormattingService.ConvertEnergyWattHours(quantity),
            UnitQuantityKind.Ratio => _unitFormattingService.ConvertRatio(quantity),
            UnitQuantityKind.Length => _unitFormattingService.ConvertLengthMillimeters(quantity),
            UnitQuantityKind.Airflow => _unitFormattingService.ConvertAirflowCfm(quantity),
            UnitQuantityKind.BitRate => _unitFormattingService.ConvertBitRateBitsPerSecond(quantity),
            UnitQuantityKind.Power => _unitFormattingService.ConvertPowerWatts(quantity),

            // Information size has no single scale factor — its display unit is chosen per magnitude — so a
            // byte count has no meaningful numeric conversion to plot against.
            _ => throw new NotSupportedException(
                $"{nameof(UnitValueConverter)} has no numeric conversion for {kind}."),
        };
    }

    /// <summary>
    /// Not supported. An axis limit is written by the view model, never edited by the user; an input control
    /// that needs the inverse converts in its view model so its bounds convert too.
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException(
            "UnitValueConverter is one-way. An editable quantity converts in its view model.");

    private static UnitQuantityKind ParseKind(object parameter)
    {
        if (parameter is UnitQuantityKind typed)
        {
            return typed;
        }

        if (parameter is string text
            && Enum.TryParse<UnitQuantityKind>(text.Trim(), ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new ArgumentException(
            $"ConverterParameter must name a {nameof(UnitQuantityKind)}; got '{parameter ?? "null"}'.",
            nameof(parameter));
    }

    private static double? ToNullableDouble(object value) => value switch
    {
        null => null,
        double doubleValue => doubleValue,
        float floatValue => floatValue,
        int intValue => intValue,
        long longValue => longValue,
        uint unsignedValue => unsignedValue,
        ulong unsignedLongValue => unsignedLongValue,
        decimal decimalValue => (double)decimalValue,
        IConvertible convertible => convertible.ToDouble(CultureInfo.InvariantCulture),
        _ => null,
    };
}
