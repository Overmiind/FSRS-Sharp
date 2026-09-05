using FsrsSharp.Configuration;
using FsrsSharp.Core;
using FsrsSharp.Models;

namespace FsrsSharp.Tests;

/// <summary>
/// One test per defect found in the audit against py-fsrs 6.3.2. Each documents the wrong behaviour it
/// guards, so a regression reads as a specific broken promise rather than an opaque number mismatch.
/// </summary>
public class SchedulerRegressionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Scheduler NoFuzz(FsrsConfig? config = null) =>
        new(config ?? new FsrsConfig { EnableFuzzing = false });

    // ---------------------------------------------------------------- Bug A: same-day Hard

    /// <summary>
    /// The short-term stability floor applies to Hard as well as Good and Easy. Without it the raw
    /// multiplier for Hard (always below 1) shrank stability by 40-60%, so a card rated Hard a minute
    /// after Good came out WORSE than before it was reviewed. Mirrors py-fsrs's own regression test.
    /// </summary>
    [Fact]
    public void SameDayHardDoesNotDecreaseStability()
    {
        var scheduler = NoFuzz(new FsrsConfig { EnableFuzzing = false, LearningSteps = [] });

        var card = scheduler.ReviewCard(new Card(), Rating.Good, T0).Card;
        var before = card.Stability!.Value;

        card = scheduler.ReviewCard(card, Rating.Hard, T0.AddMinutes(1)).Card;

        Assert.Equal(before, card.Stability!.Value, 12);
    }

    [Theory]
    [InlineData(Rating.Hard)]
    [InlineData(Rating.Good)]
    [InlineData(Rating.Easy)]
    public void SameDayRecallNeverShrinksStability(Rating rating)
    {
        var scheduler = NoFuzz();
        var card = scheduler.ReviewCard(new Card(), Rating.Good, T0).Card;
        card = scheduler.ReviewCard(card, Rating.Good, T0.AddMinutes(10)).Card;
        var before = card.Stability!.Value;

        card = scheduler.ReviewCard(card, rating, T0.AddMinutes(11)).Card;

        Assert.True(card.Stability!.Value >= before,
            $"same-day {rating} dropped stability from {before} to {card.Stability}");
    }

    [Fact]
    public void SameDayAgainStillReducesStability()
    {
        // The floor must NOT extend to Again - forgetting is supposed to cost stability.
        var scheduler = NoFuzz();
        var card = scheduler.ReviewCard(new Card(), Rating.Easy, T0).Card;
        var before = card.Stability!.Value;

        card = scheduler.ReviewCard(card, Rating.Again, T0.AddMinutes(1)).Card;

        Assert.True(card.Stability!.Value < before);
    }

    // ---------------------------------------------------------------- Bug B: whole-day elapsed time

    /// <summary>
    /// FSRS measures elapsed time in whole days. Reviewing 3 days later and 3 days 23 hours later must
    /// land on the same memory state; passing the fraction through inflated stability by up to ~15%.
    /// </summary>
    [Fact]
    public void ElapsedTimeIsMeasuredInWholeDays()
    {
        var scheduler = NoFuzz();
        var graduated = Graduate(scheduler);

        // Offsets run from the card's own last review - that, not T0, is where the day boundaries fall.
        var since = graduated.LastReview!.Value;
        var onTheDay = since.AddDays(13);
        var lateThatDay = since.AddDays(13).AddHours(23.5);

        var exact = scheduler.ReviewCard(graduated, Rating.Good, onTheDay).Card;
        var late = scheduler.ReviewCard(graduated, Rating.Good, lateThatDay).Card;

        Assert.Equal(exact.Stability!.Value, late.Stability!.Value, 12);
        Assert.Equal(exact.Difficulty!.Value, late.Difficulty!.Value, 12);
        Assert.Equal(exact.Due - onTheDay, late.Due - lateThatDay);
    }

    [Fact]
    public void CrossingADayBoundaryDoesChangeTheOutcome()
    {
        // Guards against "fixing" the above by ignoring elapsed time altogether.
        var scheduler = NoFuzz();
        var graduated = Graduate(scheduler);
        var since = graduated.LastReview!.Value;

        var day13 = scheduler.ReviewCard(graduated, Rating.Good, since.AddDays(13)).Card;
        var day14 = scheduler.ReviewCard(graduated, Rating.Good, since.AddDays(14)).Card;

        Assert.NotEqual(day13.Stability!.Value, day14.Stability!.Value);
    }

    [Fact]
    public void RetrievabilityAlsoUsesWholeDays()
    {
        var scheduler = NoFuzz();
        var card = new Card(state: State.Review, stability: 10, difficulty: 5, due: T0, lastReview: T0);

        Assert.Equal(
            scheduler.GetCardRetrievability(card, T0.AddDays(4)),
            scheduler.GetCardRetrievability(card, T0.AddDays(4).AddHours(23)),
            12);
    }

    [Fact]
    public void RetrievabilityIsZeroForANeverReviewedCard()
    {
        Assert.Equal(0, NoFuzz().GetCardRetrievability(new Card(), T0));
    }

    // ---------------------------------------------------------------- Step ladder bounds

    /// <summary>
    /// A card can carry a step index from a scheduler that had more learning steps than the current one
    /// (config changed, or the value came back from storage). Rating it Hard used to index past the end
    /// of the array and throw; the reference graduates it instead.
    /// </summary>
    [Theory]
    [InlineData(Rating.Hard)]
    [InlineData(Rating.Good)]
    [InlineData(Rating.Easy)]
    public void StepBeyondConfiguredLearningStepsGraduates(Rating rating)
    {
        var scheduler = NoFuzz(new FsrsConfig
        {
            EnableFuzzing = false,
            LearningSteps = [TimeSpan.FromMinutes(1)],
        });

        var stale = new Card(state: State.Learning, step: 7, stability: 4.0, difficulty: 5.0,
            due: T0, lastReview: T0.AddDays(-2));

        var reviewed = scheduler.ReviewCard(stale, rating, T0).Card;

        Assert.Equal(State.Review, reviewed.State);
        Assert.Null(reviewed.Step);
    }

    [Fact]
    public void StepBeyondConfiguredStepsStillRestartsOnAgain()
    {
        var scheduler = NoFuzz(new FsrsConfig
        {
            EnableFuzzing = false,
            LearningSteps = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10)],
        });

        var stale = new Card(state: State.Learning, step: 9, stability: 4.0, difficulty: 5.0,
            due: T0, lastReview: T0.AddDays(-2));

        var reviewed = scheduler.ReviewCard(stale, Rating.Again, T0).Card;

        Assert.Equal(State.Learning, reviewed.State);
        Assert.Equal(0, reviewed.Step);
    }

    [Theory]
    [InlineData(Rating.Again)]
    [InlineData(Rating.Hard)]
    [InlineData(Rating.Good)]
    [InlineData(Rating.Easy)]
    public void RelearningStepBeyondConfiguredStepsIsSafe(Rating rating)
    {
        var scheduler = NoFuzz(new FsrsConfig
        {
            EnableFuzzing = false,
            RelearningSteps = [TimeSpan.FromMinutes(10)],
        });

        var stale = new Card(state: State.Relearning, step: 4, stability: 2.0, difficulty: 6.0,
            due: T0, lastReview: T0.AddDays(-1));

        var reviewed = scheduler.ReviewCard(stale, rating, T0).Card;

        Assert.Contains(reviewed.State, new[] { State.Review, State.Relearning });
    }

    [Fact]
    public void EmptyLearningStepsGraduatesImmediately()
    {
        var scheduler = NoFuzz(new FsrsConfig { EnableFuzzing = false, LearningSteps = [] });

        var reviewed = scheduler.ReviewCard(new Card(), Rating.Good, T0).Card;

        Assert.Equal(State.Review, reviewed.State);
        Assert.Null(reviewed.Step);
    }

    [Fact]
    public void LapseWithNoRelearningStepsStaysInReview()
    {
        var scheduler = NoFuzz(new FsrsConfig { EnableFuzzing = false, RelearningSteps = [] });
        var graduated = Graduate(scheduler);

        var lapsed = scheduler.ReviewCard(graduated, Rating.Again, T0.AddDays(20)).Card;

        Assert.Equal(State.Review, lapsed.State);
    }

    // ---------------------------------------------------------------- State.New

    [Fact]
    public void NewCardBecomesLearningOnFirstReview()
    {
        var reviewed = NoFuzz().ReviewCard(new Card(state: State.New), Rating.Good, T0).Card;

        Assert.Equal(State.Learning, reviewed.State);
        Assert.NotNull(reviewed.Stability);
        Assert.NotNull(reviewed.Difficulty);
    }

    /// <summary>
    /// State.New is this port's addition, so a New card that already carries memory state (imported, or
    /// restored from a backup) is representable. It used to fall through to "unknown state" and throw.
    /// </summary>
    [Fact]
    public void NewCardThatAlreadyHasMemoryStateIsScheduledNotRejected()
    {
        var imported = new Card(state: State.New, stability: 12.0, difficulty: 5.5,
            due: T0, lastReview: T0.AddDays(-9));

        var reviewed = NoFuzz().ReviewCard(imported, Rating.Good, T0).Card;

        Assert.NotEqual(State.New, reviewed.State);
        // Existing memory was carried forward, not thrown away and re-initialised.
        Assert.True(reviewed.Stability!.Value > 12.0);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(99)]
    public void StatesFsrsCannotScheduleFailLoudly(int raw)
    {
        // Cast rather than named members: the two unreachable states this used to cover are gone, but a
        // value outside the enum can still arrive from a database row written by an older build.
        var state = (State)raw;
        var card = new Card(state: state, stability: 5, difficulty: 5, due: T0, lastReview: T0.AddDays(-1));

        var ex = Assert.Throws<InvalidOperationException>(() => NoFuzz().ReviewCard(card, Rating.Good, T0));
        Assert.Contains(state.ToString(), ex.Message);
    }

    // ---------------------------------------------------------------- Missing last review

    /// <summary>
    /// A card with memory state but no last review has no interval to measure. The reference treats that
    /// as a long-term review with R = 0, not as a same-day one; the port used to substitute "0 days ago"
    /// and take the short-term path instead.
    /// </summary>
    [Fact]
    public void MemoryStateWithoutALastReviewTakesTheLongTermPath()
    {
        var scheduler = NoFuzz();
        var card = new Card(state: State.Review, stability: 10.0, difficulty: 5.0, due: T0);

        var reviewed = scheduler.ReviewCard(card, Rating.Good, T0).Card;

        var expected = new FsrsCalculator(new FsrsParameters())
            .NextStability(5.0, 10.0, retrievability: 0, rating: Rating.Good);

        Assert.Equal(expected, reviewed.Stability!.Value, 12);
    }

    // ---------------------------------------------------------------- Bookkeeping

    [Fact]
    public void ReviewDurationIsNotTruncatedToAnInt()
    {
        long duration = (long)int.MaxValue + 5_000;

        var result = NoFuzz().ReviewCard(new Card(), Rating.Good, T0, duration);

        Assert.Equal(duration, result.ReviewLog.ReviewDuration);
    }

    [Fact]
    public void ReviewDoesNotMutateTheCardPassedIn()
    {
        var original = new Card();
        var before = (original.State, original.Step, original.Stability, original.Due);

        NoFuzz().ReviewCard(original, Rating.Easy, T0);

        Assert.Equal(before, (original.State, original.Step, original.Stability, original.Due));
    }

    [Fact]
    public void ReviewLogRecordsTheReviewInstantAndRating()
    {
        var result = NoFuzz().ReviewCard(new Card(), Rating.Hard, T0);

        Assert.Equal(Rating.Hard, result.ReviewLog.Rating);
        Assert.Equal(T0, result.ReviewLog.ReviewDatetime);
        Assert.Equal(T0, result.Card.LastReview);
        Assert.Equal(result.Card.CardId, result.ReviewLog.CardId);
    }

    /// <summary>Due must always be derived from the review instant the schedule was computed from.</summary>
    [Fact]
    public void DueIsAlwaysAheadOfTheReviewInstant()
    {
        var scheduler = NoFuzz();
        var card = new Card();
        var now = T0;

        foreach (var rating in new[] { Rating.Good, Rating.Hard, Rating.Good, Rating.Again, Rating.Good, Rating.Easy })
        {
            now = now.AddHours(30);
            card = scheduler.ReviewCard(card, rating, now).Card;
            Assert.True(card.Due > now);
            Assert.Equal(now, card.LastReview);
        }
    }

    private static Card Graduate(Scheduler scheduler)
    {
        var card = scheduler.ReviewCard(new Card(), Rating.Good, T0).Card;
        return scheduler.ReviewCard(card, Rating.Good, T0.AddMinutes(10)).Card;
    }
}
