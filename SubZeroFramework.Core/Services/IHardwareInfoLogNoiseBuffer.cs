namespace SubZeroFramework.Services;

public interface IHardwareInfoLogNoiseBuffer
{
    IHardwareInfoNoiseCapture BeginCapture();
}

public interface IHardwareInfoNoiseCapture : IDisposable
{
    void SetDataPresent(bool dataPresent);
}

public sealed class NullHardwareInfoLogNoiseBuffer : IHardwareInfoLogNoiseBuffer
{
    public static NullHardwareInfoLogNoiseBuffer Instance { get; } = new();

    public IHardwareInfoNoiseCapture BeginCapture() => NullCapture.Instance;

    private sealed class NullCapture : IHardwareInfoNoiseCapture
    {
        public static NullCapture Instance { get; } = new();
        public void SetDataPresent(bool dataPresent) { }
        public void Dispose() { }
    }
}
