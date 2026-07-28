using NUnit.Framework;

using SubZeroFramework.Services.Compute;

namespace SubZeroFramework.Tests;

[TestFixture]
public class GpuEngineInstanceNameTests
{
    [Test]
    public void TryParse_MixedCaseInstance_ReadsTheAdapterAndEngine()
    {
        // The shape captured on the Framework 16 dev machine. The LUID's two printed halves recombine into
        // the single INT64 the device property store reports as DEVPKEY_Gpu_Luid, which is what lets a
        // counter instance be matched back to a named device.
        var parsed = GpuEngineInstanceName.TryParse("pid_2908_luid_0x00000000_0x00018A19_phys_0_eng_0_engtype_3D", out var instance);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(instance.Luid, Is.EqualTo(0x00018A19L));
            Assert.That(instance.PhysicalIndex, Is.Zero);
            Assert.That(instance.EngineType, Is.EqualTo("3D"));
        });
    }

    [Test]
    public void TryParse_LowercaseInstance_ParsesTheSameWay()
    {
        // The same driver stack spells the whole name lowercase on some Windows builds — measured on this
        // machine, where every instance came back as "engtype_3d". Casing must never decide whether a device
        // is reportable.
        var parsed = GpuEngineInstanceName.TryParse("pid_10736_luid_0x00000000_0x00016626_phys_0_eng_0_engtype_3d", out var instance);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(instance.Luid, Is.EqualTo(0x00016626L));
            Assert.That(instance.PhysicalIndex, Is.Zero);

            // Preserved verbatim rather than normalized: the reader compares engine types case-insensitively,
            // so there is nothing to gain from rewriting what the driver said.
            Assert.That(instance.EngineType, Is.EqualTo("3d"));
        });
    }

    [Test]
    public void TryParse_EngineTypeWithTrailingOrdinal_KeepsTheWholeLabel()
    {
        // Newer engine labels carry an ordinal, and the separator before it is another underscore — so the
        // engine type cannot be read as "the next underscore-delimited token".
        Assert.Multiple(() =>
        {
            Assert.That(GpuEngineInstanceName.TryParse("pid_4_luid_0x00000000_0x0001898C_phys_0_eng_6_engtype_OFA_0", out var ofa), Is.True);
            Assert.That(ofa.EngineType, Is.EqualTo("OFA_0"));

            Assert.That(GpuEngineInstanceName.TryParse("pid_4_luid_0x00000000_0x0001898C_phys_0_eng_5_engtype_JPEG_Decode_0", out var jpeg), Is.True);
            Assert.That(jpeg.EngineType, Is.EqualTo("JPEG_Decode_0"));
        });
    }

    [Test]
    public void TryParse_EngineTypeWithSpaces_KeepsTheWholeLabel()
    {
        // Measured on the Radeon 890M: its engine labels contain spaces. Trailing-token parsing would
        // truncate these to "video" and "compute", silently merging engines that are not the same.
        Assert.Multiple(() =>
        {
            Assert.That(GpuEngineInstanceName.TryParse("pid_10736_luid_0x00000000_0x00016626_phys_0_eng_5_engtype_video codec engine", out var codec), Is.True);
            Assert.That(codec.EngineType, Is.EqualTo("video codec engine"));

            Assert.That(GpuEngineInstanceName.TryParse("pid_10736_luid_0x00000000_0x00016626_phys_0_eng_2_engtype_compute 0", out var compute), Is.True);
            Assert.That(compute.EngineType, Is.EqualTo("compute 0"));
        });
    }

    [Test]
    public void TryParse_NpuInstance_LooksLikeAnyOtherAdapter()
    {
        // The NPU has no counter set of its own — Windows enumerates it as an ordinary adapter inside
        // "GPU Engine" that happens to expose only compute engines. One parser covers both device kinds.
        var parsed = GpuEngineInstanceName.TryParse("pid_28588_luid_0x00000000_0x00019D12_phys_0_eng_0_engtype_compute", out var instance);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(instance.Luid, Is.EqualTo(0x00019D12L));
            Assert.That(instance.EngineType, Is.EqualTo("compute"));
        });
    }

    [Test]
    public void TryParse_NonZeroPhysicalIndex_IsRead()
    {
        // A LUID alone does not identify an adapter — the physical index pairs with it, both in the counter
        // name and in DEVPKEY_Gpu_PhyId — so it has to survive parsing.
        var parsed = GpuEngineInstanceName.TryParse("pid_512_luid_0x00000000_0x0001898C_phys_2_eng_1_engtype_copy", out var instance);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(instance.PhysicalIndex, Is.EqualTo(2));
        });
    }

    [Test]
    public void TryParse_NonZeroLuidHighPart_CombinesBothHalves()
    {
        // Every adapter observed so far has a zero high part, which would let a low-part-only parser pass
        // unnoticed until the day two adapters differ only above bit 32.
        var parsed = GpuEngineInstanceName.TryParse("pid_1_luid_0x00000007_0x0001898C_phys_0_eng_0_engtype_3D", out var instance);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(instance.Luid, Is.EqualTo(0x000000070001898CL));
        });
    }

    [Test]
    public void TryParse_UnexpectedShapes_ReturnFalse()
    {
        // The counter layout is undocumented, so a future Windows build may change it. Anything unrecognized
        // has to degrade to "not reportable" rather than throw or, worse, yield a plausible wrong adapter.
        Assert.Multiple(() =>
        {
            Assert.That(GpuEngineInstanceName.TryParse("total", out _), Is.False);
            Assert.That(GpuEngineInstanceName.TryParse("pid_2908_phys_0_eng_0_engtype_3D", out _), Is.False, "no LUID");
            Assert.That(GpuEngineInstanceName.TryParse("pid_2908_luid_0x00000000_0x00018A19_eng_0_engtype_3D", out _), Is.False, "no physical index");
            Assert.That(GpuEngineInstanceName.TryParse("pid_2908_luid_0x00000000_0x00018A19_phys_0_eng_0", out _), Is.False, "no engine type");
            Assert.That(GpuEngineInstanceName.TryParse("pid_2908_luid_0x00000000_0x00018A19_phys_0_eng_0_engtype_", out _), Is.False, "empty engine type");
            Assert.That(GpuEngineInstanceName.TryParse("pid_2908_luid_zzzz_0x00018A19_phys_0_eng_0_engtype_3D", out _), Is.False, "LUID is not hex");
            Assert.That(GpuEngineInstanceName.TryParse("pid_2908_luid_0x00000000_0x00018A19_phys_x_eng_0_engtype_3D", out _), Is.False, "physical index is not a number");
            Assert.That(GpuEngineInstanceName.TryParse("pid_2908_luid_0x00000000_0x00018A19_phys_-1_eng_0_engtype_3D", out _), Is.False, "negative physical index");
            Assert.That(GpuEngineInstanceName.TryParse("pid_2908_luid_0x00000000", out _), Is.False, "truncated after the LUID");
        });
    }

    [Test]
    public void TryParse_EmptyInput_ReturnsFalse()
    {
        // PDH hands back a name pointer per instance; an empty one is corrupt data, not an adapter.
        Assert.That(GpuEngineInstanceName.TryParse(string.Empty, out _), Is.False);
    }
}
