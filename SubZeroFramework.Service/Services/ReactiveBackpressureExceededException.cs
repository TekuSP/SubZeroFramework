namespace SubZeroFramework.Service.Services;

internal sealed class ReactiveBackpressureExceededException : InvalidOperationException
{
    public ReactiveBackpressureExceededException(string message)
        : base(message)
    {
    }
}
