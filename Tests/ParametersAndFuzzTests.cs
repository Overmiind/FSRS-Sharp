using FsrsSharp.Configuration;
using FsrsSharp.Core;
using FsrsSharp.Models;

namespace FsrsSharp.Tests;

public class ParametersAndFuzzTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The FSRS-6 defaults, copied from py-fsrs 6.3.2 DEFAULT_PARAMETERS.</summary>
    private static readonly double[] ReferenceDefaults =
    [
        0.212, 1.2931, 2.3065, 8.2956, 6.4133, 0.8334, 3.0194, 0.001,
        1.8722, 0.1666, 0.796, 1.4835, 0.0614, 0.2629, 1.6483, 0.6014,
        1.8729, 0.5425, 0.0912, 0.0658, 0.1542
    ];

    [Fact]
    public void DefaultWeightsMatchTheReference()
    {
        Assert.Equal(ReferenceDefaults, new FsrsParameters().Weights);
    }

    [Fact]
    public void DecayAndFactorAreDerivedFromTheLastWeight()
    {
        var p = new FsrsParameters();

        Assert.Equal(-ReferenceDefaults[20], p.Decay, 12);
        Assert.Equal(Math.Pow(0.9, 1.0 / p.Decay) - 1, p.Factor, 12);
    }

    [Fact]
    public void OutOfBoundsWeightsAreRejected()
    {
        var tooBig = (double[])ReferenceDefaults.Clone();
        tooBig[7] = 0.9; // upper bound is 0.75

        Assert.Throws<ArgumentException>(() => new FsrsParameters(tooBig));
    }

    [Fact]
    public void WrongWeightCountIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new FsrsParameters(ReferenceDefaults[..20]));
    }

    /// <summary>
    /// Weights are validated once, at construction. Handing back the caller's array would let them edit
    /// it afterwards and slip past that check - and desync the precomputed decay/factor.
    /// </summary>
    [Fact]
    public void WeightsCannotBeMutatedAfterConstruction()
    {
        var supplied = (double[])ReferenceDefaults.Clone();
        var p = new FsrsParameters(supplied);

        supplied[20] = 0.8;

        Assert.Equal(ReferenceDefaults[20], p.Weights[20]);
        Assert.Equal(-ReferenceDefaults[20], p.Decay, 12);
    }

    [Fact]
    public void LowerAndUpperBoundsBracketTheDefaults()
    {
        var p = new FsrsParameters();

        for (int i = 0; i < p.Weights.Count; i++)
        {
            Assert.InRange(p.Weights[i], p.LowerBounds[i], p.UpperBounds[i]);
        }
    }

    [Fact]
    public void HigherDesiredRetentionMeansShorterIntervals()
    {
        Card ReviewAt(double retention)
        {
            var scheduler = new Scheduler(new FsrsConfig { EnableFuzzing = false, DesiredRetention = retention });
            var card = scheduler.ReviewCard(new Card(), Rating.Good, T0).Card;
            return scheduler.ReviewCard(card, Rating.Good, T0.AddMinutes(10)).Card;
        }

        var strict = ReviewAt(0.95);
        var relaxed = ReviewAt(0.80);

        Assert.True(strict.Due < relaxed.Due);
    }

    [Fact]
    public void IntervalsAreCappedByMaximumInterval()
    {
        const int cap = 30;
        var scheduler = new Scheduler(new FsrsConfig { EnableFuzzing = false, MaximumInterval = cap });

        var card = new Card();
        var now = T0;
        for (int i = 0; i < 12; i++)
        {
            now = now.AddDays(cap);
            card = scheduler.ReviewCard(card, Rating.Easy, now).Card;
            Assert.True(card.Due - now <= TimeSpan.FromDays(cap));
        }
    }

    // ---------------------------------------------------------------- Fuzzing

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ShortIntervalsAreNeverFuzzed(int days)
    {
        var fuzzer = new Fuzzer();
        var interval = TimeSpan.FromDays(days);

        for (int i = 0; i < 200; i++)
        {
            Assert.Equal(interval, fuzzer.ApplyFuzz(interval, 36500));
        }
    }

    /// <summary>Bounds taken from the reference's FUZZ_RANGES; verified against py-fsrs 6.3.2.</summary>
    [Theory]
    [InlineData(3, 2, 5)]
    [InlineData(5, 4, 7)]
    [InlineData(10, 8, 13)]
    [InlineData(30, 27, 34)]
    [InlineData(100, 93, 108)]
    [InlineData(365, 345, 386)]
    public void FuzzStaysWithinTheReferenceBounds(int days, int min, int max)
    {
        var fuzzer = new Fuzzer();
        var seenMin = int.MaxValue;
        var seenMax = int.MinValue;

        for (int i = 0; i < 20_000; i++)
        {
            var d = (int)fuzzer.ApplyFuzz(TimeSpan.FromDays(days), 36500).TotalDays;
            seenMin = Math.Min(seenMin, d);
            seenMax = Math.Max(seenMax, d);
        }

        Assert.Equal(min, seenMin);
        Assert.Equal(max, seenMax);
    }

    [Fact]
    public void FuzzNeverExceedsTheMaximumInterval()
    {
        var fuzzer = new Fuzzer();

        for (int i = 0; i < 20_000; i++)
        {
            Assert.True(fuzzer.ApplyFuzz(TimeSpan.FromDays(36500), 36500).TotalDays <= 36500);
        }
    }

    /// <summary>
    /// A Scheduler is a natural singleton and reviews arrive concurrently, so the fuzzer must not hold
    /// per-instance random state that concurrent callers can tear.
    /// </summary>
    [Fact]
    public void ConcurrentFuzzingStaysWithinBounds()
    {
        var fuzzer = new Fuzzer();

        Parallel.For(0, 40_000, _ =>
        {
            var d = fuzzer.ApplyFuzz(TimeSpan.FromDays(30), 36500).TotalDays;
            Assert.InRange(d, 27, 34);
        });
    }

    [Fact]
    public void FuzzingIsOffWhenDisabled()
    {
        var scheduler = new Scheduler(new FsrsConfig { EnableFuzzing = false });

        var first = Run();
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(first, Run());
        }

        DateTimeOffset Run()
        {
            var card = scheduler.ReviewCard(new Card(), Rating.Easy, T0).Card;
            return scheduler.ReviewCard(card, Rating.Easy, T0.AddDays(40)).Card.Due;
        }
    }
}
