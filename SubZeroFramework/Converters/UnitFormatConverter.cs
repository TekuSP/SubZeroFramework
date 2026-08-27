using System.Globalization;

using Microsoft.UI.Xaml.Data;

using SubZeroFramework.Models;
using SubZeroFramework.Services.Units;

namespace SubZeroFramework.Converters;

/// <summary>
/// Formats a canonical quantity for display, in the user's chosen unit, from XAML.
/// </summary>
/// <remarks>
/// <para>
/// The read-only half of the units story. A view model holds the value in its CANONICAL unit — Celsius,
/// watts, RPM, bytes — and the binding names the quantity kind:
/// </para>
/// <code>
/// Text="{x:Bind ViewModel.PowerWatts, Mode=OneWay,
///        Converter={StaticResource UnitFormat}, ConverterParameter=Power}"
/// </code>
/// <para>
/// One converter rather than one per quantity, keyed by <see cref="UnitQuantityKind"/> so the vocabulary is
/// the same enum the preferences catalog already uses. An unrecognised parameter throws rather than silently
/// falling back: a typo that quietly rendered a raw Celsius number as if it were Fahrenheit is exactly the
/// class of bug this whole layer exists to prevent.
/// </para>
/// <para>
/// <b>This converter does not observe anything.</b> A converter is pull-only — it runs when a binding
/// evaluates it and has no handle on the bindings that use it, so subscribing to a units stream here would
/// achieve nothing. Re-evaluation is the view model's job. The PAGE model subscribes once to
/// <c>IUserUnitPreferencesClient.WatchPreferences()</c> and calls <c>RefreshUnitFormatting()</c> on each of
/// its cards — one subscription per page rather than one per card, of which there can be dozens. That method
/// recomputes the few things a converter cannot (composite strings, chart axis labelers and limits) and then
/// raises <c>PropertyChanged</c> with a NULL name.
/// </para>
/// <para>
/// The null name is the framework's own signal for "re-read everything": the generated x:Bind code tests
/// <c>String.IsNullOrEmpty(propName)</c> and, when true, calls every <c>Update_…</c> method for that source
/// (verified in the generated <c>.g.cs</c>). It is NOT the revision-counter pattern this codebase removed —
/// that faked a value change to force a re-read. Here the values genuinely have not changed, only their
/// presentation, which is exactly what the null name means.
/// </para>
/// <para>
/// Instantiated in <c>App.xaml.cs</c> with the DI-resolved formatting service and placed in application
/// resources, NOT constructed by XAML. A XAML-constructed converter could not be given the service without a
/// static or a service locator, which would make the unit preference a hidden global and cost the formatting
/// service its testability.
/// </para>
/// </remarks>
public sealed class UnitFormatConverter : IValueConverter
{
    private readonly IUnitFormattingService _unitFormattingService;

    public UnitFormatConverter(IUnitFormattingService unitFormattingService)
        => _unitFormattingService = unitFormattingService;

    /// <summary>Shown when the value is null — a reading that was not taken, rather than a zero.</summary>
    public string UnavailableDisplay { get; init; } = "--";

    /// <summary>
    /// Emits the bare number without a unit suffix, for a control that renders the suffix itself.
    /// </summary>
    /// <remarks>
    /// Several tiles draw the value large and the unit small beside it, so the suffix cannot be part of the
    /// same string. That is what the service's <c>Format…Value</c> overloads exist for, and this selects them.
    /// </remarks>
    public bool ValueOnly { get; init; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var (kind, decimals) = ParseParameter(parameter);
        var quantity = ToNullableDouble(value);

        return kind switch
        {
            UnitQuantityKind.Temperature => ValueOnly
                ? _unitFormattingService.FormatTemperatureValue(quantity, UnavailableDisplay, decimals ?? 0)
                : _unitFormattingService.FormatTemperature(quantity, UnavailableDisplay, decimals ?? 0),
            UnitQuantityKind.FanSpeed => ValueOnly
                ? _unitFormattingService.FormatFanSpeedValue(quantity, UnavailableDisplay, decimals ?? -1)
                : _unitFormattingService.FormatFanSpeed(quantity, UnavailableDisplay, decimals ?? -1),
            UnitQuantityKind.ClockFrequency => ValueOnly
                ? _unitFormattingService.FormatClockFrequencyValueMegahertz(quantity, UnavailableDisplay, decimals ?? -1)
                : _unitFormattingService.FormatClockFrequencyMegahertz(quantity, UnavailableDisplay, decimals ?? -1),
            UnitQuantityKind.RefreshRate => ValueOnly
                ? _unitFormattingService.FormatRefreshRateValueHertz(quantity, UnavailableDisplay, decimals ?? -1)
                : _unitFormattingService.FormatRefreshRateHertz(quantity, UnavailableDisplay, decimals ?? -1),
            UnitQuantityKind.Voltage => ValueOnly
                ? _unitFormattingService.FormatVoltageValue(quantity, UnavailableDisplay, decimals ?? -1)
                : _unitFormattingService.FormatVoltage(quantity, UnavailableDisplay, decimals ?? -1),
            UnitQuantityKind.Current => ValueOnly
                ? _unitFormattingService.FormatCurrentValue(quantity, UnavailableDisplay, decimals ?? -1)
                : _unitFormattingService.FormatCurrent(quantity, UnavailableDisplay, decimals ?? -1),
            UnitQuantityKind.Energy => ValueOnly
                ? _unitFormattingService.FormatEnergyValueWattHours(quantity, UnavailableDisplay, decimals ?? -1)
                : _unitFormattingService.FormatEnergyWattHours(quantity, UnavailableDisplay, decimals ?? -1),
            UnitQuantityKind.ElectricChargeCapacity => ValueOnly
                ? _unitFormattingService.FormatChargeCapacityValue(quantity, UnavailableDisplay, decimals ?? -1)
                : _unitFormattingService.FormatChargeCapacity(quantity, UnavailableDisplay, decimals ?? -1),
            // -1 defers to the PER-UNIT default (percent 0, fraction 2, …). Forcing 0 here would render a
            // fraction-preference 0.5 as "0" — the explicit :N suffix remains for sites that want otherwise.
            UnitQuantityKind.Ratio => ValueOnly
                ? _unitFormattingService.FormatRatioValue(quantity, UnavailableDisplay, decimals ?? -1)
                : _unitFormattingService.FormatRatio(quantity, UnavailableDisplay, decimals ?? -1),
            UnitQuantityKind.Power => ValueOnly
                ? _unitFormattingService.FormatPowerValueWatts(quantity, UnavailableDisplay, decimals ?? -1)
                : _unitFormattingService.FormatPowerWatts(quantity, UnavailableDisplay, decimals ?? -1),
            UnitQuantityKind.BitRate => ValueOnly
                ? _unitFormattingService.FormatBitRateValueBitsPerSecond(quantity, UnavailableDisplay, decimals ?? -1)
                : _unitFormattingService.FormatBitRateBitsPerSecond(quantity, UnavailableDisplay, decimals ?? -1),
            UnitQuantityKind.Length => ValueOnly
                ? _unitFormattingService.FormatLengthValueMillimeters(quantity, UnavailableDisplay, decimals ?? -1)
                : _unitFormattingService.FormatLengthMillimeters(quantity, UnavailableDisplay, decimals ?? -1),
            UnitQuantityKind.Airflow => ValueOnly
                ? _unitFormattingService.FormatAirflowValueCfm(quantity, UnavailableDisplay, decimals ?? -1)
                : _unitFormattingService.FormatAirflowCfm(quantity, UnavailableDisplay, decimals ?? -1),

            // Information size takes an unsigned count of bytes rather than a nullable double, so it needs
            // its own path instead of sharing the conversion above.
            UnitQuantityKind.InformationSize => quantity is { } bytes && bytes >= 0d
                ? _unitFormattingService.FormatInformationBytes(checked((ulong)bytes), treatZeroAsUnknown: false, UnavailableDisplay)
                : UnavailableDisplay,

            _ => throw new NotSupportedException(
                $"{nameof(UnitFormatConverter)} has no formatting for {kind}. Add it here and to IUnitFormattingService together."),
        };
    }

    /// <summary>
    /// Not supported. Editing a quantity needs the inverse conversion AND display-unit bounds for the
    /// control's Minimum and Maximum, which a converter cannot supply — so an input control converts in its
    /// view model instead. See the units project skill, rule 3.
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException(
            "UnitFormatConverter is one-way. An editable quantity converts in its view model so Minimum and Maximum convert too.");

    /// <summary>
    /// Reads the converter parameter, which is a quantity kind with an optional decimal count:
    /// <c>Power</c>, or <c>Ratio:1</c> for a site that wants one decimal place.
    /// </summary>
    /// <remarks>
    /// The precision suffix exists because it genuinely varies by site — a per-core load reads better at one
    /// decimal than the whole-device figure beside it. Omitting it keeps each quantity's own default, so the
    /// common case stays short.
    /// </remarks>
    private static (UnitQuantityKind Kind, int? Decimals) ParseParameter(object parameter)
    {
        if (parameter is UnitQuantityKind typed)
        {
            return (typed, null);
        }

        if (parameter is string text)
        {
            var separator = text.IndexOf(':');
            var kindText = separator < 0 ? text : text[..separator];

            if (Enum.TryParse<UnitQuantityKind>(kindText.Trim(), ignoreCase: true, out var parsed))
            {
                if (separator < 0)
                {
                    return (parsed, null);
                }

                if (int.TryParse(text[(separator + 1)..].Trim(), out var decimals))
                {
                    return (parsed, decimals);
                }

                throw new ArgumentException(
                    $"ConverterParameter '{text}' has a precision suffix that is not a number.",
                    nameof(parameter));
            }
        }

        throw new ArgumentException(
            $"ConverterParameter must name a {nameof(UnitQuantityKind)}, optionally with a precision suffix "
            + $"(for example Power or Ratio:1); got '{parameter ?? "null"}'.",
            nameof(parameter));
    }

    /// <summary>
    /// Accepts the numeric types a view model realistically holds a canonical quantity in, and treats
    /// anything else as no reading.
    /// </summary>
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
