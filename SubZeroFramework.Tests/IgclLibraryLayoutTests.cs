using NUnit.Framework;

using SubZeroFramework.Services.Compute;

namespace SubZeroFramework.Tests;

/// <summary>
/// Pins the IGCL struct transcription to the layout <c>igcl_api.h</c> actually produces.
/// </summary>
/// <remarks>
/// This is the only check on the Intel binding that can run without an Intel GPU, and it guards the one
/// mistake that would matter: a field typed differently from the header shifts every field after it, so
/// <c>ctlPowerTelemetryGet</c> would return numbers read out of the wrong offsets — plausible-looking
/// garbage rather than an obvious failure.
///
/// The expected numbers come from compiling the header's struct definitions with a C compiler and printing
/// <c>sizeof</c>, not from reading the header by eye. Re-measure before changing any of them.
/// </remarks>
[TestFixture]
public class IgclLibraryLayoutTests
{
    [Test]
    public void MeasuredLayout_MatchesTheHeaderItWasTranscribedFrom()
    {
        Assert.That(IgclLibrary.MeasuredLayout, Is.EqualTo(IgclStructLayout.Header));
    }

    /// <summary>
    /// The individual sizes, asserted separately so a failure names the struct that drifted rather than
    /// printing two whole records and leaving the reader to diff them.
    /// </summary>
    [Test]
    public void MeasuredLayout_HasTheExpectedSizePerStruct()
    {
        var measured = IgclLibrary.MeasuredLayout;

        Assert.Multiple(() =>
        {
            Assert.That(measured.TelemetryItem, Is.EqualTo(24), "ctl_oc_telemetry_item_t");
            Assert.That(measured.PsuInfo, Is.EqualTo(56), "ctl_psu_info_t");
            Assert.That(measured.PowerTelemetry, Is.EqualTo(1024), "ctl_power_telemetry_t");
            Assert.That(measured.DeviceAdapterProperties, Is.EqualTo(320), "ctl_device_adapter_properties_t");
            Assert.That(measured.InitArgs, Is.EqualTo(36), "ctl_init_args_t");
            Assert.That(measured.MemoryState, Is.EqualTo(24), "ctl_mem_state_t");
            Assert.That(measured.FrequencyProperties, Is.EqualTo(32), "ctl_freq_properties_t");
            Assert.That(measured.TemperatureProperties, Is.EqualTo(24), "ctl_temp_properties_t");
        });
    }
}
