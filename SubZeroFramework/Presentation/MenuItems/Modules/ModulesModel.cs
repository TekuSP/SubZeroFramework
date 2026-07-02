using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;

using CommunityToolkit.Mvvm.ComponentModel;

using FrameworkDotnet.Enums;

using Material.Icons;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using SubZeroFramework.Controls.Modules;
using SubZeroFramework.Controls.Modules.Models;
using SubZeroFramework.Models;
using SubZeroFramework.Services;
using SubZeroFramework.Themes;

namespace SubZeroFramework.Presentation.MenuItems.Modules;

/// <summary>
/// View-model for the Modules page. The page renders a physical module/port map whose layout is chosen by the
/// chassis <see cref="FrameworkPlatformFamily"/>; this model owns the live data (module inventory joined with
/// the PD port stream by slot index) and the card/selection state the layout bodies bind to via
/// <see cref="ModulesAccessor"/>.
/// </summary>
public partial class ModulesModel : ObservableObject, IDisposable
{
    private readonly CompositeDisposable _subscriptions = [];
    private readonly Dictionary<int, ModuleSlotCardModel> _slotCardsByIndex = [];
    private readonly Dictionary<string, ModuleDeckCardModel> _deckCardsByKey = [];
    private readonly Dictionary<FrameworkModuleIdentity, ModuleInternalChipModel> _chipsByIdentity = [];
    private readonly Dictionary<FrameworkModuleIdentity, ModuleInternalChipModel> _detachedChipsByIdentity = [];
    // Presumed passive spacers (invisible to the EC, implied by the physical layout) — plain deck cards so
    // they render and select exactly like every other tile.
    private readonly ModuleDeckCardModel _presumedTopLeftSpacer = ModuleDeckCardModel.CreatePresumedSpacer(FrameworkInputModulePosition.TopRow0, isTouchpadSpacer: false);
    private readonly ModuleDeckCardModel _presumedTopRightSpacer = ModuleDeckCardModel.CreatePresumedSpacer(FrameworkInputModulePosition.TopRow4, isTouchpadSpacer: false);
    private readonly ModuleDeckCardModel _presumedTouchpadLeftSpacer = ModuleDeckCardModel.CreatePresumedSpacer(FrameworkInputModulePosition.Touchpad, isTouchpadSpacer: true);
    private readonly ModuleDeckCardModel _presumedTouchpadRightSpacer = ModuleDeckCardModel.CreatePresumedSpacer(FrameworkInputModulePosition.Touchpad, isTouchpadSpacer: true);

    public ModulesModel(
        IFrameworkStatusClient frameworkStatusClient,
        IModuleInventoryClient moduleInventoryClient,
        IPowerDeliveryClient powerDeliveryClient,
        IHardwareInfoClient hardwareInfoClient,
        SynchronizationContext synchronizationContext,
        ModulesAccessor accessor)
    {
        // Publish this instance for the navigation-resolved layout body VMs (see ModulesAccessor).
        accessor.Current = this;

        frameworkStatusClient
            .WatchStatus()
            .ObserveOn(synchronizationContext)
            .Subscribe(status => LastStatus = status)
            .DisposeWith(_subscriptions);

        moduleInventoryClient
            .WatchInventory()
            .ObserveOn(synchronizationContext)
            .Subscribe(inventory =>
            {
                Inventory = inventory;
                RebuildModuleCards();
            })
            .DisposeWith(_subscriptions);

        powerDeliveryClient
            .WatchPorts()
            .ObserveOn(synchronizationContext)
            .Subscribe(ports =>
            {
                PdPorts = ports;
                RebuildModuleCards();
            })
            .DisposeWith(_subscriptions);

        // The bay register only classifies the GPU vendor; the actual model name comes from HardwareInfo.
        hardwareInfoClient
            .WatchHardwareInfo()
            .ObserveOn(synchronizationContext)
            .Subscribe(snapshot => VideoControllerNames =
            [
                .. snapshot.Runtime.VideoControllers
                    .Select(controller => controller.Name ?? controller.Caption ?? controller.Description ?? string.Empty)
                    .Where(name => !string.IsNullOrWhiteSpace(name)),
            ])
            .DisposeWith(_subscriptions);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlatformFamilyDisplay))]
    [NotifyPropertyChangedFor(nameof(LayoutVisibility))]
    [NotifyPropertyChangedFor(nameof(PlaceholderVisibility))]
    public partial FrameworkSystemStatus? LastStatus { get; set; }

    /// <summary>The live module inventory from the service; null until the stream produces a value.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InventorySummaryDisplay))]
    [NotifyPropertyChangedFor(nameof(BayVisibility))]
    [NotifyPropertyChangedFor(nameof(BayName))]
    [NotifyPropertyChangedFor(nameof(BayLogoVendor))]
    public partial ModuleInventoryStatus? Inventory { get; set; }

    /// <summary>The live PD port state, joined onto the slot cards by slot index.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<PowerDeliveryPortStatus>? PdPorts { get; set; }

    /// <summary>The detected video controller names, used to resolve the bay GPU's model name.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BayName))]
    public partial IReadOnlyList<string> VideoControllerNames { get; set; } = [];

    /// <summary>Expansion-card slots on the left side of the chassis, mirrored like the physical machine.</summary>
    public ObservableCollection<ModuleSlotCardModel> LeftSlotCards { get; } = [];

    /// <summary>Expansion-card slots on the right side of the chassis.</summary>
    public ObservableCollection<ModuleSlotCardModel> RightSlotCards { get; } = [];

    /// <summary>The FW16 input-deck top row (module slots + keyboard), ordered by physical position.</summary>
    public ObservableCollection<ModuleDeckCardModel> DeckTopRowCards { get; } = [];

    /// <summary>The fixed internal device chips (webcam / fingerprint / touchscreen …).</summary>
    public ObservableCollection<ModuleInternalChipModel> InternalChips { get; } = [];

    /// <summary>Cards detected over USB whose physical slot the FFI could not resolve unambiguously.</summary>
    public ObservableCollection<ModuleInternalChipModel> DetachedChips { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetachedVisibility))]
    public partial int DetachedChipCount { get; set; }

    public Visibility DetachedVisibility => DetachedChipCount == 0 ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>The FW16 touchpad row (presumed spacer · touchpad · presumed spacer); empty when unreported.</summary>
    public ObservableCollection<ModuleDeckCardModel> TouchpadRowCards { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TouchpadRowVisibility))]
    public partial int TouchpadRowCardCount { get; set; }

    public Visibility TouchpadRowVisibility => TouchpadRowCardCount == 0 ? Visibility.Collapsed : Visibility.Visible;

    [ObservableProperty]
    public partial ModuleSlotCardModel? SelectedSlotCard { get; set; }

    [ObservableProperty]
    public partial ModuleDeckCardModel? SelectedDeckCard { get; set; }

    /// <summary>The hero shown in the "Selected expansion module" band — a slot card's hero, or the bay's.</summary>
    [ObservableProperty]
    public partial ModuleHeroModel? SelectedExpansionHero { get; set; }

    /// <summary>The fixed input cover's hero on non-modular chassis (FW12/13); null when unreported.</summary>
    [ObservableProperty]
    public partial ModuleHeroModel? InputCoverHero { get; set; }

    /// <summary>Whether the expansion-bay banner is the selected expansion module.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BayBorderBrush))]
    public partial bool IsBaySelected { get; set; }

    public Brush BayBorderBrush => IsBaySelected
        ? AppThemeBrushes.Get("StatusInfoBrush", AppThemeBrushes.StatusSuccessColor)
        : AppThemeBrushes.Get("SurfaceOutlineBrush", AppThemeBrushes.TextPrimaryColor);

    /// <summary>Detected chassis family, used as the placeholder caption until every per-platform map exists.</summary>
    public string PlatformFamilyDisplay =>
        LastStatus?.PlatformFamily?.ToString() ?? "Detecting device…";

    private bool HasLayoutForPlatform => LastStatus?.PlatformFamily
        is FrameworkPlatformFamily.Framework12
        or FrameworkPlatformFamily.Framework13
        or FrameworkPlatformFamily.Framework16
        or FrameworkPlatformFamily.FrameworkDesktop;

    public Visibility LayoutVisibility => HasLayoutForPlatform ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PlaceholderVisibility => HasLayoutForPlatform ? Visibility.Collapsed : Visibility.Visible;

    public Visibility BayVisibility => Inventory?.ExpansionBayModule is null ? Visibility.Collapsed : Visibility.Visible;

    public string BayName => DiscreteGpuName ?? FrameworkModuleDisplay.For(RefinedBayIdentity).DisplayName;

    /// <summary>The bay GPU's actual model name from HardwareInfo (e.g. "NVIDIA GeForce RTX 5070 Laptop GPU"),
    /// matched to the bay vendor; null falls back to the catalog classification.</summary>
    // FD0001 intentionally suppressed: only ever matches on data a Framework 16 reported.
#pragma warning disable FD0001
    private string? DiscreteGpuName => RefinedBayIdentity switch
    {
        FrameworkModuleIdentity.ExpansionBayNvidiaGpu => VideoControllerNames.FirstOrDefault(name =>
            name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || name.Contains("GeForce", StringComparison.OrdinalIgnoreCase)),
        FrameworkModuleIdentity.ExpansionBayAmdGpu => VideoControllerNames.FirstOrDefault(name =>
            name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) && name.Contains("RX", StringComparison.OrdinalIgnoreCase)),
        _ => null,
    };
#pragma warning restore FD0001

    /// <summary>Brand-logo vendor keyword for the bay banner (AMD / Nvidia GPU modules); null shows the glyph.</summary>
    public string? BayLogoVendor => FrameworkModuleDisplay.For(RefinedBayIdentity).LogoVendor;

    /// <summary>The bay identity, refined from the vendor/board classification when the descriptor is generic
    /// (the EC often reports "ExpansionBay" while the vendor register already says which module sits in it).</summary>
    // FD0001 intentionally suppressed: refinement only ever runs on data a Framework 16 reported.
#pragma warning disable FD0001
    private FrameworkModuleIdentity RefinedBayIdentity
    {
        get
        {
            if (Inventory?.ExpansionBayModule is not { } bayModule)
            {
                return FrameworkModuleIdentity.None;
            }

            if (bayModule.Identity is not FrameworkModuleIdentity.ExpansionBay and not FrameworkModuleIdentity.None)
            {
                return bayModule.Identity;
            }

            return Inventory.ExpansionBayVendor switch
            {
                FrameworkExpansionBayVendor.AmdGpu => FrameworkModuleIdentity.ExpansionBayAmdGpu,
                FrameworkExpansionBayVendor.NvidiaGpu => FrameworkModuleIdentity.ExpansionBayNvidiaGpu,
                FrameworkExpansionBayVendor.FanOnly => FrameworkModuleIdentity.ExpansionBayFanOnly,
                FrameworkExpansionBayVendor.SsdHolder => FrameworkModuleIdentity.ExpansionBaySsdHolder,
                FrameworkExpansionBayVendor.PcieAccessory => FrameworkModuleIdentity.ExpansionBayPcieAccessory,
                _ => Inventory.ExpansionBayBoard switch
                {
                    FrameworkExpansionBayBoard.DualInterposer => FrameworkModuleIdentity.ExpansionBayDualInterposer,
                    FrameworkExpansionBayBoard.SingleInterposer => FrameworkModuleIdentity.ExpansionBaySingleInterposer,
                    FrameworkExpansionBayBoard.UmaFans => FrameworkModuleIdentity.ExpansionBayUmaFans,
                    _ => bayModule.Identity,
                },
            };
        }
    }
#pragma warning restore FD0001

    /// <summary>One-line proof-of-life summary of the streamed inventory (placeholder caption).</summary>
    public string InventorySummaryDisplay
    {
        get
        {
            if (Inventory is not { } inventory)
            {
                return "Waiting for the module inventory stream…";
            }

            var bay = inventory.ExpansionBayModule is { } bayModule
                ? $" · expansion bay: {FrameworkModuleDisplay.For(bayModule.Identity).DisplayName}"
                : string.Empty;
            var populatedSlots = inventory.UsbCSlots.Count(slot => slot.IsPresent);
            return $"{inventory.UsbCSlots.Count} expansion slots ({populatedSlots} populated) · "
                + $"{inventory.InputDeckModules.Count} input-deck modules · "
                + $"{inventory.InternalModules.Count} internal devices{bay}";
        }
    }

    public void SelectSlot(ModuleSlotCardModel card)
    {
        foreach (var slotCard in _slotCardsByIndex.Values)
        {
            slotCard.IsSelected = ReferenceEquals(slotCard, card);
        }

        IsBaySelected = false;
        SelectedSlotCard = card;
        SelectedExpansionHero = card.Hero;
    }

    /// <summary>Selects the expansion-bay banner as the expansion module shown in the hero band.</summary>
    public void SelectBay()
    {
        foreach (var slotCard in _slotCardsByIndex.Values)
        {
            slotCard.IsSelected = false;
        }

        SelectedSlotCard = null;
        IsBaySelected = true;
        SelectedExpansionHero = BuildBayHero();
    }

    public void SelectDeckCard(ModuleDeckCardModel card)
    {
        foreach (var deckCard in _deckCardsByKey.Values
            .Concat([_presumedTopLeftSpacer, _presumedTopRightSpacer, _presumedTouchpadLeftSpacer, _presumedTouchpadRightSpacer]))
        {
            deckCard.IsSelected = ReferenceEquals(deckCard, card);
        }

        SelectedDeckCard = card;
    }

    // FD0001 intentionally suppressed: the deck grouping only ever matches what the device itself reported.
#pragma warning disable FD0001
    private void RebuildModuleCards()
    {
        if (Inventory is not { } inventory)
        {
            return;
        }

        var mainboardPorts = (PdPorts ?? [])
            .Where(port => port.PortSource == "Mainboard")
            .ToDictionary(port => port.SlotIndex);

        List<ModuleSlotCardModel> leftSlots = [];
        List<ModuleSlotCardModel> rightSlots = [];
        foreach (var slot in inventory.UsbCSlots.OrderBy(slot => slot.SlotIndex))
        {
            if (!_slotCardsByIndex.TryGetValue(slot.SlotIndex, out var card))
            {
                card = new ModuleSlotCardModel(slot.SlotIndex);
                _slotCardsByIndex[slot.SlotIndex] = card;
            }

            card.Update(slot, mainboardPorts.GetValueOrDefault(slot.SlotIndex));
            (card.IsLeftSide ? leftSlots : rightSlots).Add(card);
        }

        // The FW16 chassis has 6 expansion-card slots (3 per side); the EC only reports the 4 PD-capable
        // ports today, so the remaining physical slots render as "No data" ghosts until the FFI covers them.
        if (LastStatus?.PlatformFamily == FrameworkPlatformFamily.Framework16 && leftSlots.Count + rightSlots.Count > 0)
        {
            // Deterministic synthetic indices (reported max + 1 …) so rebuilds reuse the same placeholder cards.
            var nextSyntheticIndex = inventory.UsbCSlots.Max(slot => slot.SlotIndex) + 1;
            foreach (var side in new[] { leftSlots, rightSlots })
            {
                while (side.Count < 3)
                {
                    if (!_slotCardsByIndex.TryGetValue(nextSyntheticIndex, out var placeholder))
                    {
                        placeholder = new ModuleSlotCardModel(nextSyntheticIndex, isUnreported: true);
                        _slotCardsByIndex[nextSyntheticIndex] = placeholder;
                    }

                    side.Add(placeholder);
                    nextSyntheticIndex++;
                }
            }
        }

        // Physical numbering: left column 1..N top→bottom (Back → Front → unreported), right column continues.
        static int PositionRank(ModuleSlotCardModel card) => card.IsUnreported
            ? 2
            : card.PdPort?.PortPosition.Contains("Back", StringComparison.OrdinalIgnoreCase) == true ? 0 : 1;
        leftSlots = [.. leftSlots.OrderBy(PositionRank)];
        rightSlots = [.. rightSlots.OrderBy(PositionRank)];
        for (var index = 0; index < leftSlots.Count; index++)
        {
            leftSlots[index].SetDisplayNumber(index + 1);
        }

        for (var index = 0; index < rightSlots.Count; index++)
        {
            rightSlots[index].SetDisplayNumber(leftSlots.Count + index + 1);
        }

        SynchronizeCollection(LeftSlotCards, leftSlots);
        SynchronizeCollection(RightSlotCards, rightSlots);

        // The EC reports every MUX position; a physical module spanning several positions (the keyboard covers
        // most of the top row) repeats the same identity+IDs, so adjacent identical descriptors collapse into one.
        // Positions that are connected but expose NO USB identity hold passive spacers (user heuristic);
        // BoardId can differ per MUX position of the same physical module, so it is not part of the group key.
        List<(ModuleDescriptorStatus Module, bool IsSpacer)> groupedTopRow = [];
        ModuleDescriptorStatus? touchpadDescriptor = null;
        foreach (var module in inventory.InputDeckModules.OrderBy(module => module.Position))
        {
            if (module.SlotKind == FrameworkModuleSlotKind.InputDeckTouchpad || module.Position == FrameworkInputModulePosition.Touchpad)
            {
                touchpadDescriptor = module;
                continue;
            }

            var isSpacer = module is { VendorId: 0, ProductId: 0 };
            if (groupedTopRow.Count > 0
                && groupedTopRow[^1] is { } previous
                && previous.IsSpacer == isSpacer
                && previous.Module.Identity == module.Identity
                && previous.Module.VendorId == module.VendorId
                && previous.Module.ProductId == module.ProductId)
            {
                continue;
            }

            groupedTopRow.Add((module, isSpacer));
        }

        List<ModuleDeckCardModel> topRowCards = [];
        foreach (var (module, isSpacer) in groupedTopRow)
        {
            var key = $"{module.SlotKind}:{module.Position}:{module.SlotIndex}:{isSpacer}";
            if (!_deckCardsByKey.TryGetValue(key, out var card))
            {
                card = new ModuleDeckCardModel(module.Position);
                _deckCardsByKey[key] = card;
            }

            card.Update(module, isSpacer);
            topRowCards.Add(card);
        }

        // A lone top-row module implies passive spacers flanking it (spacers expose nothing to the EC).
        if (topRowCards.Count == 1)
        {
            topRowCards = [_presumedTopLeftSpacer, topRowCards[0], _presumedTopRightSpacer];
        }

        SynchronizeCollection(DeckTopRowCards, topRowCards);

        List<ModuleDeckCardModel> touchpadRowCards = [];
        if (touchpadDescriptor is { } touchpadModule)
        {
            var key = $"{touchpadModule.SlotKind}:{touchpadModule.Position}:{touchpadModule.SlotIndex}";
            if (!_deckCardsByKey.TryGetValue(key, out var card))
            {
                card = new ModuleDeckCardModel(touchpadModule.Position);
                _deckCardsByKey[key] = card;
            }

            card.Update(touchpadModule);
            // The standard touchpad is always flanked by passive touchpad spacers.
            touchpadRowCards = [_presumedTouchpadLeftSpacer, card, _presumedTouchpadRightSpacer];
        }

        SynchronizeCollection(TouchpadRowCards, touchpadRowCards);
        TouchpadRowCardCount = touchpadRowCards.Count;

        List<ModuleInternalChipModel> chips = [];
        foreach (var module in inventory.InternalModules)
        {
            if (!_chipsByIdentity.TryGetValue(module.Identity, out var chip))
            {
                chip = new ModuleInternalChipModel(module.Identity);
                _chipsByIdentity[module.Identity] = chip;
            }

            chip.Update(module);
            chips.Add(chip);
        }

        SynchronizeCollection(InternalChips, chips);

        // Cards detected over USB whose slot could not be resolved unambiguously (FFI "detached" bucket).
        List<ModuleInternalChipModel> detachedChips = [];
        foreach (var module in inventory.DetachedModules)
        {
            if (!_detachedChipsByIdentity.TryGetValue(module.Identity, out var chip))
            {
                chip = new ModuleInternalChipModel(module.Identity);
                _detachedChipsByIdentity[module.Identity] = chip;
            }

            chip.Update(module);
            detachedChips.Add(chip);
        }

        SynchronizeCollection(DetachedChips, detachedChips);
        DetachedChipCount = detachedChips.Count;
        EnsureSelections();

        // Refresh whichever expansion hero is showing with the just-updated data.
        SelectedExpansionHero = IsBaySelected ? BuildBayHero() : SelectedSlotCard?.Hero;
        InputCoverHero = BuildInputCoverHero();
    }

    /// <summary>Builds the fixed input cover hero (FW12/13) from the internal keyboard descriptor.</summary>
    private ModuleHeroModel? BuildInputCoverHero()
    {
        if (Inventory?.InternalModules.FirstOrDefault(module => module.Identity == FrameworkModuleIdentity.InternalKeyboard) is not { } keyboard)
        {
            return null;
        }

        List<ModuleHeroChipModel> chips = [];
        if (keyboard.Flags.HasFlag(FrameworkModuleFlags.BuiltIn))
        {
            chips.Add(new ModuleHeroChipModel(MaterialIconKind.Chip, "BuiltIn"));
        }

        if (keyboard.Flags.HasFlag(FrameworkModuleFlags.Connected))
        {
            chips.Add(new ModuleHeroChipModel(MaterialIconKind.Connection, "Connected"));
        }

        if (keyboard.Flags.HasFlag(FrameworkModuleFlags.Active))
        {
            chips.Add(new ModuleHeroChipModel(MaterialIconKind.Flash, "Active"));
        }

        return new ModuleHeroModel(
            MaterialIconKind.KeyboardOutline,
            LogoVendor: null,
            Title: "Input cover",
            ConfidenceLabel: FrameworkModuleDisplay.ConfidenceLabel(keyboard.Confidence),
            ShowConfidence: true,
            Chips: chips,
            PdPillText: null,
            Tiles:
            [
                new ModuleHeroTileModel(MaterialIconKind.KeyboardOutline, "Module", "Input cover"),
                new ModuleHeroTileModel(MaterialIconKind.Power, "State", FrameworkModuleDisplay.StateLabel(keyboard.Flags)),
                new ModuleHeroTileModel(MaterialIconKind.Sitemap, "Slot kind", FrameworkModuleDisplay.SlotKindLabel(FrameworkModuleSlotKind.InternalFixed)),
                new ModuleHeroTileModel(MaterialIconKind.ShieldCheck, "Confidence", FrameworkModuleDisplay.ConfidenceLabel(keyboard.Confidence)),
                new ModuleHeroTileModel(MaterialIconKind.Barcode, "Vendor ID", keyboard.VendorId == 0 ? "—" : $"0x{keyboard.VendorId:X4}"),
                new ModuleHeroTileModel(MaterialIconKind.Barcode, "Product ID", keyboard.ProductId == 0 ? "—" : $"0x{keyboard.ProductId:X4}"),
                new ModuleHeroTileModel(MaterialIconKind.Pound, "Board ID", keyboard.BoardId < 0 ? "—" : keyboard.BoardId.ToString()),
            ]);
    }

    /// <summary>Builds the expansion-bay hero: GPU model (from HardwareInfo), interposer board, serial, flags.</summary>
    // FD0001 intentionally suppressed: only ever built from data a Framework 16 reported.
#pragma warning disable FD0001
    private ModuleHeroModel? BuildBayHero()
    {
        if (Inventory is not { ExpansionBayModule: { } bayModule } inventory)
        {
            return null;
        }

        var info = FrameworkModuleDisplay.For(RefinedBayIdentity);

        List<ModuleHeroChipModel> chips = [];
        if (bayModule.Flags.HasFlag(FrameworkModuleFlags.Connected))
        {
            chips.Add(new ModuleHeroChipModel(MaterialIconKind.Connection, "Connected"));
        }

        if (bayModule.Flags.HasFlag(FrameworkModuleFlags.Active))
        {
            chips.Add(new ModuleHeroChipModel(MaterialIconKind.Flash, "Active"));
        }

        if (bayModule.Flags.HasFlag(FrameworkModuleFlags.Enabled))
        {
            chips.Add(new ModuleHeroChipModel(MaterialIconKind.Power, "Enabled"));
        }

        if (bayModule.Flags.HasFlag(FrameworkModuleFlags.DoorClosed))
        {
            chips.Add(new ModuleHeroChipModel(MaterialIconKind.DoorClosed, "Door closed"));
        }

        var boardLabel = inventory.ExpansionBayBoard switch
        {
            FrameworkExpansionBayBoard.DualInterposer => "Dual interposer",
            FrameworkExpansionBayBoard.SingleInterposer => "Single interposer",
            FrameworkExpansionBayBoard.UmaFans => "UMA fan shim",
            FrameworkExpansionBayBoard.NoModule => "No module",
            FrameworkExpansionBayBoard.BadConnection => "Bad connection",
            _ => "Unknown",
        };

        List<ModuleHeroTileModel> tiles =
        [
            new(ModuleArt.ResolveIcon(info.IconName), "Module", info.DisplayName),
        ];
        if (DiscreteGpuName is { } gpuName)
        {
            tiles.Add(new ModuleHeroTileModel(MaterialIconKind.ExpansionCard, "GPU", gpuName));
        }

        tiles.Add(new ModuleHeroTileModel(MaterialIconKind.DeveloperBoard, "Interposer board", boardLabel));
        tiles.Add(new ModuleHeroTileModel(MaterialIconKind.Sitemap, "Slot kind", FrameworkModuleDisplay.SlotKindLabel(FrameworkModuleSlotKind.ExpansionBay)));
        tiles.Add(new ModuleHeroTileModel(MaterialIconKind.ShieldCheck, "Confidence", FrameworkModuleDisplay.ConfidenceLabel(bayModule.Confidence)));
        tiles.Add(new ModuleHeroTileModel(MaterialIconKind.Pound, "Serial", string.IsNullOrWhiteSpace(inventory.ExpansionBaySerialNumber) ? "—" : inventory.ExpansionBaySerialNumber));

        return new ModuleHeroModel(
            ModuleArt.ResolveIcon(info.IconName),
            info.LogoVendor,
            Title: BayName,
            ConfidenceLabel: FrameworkModuleDisplay.ConfidenceLabel(bayModule.Confidence),
            ShowConfidence: true,
            Chips: chips,
            PdPillText: null,
            Tiles: tiles);
    }
#pragma warning restore FD0001

    // FD0001 intentionally suppressed: the keyboard preference only ever matches on a Framework 16.
#pragma warning disable FD0001
    private void EnsureSelections()
    {
        // The bay counts as the selected expansion module — don't let the slot default steal the selection.
        if (!IsBaySelected
            && (SelectedSlotCard is null || !LeftSlotCards.Contains(SelectedSlotCard) && !RightSlotCards.Contains(SelectedSlotCard)))
        {
            var firstSlot = LeftSlotCards.Concat(RightSlotCards).FirstOrDefault(card => !card.IsEmpty)
                ?? LeftSlotCards.Concat(RightSlotCards).FirstOrDefault();
            if (firstSlot is not null)
            {
                SelectSlot(firstSlot);
            }
        }

        if (SelectedDeckCard is null
            || !DeckTopRowCards.Contains(SelectedDeckCard) && !TouchpadRowCards.Contains(SelectedDeckCard))
        {
            var preferredDeckCard = DeckTopRowCards.FirstOrDefault(card => !card.IsSpacer && card.Descriptor?.Identity == FrameworkModuleIdentity.Framework16KeyboardModule)
                ?? DeckTopRowCards.FirstOrDefault(card => !card.IsSpacer)
                ?? DeckTopRowCards.FirstOrDefault()
                ?? TouchpadRowCards.FirstOrDefault(card => !card.IsSpacer);
            if (preferredDeckCard is not null)
            {
                SelectDeckCard(preferredDeckCard);
            }
        }
    }
#pragma warning restore FD0001

    private static void SynchronizeCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> desired)
        where T : class
    {
        if (target.Count == desired.Count && !target.Where((item, index) => !ReferenceEquals(item, desired[index])).Any())
        {
            return;
        }

        target.Clear();
        foreach (var item in desired)
        {
            target.Add(item);
        }
    }

    public void Dispose() => _subscriptions.Dispose();
}
