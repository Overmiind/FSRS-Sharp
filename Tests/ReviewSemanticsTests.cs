using FsrsSharp.Configuration;
using FsrsSharp.Core;
using FsrsSharp.Models;

namespace FsrsSharp.Tests;

/// <summary>
/// Properties of the scheduler that callers building a review UI need to know about, because they
/// decide how that UI has to be shaped. None of them are bugs — each is faithful to reference FSRS —
/// but each one bites if you assume otherwise.
/// </summary>
public class ReviewSemanticsTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-01-01T00:00:00+00:00");

    private static Scheduler NoFuzz() => new(new FsrsConfig { EnableFuzzing = false });

    private static Card Mature(double stability = 200, double difficulty = 5) =>
        new(state: State.Review, stability: stability, difficulty: difficulty,
            due: T0.AddDays(stability), lastReview: T0);

    // ------------------------------------------------- A review cannot be undone, only replayed

    /// <summary>
    /// FSRS is not invertible: once a card has been scheduled from Good there is no way to walk that
    /// back and apply Again instead. If you let a card be re-answered, keep the pre-review snapshot and
    /// re-review from it — that is the only way to land where the corrected answer alone would have.
    /// </summary>
    [Fact]
    public void ReReviewingFromASnapshotMatchesHavingOnlyGivenTheOtherRating()
    {
        var reviewedAt = T0.AddDays(200);
        var scheduler = NoFuzz();

        // What the card SHOULD look like if Again had been the only answer.
        var direct = scheduler.ReviewCard(Mature(), Rating.Again, reviewedAt).Card;

        // Good lands first, then a corrected answer arrives.
        var afterGood = scheduler.ReviewCard(Mature(), Rating.Good, reviewedAt).Card;
        Assert.NotEqual(direct.Stability, afterGood.Stability);

        // Restore the snapshot, re-review with the corrected rating.
        var reDerived = scheduler.ReviewCard(Mature(), Rating.Again, reviewedAt).Card;

        Assert.Equal(direct.State, reDerived.State);
        Assert.Equal(direct.Stability!.Value, reDerived.Stability!.Value, 12);
        Assert.Equal(direct.Difficulty!.Value, reDerived.Difficulty!.Value, 12);
        Assert.Equal(direct.Due, reDerived.Due);
    }

    /// <summary>
    /// The failure mode of NOT re-deriving: chaining a second review onto the first leaves the card in a
    /// state no single answer could produce, because the second lands with elapsed = 0 and therefore
    /// takes the short-term path.
    /// </summary>
    [Fact]
    public void ChainingASecondReviewInsteadOfReDerivingGivesTheWrongState()
    {
        var reviewedAt = T0.AddDays(200);
        var scheduler = NoFuzz();

        var chained = scheduler.ReviewCard(
            scheduler.ReviewCard(Mature(), Rating.Good, reviewedAt).Card, Rating.Again, reviewedAt).Card;

        var correct = scheduler.ReviewCard(Mature(), Rating.Again, reviewedAt).Card;

        Assert.NotEqual(correct.Stability!.Value, chained.Stability!.Value);
    }

    // ------------------------------------------------- Reviewing a card early is not free

    /// <summary>
    /// A same-day repeat of a mature card leaves stability untouched — the short-term multiplier is
    /// floored at 1.0 — yet still resets Due to now plus the full interval. Pure clock reset for zero
    /// learning, so don't drive a review off every time a card happens to be shown.
    /// </summary>
    [Fact]
    public void SameDayGoodOnAMatureCardMovesDueButNotStability()
    {
        var scheduler = NoFuzz();
        var card = Mature();
        var sameDay = T0.AddHours(1);

        var reviewed = scheduler.ReviewCard(card, Rating.Good, sameDay).Card;

        Assert.Equal(card.Stability!.Value, reviewed.Stability!.Value, 12);
        Assert.True(reviewed.Due > card.Due, "Due slid forward despite no change in stability.");
    }

    /// <summary>
    /// Failing a card EARLY costs more stability than failing it on time, because forget-stability scales
    /// by exp((1-R)*w14) and an early review has a higher R. The penalty is a flat ~15.2% — it is
    /// 1 - exp(-0.1 * w14) — and it is faithful to reference FSRS, not a porting bug. It is the reason
    /// answers given ahead of the due date are worth grading but not necessarily worth rescheduling.
    /// </summary>
    [Theory]
    [InlineData(20.0, 3.0)]
    [InlineData(60.0, 5.0)]
    [InlineData(200.0, 8.0)]
    public void AnEarlyLapseCostsMoreStabilityThanAnOnTimeOne(double stability, double difficulty)
    {
        var scheduler = NoFuzz();

        var onTime = scheduler.ReviewCard(
            Mature(stability, difficulty), Rating.Again, T0.AddDays(stability)).Card;

        // One day in: elapsed is a whole day, so this is still the long-term path, just at a high R.
        var early = scheduler.ReviewCard(
            Mature(stability, difficulty), Rating.Again, T0.AddDays(1)).Card;

        Assert.True(
            early.Stability!.Value < onTime.Stability!.Value,
            $"Early lapse kept {early.Stability} vs {onTime.Stability} on time.");

        var penalty = 1 - (early.Stability.Value / onTime.Stability.Value);
        Assert.InRange(penalty, 0.10, 0.20);
    }
}
