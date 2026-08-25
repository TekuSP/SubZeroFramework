using NUnit.Framework;

using SubZeroFramework.Models;

namespace SubZeroFramework.Tests;

/// <summary>
/// Cover for how the controller reports what it knows about a fan.
/// </summary>
/// <remarks>
/// The states are a promise to the user, not a progress metric: none of them is a fault, and a fan running on
/// defaults must never report as anything worse than "Learning". These tests pin the boundaries so the UI's
/// wording cannot quietly stop matching the model.
/// </remarks>
[TestFixture]
public class AdaptiveConfidenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void AFanThatHasLearnedNothing_IsLearning()
    {
        Assert.That(AdaptiveLearningState.None.ConfidenceAt(Now), Is.EqualTo(AdaptiveConfidence.Learning));
    }

    [Test]
    public void AFanWithObservationsButNoSeparableFit_IsStillLearning()
    {
        // Samples accumulate while the machine sits at one operating point, but the fit is not separable, so
        // there is no model yet. Reporting anything better would overstate what is known.
        var state = new AdaptiveLearningState { ObservationCount = 40 };

        Assert.That(state.ConfidenceAt(Now), Is.EqualTo(AdaptiveConfidence.Learning));
    }

    [Test]
    public void AFreshlyIdentifiedModel_IsConverging()
    {
        var state = Identified(observationCount: 40, lastMaterialChange: Now.AddMinutes(-14));

        Assert.That(state.ConfidenceAt(Now), Is.EqualTo(AdaptiveConfidence.Converging));
    }

    [Test]
    public void AModelStillMoving_StaysConvergingHoweverManyObservations()
    {
        // Observation count alone must not promote a model. A machine used the same way all week accumulates
        // samples without the estimate ever being challenged.
        var state = Identified(
            observationCount: AdaptiveLearningState.ConfidentObservationTarget * 3,
            lastMaterialChange: Now.AddMinutes(-5));

        Assert.That(state.ConfidenceAt(Now), Is.EqualTo(AdaptiveConfidence.Converging));
    }

    [Test]
    public void AStableModelWithEnoughObservations_IsConfident()
    {
        var state = Identified(
            observationCount: AdaptiveLearningState.ConfidentObservationTarget,
            lastMaterialChange: Now - AdaptiveLearningState.ConfidentStabilityWindow);

        Assert.That(state.ConfidenceAt(Now), Is.EqualTo(AdaptiveConfidence.Confident));
    }

    [Test]
    public void AStableModelWithTooFewObservations_IsNotYetConfident()
    {
        // Stability alone must not promote either — a machine idle overnight holds still without having
        // learned anything.
        var state = Identified(
            observationCount: AdaptiveLearningState.ConfidentObservationTarget - 1,
            lastMaterialChange: Now.AddDays(-6));

        Assert.That(state.ConfidenceAt(Now), Is.EqualTo(AdaptiveConfidence.Converging));
    }

    [Test]
    public void AModelThatNeverNeededToMove_CountsAsStable()
    {
        // A fit that was right from its first separable answer has nothing to settle into. Without this it
        // could never reach Confident, because there would be no material change to age out.
        var state = Identified(observationCount: AdaptiveLearningState.ConfidentObservationTarget, lastMaterialChange: null)
            with
        { LastUpdatedAt = null };

        Assert.That(state.ConfidenceAt(Now), Is.EqualTo(AdaptiveConfidence.Confident));
    }

    [Test]
    public void ConfidenceNeverRegresses_WhileTheModelHoldsStill()
    {
        // Time passing must only ever improve confidence, never walk it backwards — a UI that said "Confident"
        // yesterday and "Converging" today, with nothing having changed, reads as a fault.
        var state = Identified(
            observationCount: AdaptiveLearningState.ConfidentObservationTarget,
            lastMaterialChange: Now.AddHours(-1));

        var atFirst = state.ConfidenceAt(Now);
        var later = state.ConfidenceAt(Now.AddDays(3));

        Assert.That((int)later, Is.GreaterThanOrEqualTo((int)atFirst));
    }

    private static AdaptiveLearningState Identified(int observationCount, DateTimeOffset? lastMaterialChange)
        => new()
        {
            FeedForwardDutyPerWatt = 2.6d,
            IdentifiedProcessGainCelsiusPerPercent = 0.42d,
            IdentifiedCelsiusPerWatt = 1.1d,
            ObservationCount = observationCount,
            LastUpdatedAt = lastMaterialChange,
            LastMaterialChangeAt = lastMaterialChange,
        };
}
