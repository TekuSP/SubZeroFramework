using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using FrameworkDotnet.Enums;

using SubZeroFramework.Controls.FanCurveProfiles.Models;
using SubZeroFramework.Controls.Fans.Models;

namespace SubZeroFramework.Presentation.MenuItems.FanCurveProfiles;

/// <summary>
/// The "Applies to" link group for the selected fan: which fans share its curve and apply together. Owns the
/// link chips and the per-leader link sets; reads the selected fan and fleet from its parent coordinator. The
/// parent drives it (rebuild on selection change, update on telemetry) and queries <see cref="GetLinkedPartners"/>
/// when it fans a curve out on Apply.
/// </summary>
public partial class FanLinkSectionModel : ObservableObject
{
    private readonly FanCurveProfilesModel _parent;
    private readonly ObservableCollection<FanLinkChip> _linkChips = [];

    // Pending (not-yet-applied) link overrides on top of the persisted service state. Keyed by fan index; the
    // value is the staged leader (null = staged-unlinked). The grouping the UI shows is the staged override when
    // present, else the fan's persisted control-state LinkedLeaderIndex. Flushed to the service on Apply,
    // discarded on Revert — so linking shows immediately but is only saved when the user commits.
    private readonly Dictionary<int, int?> _stagedLinks = [];

    public FanLinkSectionModel(FanCurveProfilesModel parent)
    {
        _parent = parent;
        LinkChips = new ReadOnlyObservableCollection<FanLinkChip>(_linkChips);
    }

    /// <summary>The "Applies to" chips — one per fan; the edited fan is locked in, others toggle into the group.</summary>
    public ReadOnlyObservableCollection<FanLinkChip> LinkChips { get; }

    /// <summary>Plain-language summary under the link chips.</summary>
    [ObservableProperty]
    public partial string LinkSummaryText { get; set; } = string.Empty;

    /// <summary>True when at least one other fan is linked, so the action becomes "Only this fan" (else "Link all").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LinkAllVisibility))]
    [NotifyPropertyChangedFor(nameof(OnlyThisVisibility))]
    public partial bool HasLinkedPartners { get; set; }

    public Microsoft.UI.Xaml.Visibility LinkAllVisibility =>
        HasLinkedPartners ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    public Microsoft.UI.Xaml.Visibility OnlyThisVisibility =>
        HasLinkedPartners ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    /// <summary>The fans this leader's curve also applies to (always includes the leader). Used by Apply fan-out.</summary>
    public IReadOnlyCollection<int> GetLinkedPartners(int leader) => GetLinkSet(leader);

    /// <summary>True when there are pending link changes not yet saved to the service (cleared on Apply / Revert).</summary>
    public bool HasStagedLinks => _stagedLinks.Count > 0;

    // The leader this fan effectively follows right now: the staged override if one exists, else the persisted
    // service value.
    private int? EffectiveLeader(FanCardModel fan) =>
        _stagedLinks.TryGetValue(fan.Snapshot.FanIndex, out var staged) ? staged : fan.ControlState?.LinkedLeaderIndex;

    /// <summary>
    /// The leader this fan effectively follows (staged override or persisted), or null. Lets the coordinator keep
    /// a linked partner non-selectable even before the link is applied.
    /// </summary>
    public int? EffectiveLeaderOf(FanCardModel fan) => EffectiveLeader(fan);

    // The leader plus every fan that effectively follows it (staged overrides applied).
    private HashSet<int> GetLinkSet(int leader)
    {
        var set = new HashSet<int> { leader };
        foreach (var fan in _parent.Fans)
        {
            if (EffectiveLeader(fan) == leader)
            {
                set.Add(fan.Snapshot.FanIndex);
            }
        }

        return set;
    }

    // Stages a link change client-side (no service write) — pruned when it matches the persisted value so it
    // stops counting as a pending change.
    private void StageLink(int fanIndex, int? leaderIndex)
    {
        var serviceLeader = FindFan(fanIndex)?.ControlState?.LinkedLeaderIndex;
        if (leaderIndex == serviceLeader)
        {
            _stagedLinks.Remove(fanIndex);
        }
        else
        {
            _stagedLinks[fanIndex] = leaderIndex;
        }

        _parent.OnStagedLinksChanged();
    }

    /// <summary>Persists all staged link changes to the service (called from Apply), then clears the overlay.</summary>
    public async Task FlushStagedLinksAsync(CancellationToken cancellationToken)
    {
        if (_stagedLinks.Count == 0)
        {
            return;
        }

        // Unstage each link only after a successful persist — clearing up front silently discarded
        // staged links whenever the write failed (same defect as the boost overlay; see
        // FanBoostSectionModel.FlushStagedBoostsAsync). Failures stay staged and retry on the next Apply.
        foreach (var (fanIndex, leaderIndex) in _stagedLinks.ToArray())
        {
            if (await _parent.PersistFanLinkAsync(fanIndex, leaderIndex, cancellationToken).ConfigureAwait(true))
            {
                _stagedLinks.Remove(fanIndex);
            }
        }

        _parent.OnStagedLinksChanged();
        RebuildLinkChips();
    }

    /// <summary>Discards pending link changes (called from Revert), reverting the UI to the persisted state.</summary>
    public void DiscardStagedLinks()
    {
        if (_stagedLinks.Count == 0)
        {
            return;
        }

        _stagedLinks.Clear();
        RebuildLinkChips();
    }

    /// <summary>Rebuilds the link chips for the selected fan (locked self, linked/available others, disabled stalled).</summary>
    public void RebuildLinkChips()
    {
        _linkChips.Clear();

        if (_parent.SelectedFan is not { } current)
        {
            LinkSummaryText = string.Empty;
            return;
        }

        var leader = current.Snapshot.FanIndex;
        var linkSet = GetLinkSet(leader);

        foreach (var fan in _parent.Fans.OrderBy(static f => f.Snapshot.FanIndex))
        {
            var index = fan.Snapshot.FanIndex;
            var stalled = fan.FanState?.FanState == FrameworkFanState.Stalled;

            _linkChips.Add(new FanLinkChip(index)
            {
                DisplayName = fan.Snapshot.DisplayName,
                IsCurrent = index == leader,
                IsStalled = stalled,
                IsLinked = linkSet.Contains(index),
            });
        }

        RefreshLinkSummary();
    }

    /// <summary>Updates chip names/stalled flags in place as telemetry arrives, without disrupting selection.</summary>
    public void UpdateLinkChipStates()
    {
        if (_linkChips.Count == 0)
        {
            return;
        }

        // Re-derive linked state from the (possibly just-streamed) persisted control-state so a link written by
        // this or another client is reflected without a full chip rebuild.
        var linkSet = _parent.SelectedFan is { } current ? GetLinkSet(current.Snapshot.FanIndex) : null;

        foreach (var chip in _linkChips)
        {
            if (FindFan(chip.FanIndex) is { } fan)
            {
                chip.DisplayName = fan.Snapshot.DisplayName;
                chip.IsStalled = fan.FanState?.FanState == FrameworkFanState.Stalled;
            }

            if (linkSet is not null)
            {
                chip.IsLinked = linkSet.Contains(chip.FanIndex);
            }
        }

        RefreshLinkSummary();
    }

    /// <summary>No-op: group membership is derived from persisted control-state, so a departed fan drops out automatically.</summary>
    public void RemoveFanFromSets(int fanIndex)
    {
    }

    private void RefreshLinkSummary()
    {
        if (_parent.SelectedFan is not { } current)
        {
            LinkSummaryText = string.Empty;
            return;
        }

        var leader = current.Snapshot.FanIndex;
        var linkSet = GetLinkSet(leader);
        var count = linkSet.Count(index => FindFan(index) is not null);

        HasLinkedPartners = count > 1;
        LinkSummaryText = count <= 1
            ? $"Only {current.Snapshot.DisplayName} — changes apply to this fan alone."
            : $"{count} fans linked — they share this curve and apply together.";

        RefreshFanLinkStates();
    }

    /// <summary>
    /// Reflects link intent onto the fleet's master-list rows: a fan that is in another fan's link group (as a
    /// non-leader) is a linked partner — its row is disabled and it shows which leader it follows. Idempotent, so
    /// it can run on every telemetry tick via <see cref="UpdateLinkChipStates"/> without churn.
    /// </summary>
    private void RefreshFanLinkStates()
    {
        foreach (var fan in _parent.Fans)
        {
            // A fan's leader is its staged override if any, else its persisted control-state LinkedLeaderIndex.
            var leader = EffectiveLeader(fan);
            fan.IsLinkedPartner = leader is not null;
            fan.LinkedLeaderName = leader is { } leaderIndex ? FindFan(leaderIndex)?.Snapshot.DisplayName : null;
        }
    }

    private FanCardModel? FindFan(int fanIndex)
    {
        foreach (var fan in _parent.Fans)
        {
            if (fan.Snapshot.FanIndex == fanIndex)
            {
                return fan;
            }
        }

        return null;
    }

    [RelayCommand]
    private void ToggleLink(FanLinkChip? chip)
    {
        if (chip is null || !chip.IsToggleEnabled || _parent.SelectedFan is not { } current)
        {
            return;
        }

        var leader = current.Snapshot.FanIndex;
        var nowLinked = !chip.IsLinked;
        chip.IsLinked = nowLinked; // immediate UI; staged client-side and saved only on Apply
        StageLink(chip.FanIndex, nowLinked ? leader : null);
        RefreshLinkSummary();
    }

    [RelayCommand]
    private void LinkAllFans()
    {
        if (_parent.SelectedFan is not { } current)
        {
            return;
        }

        var leader = current.Snapshot.FanIndex;
        foreach (var chip in _linkChips)
        {
            if (chip.IsStalled || chip.IsCurrent || chip.IsLinked)
            {
                continue;
            }

            chip.IsLinked = true;
            StageLink(chip.FanIndex, leader);
        }

        RefreshLinkSummary();
    }

    [RelayCommand]
    private void OnlyThisFan()
    {
        if (_parent.SelectedFan is null)
        {
            return;
        }

        foreach (var chip in _linkChips)
        {
            if (chip.IsCurrent || !chip.IsLinked)
            {
                continue;
            }

            chip.IsLinked = false;
            StageLink(chip.FanIndex, null);
        }

        RefreshLinkSummary();
    }
}
