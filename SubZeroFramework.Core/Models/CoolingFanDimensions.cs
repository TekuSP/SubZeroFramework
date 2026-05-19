namespace SubZeroFramework.Models;

public sealed record CoolingFanDimensions
{
    public required double WidthMillimeters { get; init; }

    public required double HeightMillimeters { get; init; }

    public required double ThicknessMillimeters { get; init; }

    public bool IsCircular { get; init; }
}
