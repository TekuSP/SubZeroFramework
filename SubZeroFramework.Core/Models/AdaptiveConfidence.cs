namespace SubZeroFramework.Models;

/// <summary>
/// How well the controller knows a fan, as a state the UI can speak plainly about.
/// </summary>
/// <remarks>
/// <para>
/// Three states rather than a percentage, deliberately. A completion metric invites the reading that the fan
/// is only partly working until the bar fills, which is false — a fan on conservative defaults is a working
/// fan, just a slightly less clever one. These names describe what the CONTROLLER knows, not how finished
/// anything is.
/// </para>
/// <para>
/// None of the three is a fault, and the UI must not render any of them as a warning. The genuinely bad
/// cases — a model contradicted by the machine, a gain pinned at its bound — are separate signals, not the
/// bottom of this scale.
/// </para>
/// </remarks>
public enum AdaptiveConfidence
{
    /// <summary>
    /// Running on safe defaults; the fit is not yet separable.
    /// </summary>
    /// <remarks>
    /// What every fan looks like on its first day. The user should read this as "it is getting to know my
    /// machine", never as "it is not ready".
    /// </remarks>
    Learning = 0,

    /// <summary>
    /// The controller has its own model of this fan and is still refining it.
    /// </summary>
    /// <remarks>
    /// Already better than the defaults. Quiet progress, not a warning.
    /// </remarks>
    Converging = 1,

    /// <summary>
    /// The model has been stable across many settled periods. Nothing for the user to do.
    /// </summary>
    Confident = 2,
}
