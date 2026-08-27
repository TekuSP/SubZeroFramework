using CommunityToolkit.Mvvm.ComponentModel;

namespace SubZeroFramework.Controls.Fans.Models;

public abstract class FanAdvancedInfoCardModel : ObservableObject
{
	/// <summary>
	/// Re-renders this card in the newly chosen display units. Overrides reassign their composite strings
	/// (dimensions, ranges, "~" approximations) and then call base, whose null-named raise re-runs every
	/// UnitFormatConverter binding on the card — the canonical single quantities are formatted there.
	/// </summary>
	public virtual void RefreshUnitFormatting() => OnPropertyChanged(propertyName: null);
}
