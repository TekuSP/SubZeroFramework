using System.Text;

namespace SubZeroFramework.Services.Linux;

/// <summary>
/// Decodes an EDID base block into <see cref="EdidDisplayInfo"/>.
/// </summary>
/// <remarks>
/// Pure and platform-independent so it can be unit tested against real captured blobs — byte-offset mistakes
/// here produce plausible-looking but wrong monitor names, which is worse than reporting nothing. Layout per
/// VESA E-EDID 1.3/1.4; only the 128-byte base block is decoded. Extension blocks (CTA-861, DisplayID) are
/// counted but not parsed, so high-refresh modes declared only in an extension are not seen here.
/// </remarks>
public static class EdidParser
{
    /// <summary>The base block is always 128 bytes; extensions follow in further 128-byte blocks.</summary>
    public const int BaseBlockLength = 128;

    private static ReadOnlySpan<byte> HeaderMagic => [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];

    // The four 18-byte descriptor slots. The first is conventionally the preferred detailed timing; any slot
    // whose first two bytes are zero is a display descriptor (name / serial / range limits) instead.
    private static ReadOnlySpan<int> DescriptorOffsets => [54, 72, 90, 108];

    private const int DescriptorLength = 18;
    private const byte DescriptorTagSerialNumber = 0xFF;
    private const byte DescriptorTagDataString = 0xFE;
    private const byte DescriptorTagMonitorName = 0xFC;

    /// <summary>
    /// Decodes the base block. Returns false only when the buffer is too short or the header magic is absent —
    /// a bad checksum is reported through <see cref="EdidDisplayInfo.ChecksumValid"/> rather than rejected.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> edid, out EdidDisplayInfo? info)
    {
        info = null;

        if (edid.Length < BaseBlockLength || !edid[..8].SequenceEqual(HeaderMagic))
        {
            return false;
        }

        var baseBlock = edid[..BaseBlockLength];

        string? monitorName = null;
        string? serialNumberText = null;
        string? dataString = null;
        EdidDetailedTiming? preferredTiming = null;

        foreach (var offset in DescriptorOffsets)
        {
            var descriptor = baseBlock.Slice(offset, DescriptorLength);

            // Bytes 0-1 form the pixel clock; zero marks a display descriptor rather than a timing.
            if (descriptor[0] != 0 || descriptor[1] != 0)
            {
                preferredTiming ??= ParseDetailedTiming(descriptor);
                continue;
            }

            var text = ReadDescriptorText(descriptor);
            switch (descriptor[3])
            {
                case DescriptorTagMonitorName:
                    monitorName ??= text;
                    break;
                case DescriptorTagSerialNumber:
                    serialNumberText ??= text;
                    break;
                case DescriptorTagDataString:
                    dataString ??= text;
                    break;
            }
        }

        var weekByte = baseBlock[16];
        var yearByte = baseBlock[17];

        info = new EdidDisplayInfo
        {
            ManufacturerId = ReadManufacturerId(baseBlock),
            ProductCode = (ushort)(baseBlock[10] | (baseBlock[11] << 8)),
            SerialNumber = (uint)(baseBlock[12] | (baseBlock[13] << 8) | (baseBlock[14] << 16) | (baseBlock[15] << 24)),
            // Week 0 means unspecified; 0xFF redefines the year byte as a model year, so there is no week either.
            WeekOfManufacture = weekByte is 0 or 0xFF ? null : weekByte,
            // Year is an offset from 1990. A zero byte would claim 1990, which in practice means "not set".
            YearOfManufacture = yearByte == 0 ? null : 1990 + yearByte,
            VersionMajor = baseBlock[18],
            VersionMinor = baseBlock[19],
            WidthCentimeters = baseBlock[21],
            HeightCentimeters = baseBlock[22],
            MonitorName = monitorName,
            SerialNumberText = serialNumberText,
            DataString = dataString,
            PreferredTiming = preferredTiming,
            ExtensionBlockCount = baseBlock[126],
            ChecksumValid = IsChecksumValid(baseBlock),
        };

        return true;
    }

    /// <summary>All 128 bytes of a valid block sum to a multiple of 256.</summary>
    public static bool IsChecksumValid(ReadOnlySpan<byte> block)
    {
        if (block.Length < BaseBlockLength)
        {
            return false;
        }

        var sum = 0;
        for (var index = 0; index < BaseBlockLength; index++)
        {
            sum += block[index];
        }

        return sum % 256 == 0;
    }

    // Bytes 8-9 hold three 5-bit letters, big endian, with 1 = 'A'. Bit 15 is reserved zero.
    private static string ReadManufacturerId(ReadOnlySpan<byte> baseBlock)
    {
        var packed = (baseBlock[8] << 8) | baseBlock[9];

        Span<char> letters =
        [
            (char)(((packed >> 10) & 0x1F) + 'A' - 1),
            (char)(((packed >> 5) & 0x1F) + 'A' - 1),
            (char)((packed & 0x1F) + 'A' - 1),
        ];

        foreach (var letter in letters)
        {
            if (letter is < 'A' or > 'Z')
            {
                return string.Empty;
            }
        }

        return new string(letters);
    }

    // Display-descriptor text lives in bytes 5..17, ends at the first 0x0A and is space padded after it.
    private static string? ReadDescriptorText(ReadOnlySpan<byte> descriptor)
    {
        var text = descriptor[5..DescriptorLength];
        var terminator = text.IndexOf((byte)0x0A);
        if (terminator >= 0)
        {
            text = text[..terminator];
        }

        Span<char> buffer = stackalloc char[text.Length];
        var length = 0;
        foreach (var value in text)
        {
            // Panels pad with spaces and very occasionally with NULs; keep printable ASCII only.
            if (value is >= 0x20 and < 0x7F)
            {
                buffer[length++] = (char)value;
            }
        }

        var result = new string(buffer[..length]).Trim();
        return result.Length == 0 ? null : result;
    }

    private static EdidDetailedTiming? ParseDetailedTiming(ReadOnlySpan<byte> descriptor)
    {
        // Bytes 0-1: pixel clock in 10 kHz units, little endian.
        var pixelClockHz = ((descriptor[1] << 8) | descriptor[0]) * 10_000L;
        if (pixelClockHz <= 0)
        {
            return null;
        }

        // Byte 4 packs the high nibbles: upper for horizontal active, lower for horizontal blanking.
        var horizontalActive = (((descriptor[4] >> 4) & 0x0F) << 8) | descriptor[2];
        var horizontalBlanking = ((descriptor[4] & 0x0F) << 8) | descriptor[3];
        // Byte 7 packs the same for vertical.
        var verticalActive = (((descriptor[7] >> 4) & 0x0F) << 8) | descriptor[5];
        var verticalBlanking = ((descriptor[7] & 0x0F) << 8) | descriptor[6];

        var horizontalTotal = horizontalActive + horizontalBlanking;
        var verticalTotal = verticalActive + verticalBlanking;

        if (horizontalActive <= 0 || verticalActive <= 0 || horizontalTotal <= 0 || verticalTotal <= 0)
        {
            return null;
        }

        // Byte 14 packs the high nibbles of the millimetre image size the same way.
        var widthMillimeters = (((descriptor[14] >> 4) & 0x0F) << 8) | descriptor[12];
        var heightMillimeters = ((descriptor[14] & 0x0F) << 8) | descriptor[13];

        var interlaced = (descriptor[17] & 0x80) != 0;
        var refreshHz = (double)pixelClockHz / (horizontalTotal * (long)verticalTotal);

        return new EdidDetailedTiming
        {
            HorizontalActive = horizontalActive,
            VerticalActive = verticalActive,
            PixelClockHz = pixelClockHz,
            // An interlaced mode draws two fields per frame, so its field rate is twice the frame rate.
            RefreshHz = interlaced ? refreshHz * 2d : refreshHz,
            WidthMillimeters = widthMillimeters,
            HeightMillimeters = heightMillimeters,
            Interlaced = interlaced,
        };
    }

    /// <summary>
    /// Parses the hex text form the kernel also exposes (and that edid-decode consumes), for diagnostics and
    /// tests. Whitespace and newlines are ignored.
    /// </summary>
    public static bool TryParseHex(string hex, out EdidDisplayInfo? info)
    {
        info = null;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var builder = new StringBuilder(hex.Length);
        foreach (var character in hex)
        {
            if (!char.IsWhiteSpace(character))
            {
                builder.Append(character);
            }
        }

        if (builder.Length % 2 != 0)
        {
            return false;
        }

        var bytes = new byte[builder.Length / 2];
        for (var index = 0; index < bytes.Length; index++)
        {
            if (!byte.TryParse(builder.ToString(index * 2, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out bytes[index]))
            {
                return false;
            }
        }

        return TryParse(bytes, out info);
    }
}
