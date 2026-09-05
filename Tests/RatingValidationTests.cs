using FsrsSharp.Configuration;
using FsrsSharp.Core;
using FsrsSharp.Models;

namespace FsrsSharp.Tests;

/// <summary>
/// A <see cref="Rating"/> is an <c>int</c>, and neither model binding nor JSON deserialization
/// range-checks it — so an out-of-range value used to reach the weight lookup, where it is an ARRAY
/// INDEX. Rating 0 indexed <c>Weights[-1]</c> and threw; rating 7 quietly read w6 (the difficulty-delta
/// weight) and used it as an initial stability, corrupting the card with no error at all.
/// </summary>
public class RatingValidationTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-01-01T00:00:00+00:00");

    private static Scheduler NoFuzz() => new(new FsrsConfig { EnableFuzzing = false });

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(int.MaxValue)]
    public void OutOfRangeRatingIsRejectedOnAFirstReview(int raw)
    {
        var card = new Card();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => NoFuzz().ReviewCard(card, (Rating)raw, T0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void OutOfRangeRatingIsRejectedOnAReviewStateCardToo(int raw)
    {
        // The dangerous case: this path never touches the weight array by index, so before the guard it
        // did not throw — it took the recall branch and swung difficulty by w6 * (rating - 3).
        var card = new Card(
            state: State.Review, stability: 40, difficulty: 5, due: T0, lastReview: T0.AddDays(-40));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => NoFuzz().ReviewCard(card, (Rating)raw, T0));
    }

    [Fact]
    public void OutOfRangeRatingIsRejectedByTheCalculatorDirectly()
    {
        var calc = new FsrsCalculator(new FsrsParameters());

        Assert.Throws<ArgumentOutOfRangeException>(() => calc.InitialStability((Rating)0));
        Assert.Throws<ArgumentOutOfRangeException>(() => calc.InitialStability((Rating)7));
    }

    [Theory]
    [InlineData(Rating.Again)]
    [InlineData(Rating.Hard)]
    [InlineData(Rating.Good)]
    [InlineData(Rating.Easy)]
    public void EveryRealRatingStillSchedules(Rating rating)
    {
        var reviewed = NoFuzz().ReviewCard(new Card(), rating, T0).Card;

        Assert.NotNull(reviewed.Stability);
        Assert.NotNull(reviewed.Difficulty);
    }

    [Theory]
    [InlineData(null, Rating.Good, Rating.Good)]
    [InlineData(Rating.Good, Rating.Again, Rating.Again)]
    [InlineData(Rating.Again, Rating.Good, Rating.Again)]
    [InlineData(Rating.Easy, Rating.Hard, Rating.Hard)]
    [InlineData(Rating.Hard, Rating.Easy, Rating.Hard)]
    [InlineData(Rating.Good, Rating.Good, Rating.Good)]
    public void WorstPicksTheLowerRatingRegardlessOfOrder(Rating? seen, Rating next, Rating expected)
    {
        // This is what makes a session's schedule independent of the order its exercises are answered in.
        Assert.Equal(expected, Ratings.Worst(seen, next));
    }
}
