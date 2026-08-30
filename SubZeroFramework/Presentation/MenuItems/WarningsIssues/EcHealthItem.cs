using Microsoft.UI.Xaml.Controls;

namespace SubZeroFramework.Presentation.MenuItems.WarningsIssues;

/// <summary>
/// One condition the embedded controller is reporting about itself, ready to render.
/// </summary>
/// <remarks>
/// <para>
/// Only conditions that are actually TRUE become items. The page lists what is wrong rather than a checklist
/// of everything that could be, so a healthy machine shows an empty state instead of a wall of green ticks
/// nobody reads.
/// </para>
/// <para>
/// A record so the list can be rebuilt on every status push and compared by value — handing an ItemsRepeater
/// a fresh list of equal items would tear down and re-create every row several times a second.
/// </para>
/// </remarks>
/// <param name="Title">The condition, in the user's terms.</param>
/// <param name="Detail">What it means for them, in one sentence.</param>
/// <param name="Severity">Drives icon and accent; reuses the page's existing severity vocabulary.</param>
public sealed record EcHealthItem(string Title, string Detail, InfoBarSeverity Severity);
