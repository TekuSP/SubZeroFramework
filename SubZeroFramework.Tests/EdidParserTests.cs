using NUnit.Framework;

using SubZeroFramework.Services.Linux;

namespace SubZeroFramework.Tests;

/// <summary>
/// Pins the EDID base-block layout against real captured blobs.
/// </summary>
/// <remarks>
/// A byte-offset error in this parser does not fail loudly — it produces a plausible-looking but wrong monitor
/// name, size or refresh rate, which is worse than reporting nothing. The primary fixture is the panel of the
/// machine this feature was developed on, and every expected value below was confirmed independently against
/// what Windows reports for the same display.
/// </remarks>
[TestFixture]
public class EdidParserTests
{
    /// <summary>
    /// BOE NE160QDM-NZ6 — the Framework 16 2560x1600 panel. Captured from the Windows display registry
    /// (HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY\BOE0D79\...\Device Parameters\EDID), which is byte-for-byte
    /// the same blob Linux exposes at /sys/class/drm/card*-eDP-1/edid.
    /// </summary>
    private const string FrameworkPanelEdid =
        "00ffffffffffff0009e5790d000000002a220104a5221678033d35ae5043b1250e5054" +
        "00000001010101010101010101010101010101347000a0a040a0603020360059d71000" +
        "001a000000000000000000000000000000000000000000fe00424f452043510a202020" +
        "202020000000fc004e4531363051444d2d4e5a360a012a";

    [Test]
    public void FrameworkPanel_DecodesEveryFieldTheUiShows()
    {
        Assert.That(EdidParser.TryParseHex(FrameworkPanelEdid, out var info), Is.True);
        Assert.That(info, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(info!.ManufacturerId, Is.EqualTo("BOE"), "packed 5-bit vendor letters");
            Assert.That(info.ProductCode, Is.EqualTo(0x0D79), "little-endian product code");
            Assert.That(info.SerialNumber, Is.Zero, "this panel reports no binary serial");
            Assert.That(info.WeekOfManufacture, Is.EqualTo(42));
            Assert.That(info.YearOfManufacture, Is.EqualTo(2024), "byte 17 is an offset from 1990");
            Assert.That(info.VersionMajor, Is.EqualTo(1));
            Assert.That(info.VersionMinor, Is.EqualTo(4));
            Assert.That(info.WidthCentimeters, Is.EqualTo(34));
            Assert.That(info.HeightCentimeters, Is.EqualTo(22));
            Assert.That(info.MonitorName, Is.EqualTo("NE160QDM-NZ6"), "descriptor tag 0xFC");
            Assert.That(info.DataString, Is.EqualTo("BOE CQ"), "descriptor tag 0xFE");
            Assert.That(info.SerialNumberText, Is.Null, "no 0xFF descriptor on this panel");
            Assert.That(info.ExtensionBlockCount, Is.EqualTo(1));
            Assert.That(info.ChecksumValid, Is.True);
            Assert.That(info.DisplayName, Is.EqualTo("NE160QDM-NZ6"));
        });
    }

    [Test]
    public void FrameworkPanel_PreferredTimingIsTheBaseBlockMode_NotThePanelMaximum()
    {
        Assert.That(EdidParser.TryParseHex(FrameworkPanelEdid, out var info), Is.True);
        var timing = info!.PreferredTiming;

        Assert.That(timing, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(timing!.HorizontalActive, Is.EqualTo(2560), "high nibble of byte 4 + byte 2");
            Assert.That(timing.VerticalActive, Is.EqualTo(1600), "high nibble of byte 7 + byte 5");
            Assert.That(timing.PixelClockHz, Is.EqualTo(287_240_000L), "10 kHz units, little endian");
            Assert.That(timing.Interlaced, Is.False);
            Assert.That(timing.WidthMillimeters, Is.EqualTo(345));
            Assert.That(timing.HeightMillimeters, Is.EqualTo(215));
            // 287.24 MHz / (2720 total x 1760 total). The panel actually RUNS at 165 Hz: the high-refresh
            // modes live in the DisplayID extension block, which the base block only counts. This assertion
            // exists to stop anyone "fixing" the preferred rate into the panel maximum.
            Assert.That(timing.RefreshHz, Is.EqualTo(60d).Within(0.01d));
        });
    }

    [Test]
    public void ShortOrUnmagicBuffers_AreRejected_NeverGuessed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(EdidParser.TryParse(new byte[64], out _), Is.False, "shorter than a base block");
            Assert.That(EdidParser.TryParse(new byte[128], out _), Is.False, "all zeroes has no header magic");
            Assert.That(EdidParser.TryParseHex(string.Empty, out _), Is.False);
            Assert.That(EdidParser.TryParseHex("00ffff", out _), Is.False, "truncated hex");
            Assert.That(EdidParser.TryParseHex("00ffffffffffff0", out _), Is.False, "odd digit count");
        });
    }

    [Test]
    public void UnwritableEdid_ReadsAsAbsent_RatherThanAsAMonitorNamedGarbage()
    {
        // A disconnected DisplayPort connector commonly exposes an all-0xFF or all-0x00 edid file. Neither
        // carries the header magic, so both must be rejected outright.
        var allOnes = new byte[128];
        Array.Fill(allOnes, (byte)0xFF);

        Assert.That(EdidParser.TryParse(allOnes, out _), Is.False);
    }

    [Test]
    public void BadChecksum_StillParses_ButIsFlagged()
    {
        // The kernel hands us EDIDs it already accepted, and cheap panels ship stale checksums with otherwise
        // sane fields. Dropping the display entirely would be a worse outcome than showing it with a caveat.
        Assert.That(EdidParser.TryParseHex(FrameworkPanelEdid, out var good), Is.True);

        var bytes = HexToBytes(FrameworkPanelEdid);
        bytes[127] ^= 0xFF;

        Assert.That(EdidParser.TryParse(bytes, out var tampered), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(tampered!.ChecksumValid, Is.False);
            Assert.That(tampered.MonitorName, Is.EqualTo(good!.MonitorName), "the payload is still readable");
        });
    }

    [Test]
    public void ManufacturerId_RejectsNonLetterPackings()
    {
        // Vendor bytes of 0x0000 unpack to '@@@' — outside A-Z, so the field must come back empty rather than
        // as punctuation the UI would show as a manufacturer.
        var bytes = HexToBytes(FrameworkPanelEdid);
        bytes[8] = 0x00;
        bytes[9] = 0x00;

        Assert.That(EdidParser.TryParse(bytes, out var info), Is.True);
        Assert.That(info!.ManufacturerId, Is.Empty);
    }

    [Test]
    public void DisplayName_FallsBackThroughDescriptorsToVendorAndProduct()
    {
        var bytes = HexToBytes(FrameworkPanelEdid);
        // Blank the 0xFC monitor-name descriptor's tag so only the 0xFE data string remains.
        bytes[108 + 3] = 0x00;

        Assert.That(EdidParser.TryParse(bytes, out var withoutName), Is.True);
        Assert.That(withoutName!.DisplayName, Is.EqualTo("BOE CQ"));

        // Blank the 0xFE descriptor too: nothing human-readable is left, so fall back to vendor + product.
        bytes[90 + 3] = 0x00;

        Assert.That(EdidParser.TryParse(bytes, out var anonymous), Is.True);
        Assert.That(anonymous!.DisplayName, Is.EqualTo("BOE 0D79"));
    }

    private static byte[] HexToBytes(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
        }

        return bytes;
    }
}
