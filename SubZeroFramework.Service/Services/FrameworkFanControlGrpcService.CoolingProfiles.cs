using System.Collections.Immutable;

using FrameworkDotnet.Enums;

using Grpc.Core;

using SubZeroFramework.GrpcContracts;
using SubZeroFramework.Models;

namespace SubZeroFramework.Service.Services;

/// <summary>
/// The cooling profile half of the fan control service.
/// </summary>
/// <remarks>
/// A partial rather than a new service so profiles arrive on the same channel, with the same authorization
/// and the same calibration guard, as every other fan command — and in its own file because the main one is
/// already long enough to be hard to hold in mind.
/// </remarks>
public sealed partial class FrameworkFanControlGrpcService
{
    public override Task<CoolingProfileOperationReply> SaveCoolingProfile(SaveCoolingProfileRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            _authorizationService.EnsureCommandAccess();

            if (request.Profile is null || string.IsNullOrWhiteSpace(request.Profile.Id))
            {
                return Task.FromResult(Failed(request.Profile?.Id ?? string.Empty, "A profile needs an id."));
            }

            if (string.IsNullOrWhiteSpace(request.Profile.Name))
            {
                return Task.FromResult(Failed(request.Profile.Id, "A profile needs a name."));
            }

            var profile = CoolingProfileProtoMapper.ToProfile(request.Profile);
            _coolingProfileStore.Save(profile);

            _logger.LogInformation("Saved cooling profile {ProfileId} ({ProfileName}).", profile.Id, profile.Name);
            return Task.FromResult(Succeeded(profile.Id));
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected SaveCoolingProfile because the service was not in a writable state.");
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override async Task<CoolingProfileOperationReply> DeleteCoolingProfile(DeleteCoolingProfileRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            _authorizationService.EnsureCommandAccess();
            _coolingProfileStore.Delete(request.ProfileId);

            _logger.LogInformation("Deleted cooling profile {ProfileId}.", request.ProfileId);

            // Deleting the last profile puts the baseline back and selects it. The store only stores, so
            // APPLYING it is this layer's job — otherwise the fans would carry on doing whatever the deleted
            // profile left them doing while the shelf claimed the machine was on Default.
            if (_coolingProfileStore.ActiveProfileId is { } restoredId
                && _coolingProfileStore.Find(restoredId) is { } restored)
            {
                await CoolingProfileApplier
                    .ApplyAsync(restored, new ProfileCommandTarget(this), context.CancellationToken)
                    .ConfigureAwait(false);
            }

            return Succeeded(request.ProfileId);
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected DeleteCoolingProfile because the service was not in a writable state.");
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    public override Task<CoolingProfileOperationReply> RenameCoolingProfile(RenameCoolingProfileRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            _authorizationService.EnsureCommandAccess();

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Task.FromResult(Failed(request.ProfileId, "A profile needs a name."));
            }

            return Task.FromResult(_coolingProfileStore.Rename(request.ProfileId, request.Name)
                ? Succeeded(request.ProfileId)
                : Failed(request.ProfileId, "That profile no longer exists."));
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected RenameCoolingProfile because the service was not in a writable state.");
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    /// <summary>Applies a profile to every fan and records it as the selection.</summary>
    /// <remarks>
    /// An empty profile id DESELECTS: it records that no profile is in effect without touching a single fan,
    /// which is what the shell needs to drop its tint back to black.
    /// </remarks>
    public override async Task<CoolingProfileOperationReply> SetActiveCoolingProfile(SetActiveCoolingProfileRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            _authorizationService.EnsureCommandAccess();

            if (string.IsNullOrWhiteSpace(request.ProfileId))
            {
                _coolingProfileStore.SetActive(null);
                _logger.LogInformation("Cleared the active cooling profile.");
                return Succeeded(string.Empty);
            }

            var profile = _coolingProfileStore.Find(request.ProfileId);
            if (profile is null)
            {
                return Failed(request.ProfileId, "That profile no longer exists.");
            }

            var failed = await CoolingProfileApplier
                .ApplyAsync(profile, new ProfileCommandTarget(this), context.CancellationToken)
                .ConfigureAwait(false);

            // Recorded even on a PARTIAL apply: the user did choose this profile, and the client's own drift
            // detection will show it as modified on its own. Refusing to record it would leave the shell
            // naming nothing at all on a machine that mostly did take the profile.
            _coolingProfileStore.SetActive(profile.Id);

            _logger.LogInformation(
                "Applied cooling profile {ProfileId} ({ProfileName}). {FailedCount} fan(s) refused.",
                profile.Id,
                profile.Name,
                failed.Length);

            var reply = new CoolingProfileOperationReply
            {
                ProfileId = profile.Id,
                Succeeded = failed.IsEmpty,
                Message = failed.IsEmpty ? string.Empty : "Some fans did not accept this profile.",
            };

            reply.FailedFanNames.AddRange(failed);
            return reply;
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogWarning(exception, "Rejected SetActiveCoolingProfile because the service was not in a writable state.");
            throw new RpcException(new Status(StatusCode.FailedPrecondition, exception.Message));
        }
    }

    private static CoolingProfileOperationReply Succeeded(string profileId)
        => new() { ProfileId = profileId, Succeeded = true, Message = string.Empty };

    private static CoolingProfileOperationReply Failed(string profileId, string message)
        => new() { ProfileId = profileId, Succeeded = false, Message = message };

    /// <summary>
    /// Applies a profile's entries through the same paths the individual fan commands use.
    /// </summary>
    /// <remarks>
    /// Nested so it can reach the service's data provider, state store and persistence helper without any of
    /// them becoming public. Every method mirrors the corresponding RPC handler rather than reimplementing
    /// it, so a profile can never put a fan into a state a direct command could not.
    /// </remarks>
    private sealed class ProfileCommandTarget(FrameworkFanControlGrpcService owner) : IFanCommandTarget
    {
        public bool Exists(int fanIndex) => owner._fanControlStateStore.GetState(fanIndex) is not null;

        public string DisplayName(int fanIndex)
            => owner._fanControlStateStore.GetState(fanIndex)?.DisplayName ?? $"Fan {fanIndex}";

        public async Task<bool> TrySetAutoAsync(int fanIndex, CancellationToken cancellationToken)
        {
            try
            {
                owner.EnsureNotCalibrating(fanIndex);
                await owner._frameworkDataProvider.RestoreAutoFanControlAsync(fanIndex, cancellationToken).ConfigureAwait(false);
                owner._fanControlStateStore.MarkAuto(fanIndex);
                await owner.PersistFanControlStateAsync(fanIndex, preview: false, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (Exception exception) when (IsFanRefusal(exception))
            {
                owner._logger.LogWarning(exception, "Fan {FanIndex} refused Auto while applying a cooling profile.", fanIndex);
                return false;
            }
        }

        public async Task<bool> TrySetMaxAsync(int fanIndex, CancellationToken cancellationToken)
        {
            try
            {
                owner.EnsureNotCalibrating(fanIndex);
                var result = await owner._frameworkDataProvider.SetFanDutyAsync(fanIndex, 100d, cancellationToken).ConfigureAwait(false);
                owner._fanControlStateStore.MarkMax(fanIndex);
                owner._fanControlStateStore.RecordAppliedDuty(fanIndex, result.AppliedDutyPercent);
                await owner.PersistFanControlStateAsync(fanIndex, preview: false, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (Exception exception) when (IsFanRefusal(exception))
            {
                owner._logger.LogWarning(exception, "Fan {FanIndex} refused Max while applying a cooling profile.", fanIndex);
                return false;
            }
        }

        public async Task<bool> TrySetDutyAsync(int fanIndex, double dutyPercent, CancellationToken cancellationToken)
        {
            try
            {
                owner.EnsureNotCalibrating(fanIndex);
                var result = await owner._frameworkDataProvider.SetFanDutyAsync(fanIndex, dutyPercent, cancellationToken).ConfigureAwait(false);
                owner._fanControlStateStore.MarkManual(fanIndex);
                owner._fanControlStateStore.RecordAppliedDuty(fanIndex, result.AppliedDutyPercent);
                await owner.PersistFanControlStateAsync(fanIndex, preview: false, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (Exception exception) when (IsFanRefusal(exception))
            {
                owner._logger.LogWarning(exception, "Fan {FanIndex} refused a fixed duty while applying a cooling profile.", fanIndex);
                return false;
            }
        }

        /// <summary>
        /// Puts the fan on its adaptive loop at the profile's target.
        /// </summary>
        /// <remarks>
        /// Reuses the fan's OWN driving sensors and aggregation, because the profile deliberately does not
        /// carry them: which sensors drive a fan is a property of the hardware, not of the profile, and
        /// overwriting them here would silently undo work done on the fan detail page. An uncalibrated fan
        /// refuses, which is a real refusal the user should see rather than a crash.
        /// </remarks>
        public async Task<bool> TrySetAdaptiveAsync(
            int fanIndex,
            double targetCelsius,
            IReadOnlyList<int> drivingSensorIndices,
            TemperatureAggregationMode aggregation,
            CancellationToken cancellationToken)
        {
            try
            {
                owner.EnsureNotCalibrating(fanIndex);

                var existing = owner._fanControlStateStore.GetState(fanIndex);
                if (existing is null)
                {
                    return false;
                }

                var settings = existing.AdaptiveSettings with { TargetTemperatureCelsius = targetCelsius };

                // The PROFILE'S sensors, falling back to the fan's own. Auto and Max clear a fan's sensor
                // list, so after switching through one of those the fan has none — and arming Adaptive with
                // no sensors is refused, which is what left both fans on Auto reporting a partial apply.
                var sensors = drivingSensorIndices.Count > 0
                    ? [.. drivingSensorIndices]
                    : existing.DrivingSensorIndices;

                var result = owner._fanControlStateStore.SetAdaptiveMode(
                    fanIndex,
                    [.. sensors],
                    aggregation,
                    settings);

                if (!result.Succeeded)
                {
                    owner._logger.LogWarning(
                        "Fan {FanIndex} refused Adaptive while applying a cooling profile: {Message}",
                        fanIndex,
                        result.Message);
                    return false;
                }

                await owner.PersistFanControlStateAsync(fanIndex, preview: false, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (Exception exception) when (IsFanRefusal(exception))
            {
                owner._logger.LogWarning(exception, "Fan {FanIndex} refused Adaptive while applying a cooling profile.", fanIndex);
                return false;
            }
        }

        /// <summary>Drives the fan from the curve the profile carries.</summary>
        /// <remarks>
        /// Written into the RESERVED slot, never the active one. <c>SetCustomCurve</c> saves wherever the fan
        /// happens to be pointing, so applying a profile through it would overwrite a curve the user built.
        /// </remarks>
        public async Task<bool> TrySetCurveAsync(
            int fanIndex,
            IReadOnlyDictionary<int, double> points,
            TemperatureAggregationMode aggregation,
            IReadOnlyList<int> drivingSensorIndices,
            CancellationToken cancellationToken)
        {
            try
            {
                owner.EnsureNotCalibrating(fanIndex);

                var existing = owner._fanControlStateStore.GetState(fanIndex);
                if (existing is null || points.Count == 0)
                {
                    return false;
                }

                // As in TrySetAdaptiveAsync: the profile's sensors first, the fan's own only as a fallback.
                var sensors = drivingSensorIndices.Count > 0
                    ? [.. drivingSensorIndices]
                    : existing.DrivingSensorIndices;

                owner._fanControlStateStore.SaveCurveProfile(
                    fanIndex,
                    FrameworkFanControlStateStore.ReservedProfileSlot,
                    name: "Cooling profile",
                    curvePoints: points,
                    aggregationMode: aggregation,
                    drivingSensorIndices: [.. sensors],
                    followFanIndex: null,
                    activate: true);

                await owner.PersistFanControlStateAsync(fanIndex, preview: false, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (Exception exception) when (IsFanRefusal(exception))
            {
                owner._logger.LogWarning(exception, "Fan {FanIndex} refused a curve while applying a cooling profile.", fanIndex);
                return false;
            }
        }

        /// <summary>
        /// Whether an exception is one fan declining, rather than something the whole apply should die on.
        /// </summary>
        /// <remarks>
        /// Narrow on purpose. A calibration in progress, a bad argument, or a fan that is not writable are all
        /// "this fan said no" and the rest of the profile should still land. Anything else is a real fault and
        /// must not be swallowed into a quiet "some fans did not accept this profile".
        /// </remarks>
        private static bool IsFanRefusal(Exception exception)
            => exception is InvalidOperationException or ArgumentException or TimeoutException;
    }
}

/// <summary>Translates cooling profiles between the wire and the model.</summary>
public static class CoolingProfileProtoMapper
{
    public static CoolingProfileReply ToReply(CoolingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var reply = new CoolingProfileReply
        {
            Id = profile.Id,
            Name = profile.Name,
            IconName = profile.IconName ?? string.Empty,
            IsSeeded = profile.IsSeeded,
        };

        if (profile.AccentColorArgb is { } accent)
        {
            reply.AccentColorArgb = accent;
        }

        foreach (var entry in profile.Fans)
        {
            var entryReply = new CoolingProfileFanEntryReply
            {
                FanIndex = entry.FanIndex,
                Mode = ToModeValue(entry.Mode),
                DutyPercent = entry.DutyPercent,
                AdaptiveTargetCelsius = entry.AdaptiveTargetCelsius,
                Aggregation = ToAggregationValue(entry.Aggregation),
            };

            foreach (var point in entry.CurvePoints)
            {
                entryReply.CurvePoints.Add(new FanCurvePointReply
                {
                    TemperatureCelsius = point.Key,
                    FanDutyPercent = point.Value,
                });
            }

            entryReply.DrivingSensorIndices.AddRange(entry.DrivingSensorIndices);

            reply.Fans.Add(entryReply);
        }

        return reply;
    }

    public static CoolingProfile ToProfile(CoolingProfileReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        return new CoolingProfile
        {
            Id = reply.Id,
            Name = reply.Name,
            IconName = string.IsNullOrWhiteSpace(reply.IconName) ? null : reply.IconName,
            AccentColorArgb = reply.HasAccentColorArgb ? reply.AccentColorArgb : null,
            IsSeeded = reply.IsSeeded,
            Fans = [.. reply.Fans.Select(static entry => new CoolingProfileFanEntry
            {
                FanIndex = entry.FanIndex,
                Mode = ToMode(entry.Mode),
                DutyPercent = entry.DutyPercent,
                AdaptiveTargetCelsius = entry.AdaptiveTargetCelsius,
                Aggregation = ToAggregation(entry.Aggregation),

                // Last value wins on a duplicated temperature. A malformed client should not be able to make
                // the service throw out of a mapping call.
                CurvePoints = entry.CurvePoints
                    .GroupBy(static point => point.TemperatureCelsius)
                    .ToImmutableSortedDictionary(
                        static group => group.Key,
                        static group => group.Last().FanDutyPercent),
                DrivingSensorIndices = [.. entry.DrivingSensorIndices],
            })],
        };
    }

    private static FanControlModeValue ToModeValue(FanControlMode mode) => mode switch
    {
        FanControlMode.Manual => FanControlModeValue.Manual,
        FanControlMode.Max => FanControlModeValue.Max,
        FanControlMode.CustomCurve => FanControlModeValue.CustomCurve,
        FanControlMode.Adaptive => FanControlModeValue.Adaptive,
        _ => FanControlModeValue.Auto,
    };

    private static FanControlMode ToMode(FanControlModeValue mode) => mode switch
    {
        FanControlModeValue.Manual => FanControlMode.Manual,
        FanControlModeValue.Max => FanControlMode.Max,
        FanControlModeValue.CustomCurve => FanControlMode.CustomCurve,
        FanControlModeValue.Adaptive => FanControlMode.Adaptive,
        _ => FanControlMode.Auto,
    };

    private static TemperatureAggregationModeValue ToAggregationValue(TemperatureAggregationMode mode) => mode switch
    {
        TemperatureAggregationMode.Average => TemperatureAggregationModeValue.Average,
        TemperatureAggregationMode.Median => TemperatureAggregationModeValue.Median,
        TemperatureAggregationMode.Minimum => TemperatureAggregationModeValue.Minimum,
        _ => TemperatureAggregationModeValue.Maximum,
    };

    /// <summary>Through the shared parser, so a profile reads aggregation the same way every other RPC does.</summary>
    private static TemperatureAggregationMode ToAggregation(TemperatureAggregationModeValue mode)
        => TelemetryGrpcMapper.TryParseTemperatureAggregationMode(mode, out var parsed)
            ? parsed
            : TemperatureAggregationMode.Maximum;
}
