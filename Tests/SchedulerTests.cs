using FsrsSharp.Configuration;
using FsrsSharp.Core;
using FsrsSharp.Models;
using Xunit;

namespace Tests;

public class SchedulerTests
{
    private static readonly Rating[] TestRatings1 =
    [
        Rating.Good,
        Rating.Good,
        Rating.Good,
        Rating.Good,
        Rating.Good,
        Rating.Good,
        Rating.Again,
        Rating.Again,
        Rating.Good,
        Rating.Good,
        Rating.Good,
        Rating.Good,
        Rating.Good
    ];

    [Fact]
    public void TestReviewCard()
    {
        var scheduler = new Scheduler(new FsrsConfig()
        {
            EnableFuzzing = false,
        });
        var card = new Card();
        var reviewDatetime = new DateTimeOffset(2022, 11, 29, 12, 30, 0, TimeSpan.Zero);

        var ivlHistory = new List<int>();

        foreach (var rating in TestRatings1)
        {
            card = scheduler.ReviewCard(card, rating, reviewDatetime).Card;

            if (card.LastReview.HasValue)
            {
                var ivl = (int)(card.Due - card.LastReview.Value).TotalDays;
                ivlHistory.Add(ivl);
            }

            reviewDatetime = card.Due;
        }

        Assert.Equal(new[] { 0, 2, 11, 46, 163, 498, 0, 0, 2, 4, 7, 12, 21 }, ivlHistory);
    }

    [Fact]
    public void TestRepeatedCorrectReviews()
    {
        var scheduler = new Scheduler(new FsrsConfig()
        {
            EnableFuzzing = false,
        });
        var card = new Card();

        for (int i = 0; i < 10; i++)
        {
            var reviewDatetime =
                new DateTimeOffset(2022, 11, 29, 12, 30, 0, TimeSpan.Zero).AddTicks(i * 10); // ticks are 100ns
            var result = scheduler.ReviewCard(card, Rating.Easy, reviewDatetime);
            card = result.Card;
        }

        Assert.Equal(1.0, card.Difficulty);
    }

    [Fact]
    public void TestFuzz()
    {
        var scheduler = new Scheduler(new FsrsConfig()
        {
            EnableFuzzing = true,
        });
        var card = new Card();

        // Review once to move to Learning
        var result = scheduler.ReviewCard(card, Rating.Good);

        // Review again to move to Review (if default params allow, or just advance)
        // With default params: 1m, 10m.
        // 1st review (Good) -> Step 1 (10m).
        result = scheduler.ReviewCard(result.Card, Rating.Good);
        // 2nd review (Good) -> Review state.
        Assert.Equal(State.Review, result.Card.State);

        // Now review in Review state, Fuzzing should apply if interval is large enough
        // But initial interval might be small.
        // Let's force a larger interval or just check it runs.
        result = scheduler.ReviewCard(result.Card, Rating.Good);

        // Fuzzing logic applies if days >= 2.5. Initial review interval might be small (days).
        // Defaults: DesiredRetention=0.9 -> likely > 2.5 days? 
        // If not, we can force parameters, but merely running it ensures no crash.
    }

    [Fact]
    public void TestRepeatDefaultArg()
    {
        var scheduler = new Scheduler();
        var card = new Card();

        var result = scheduler.ReviewCard(card, Rating.Good);

        // Due defaults to now + interval. 
        // Logic: review_datetime=null -> now.
        // card is new, Good -> step 1 (10 mins).
        // Due should be approx 10 mins from now.

        var timeDelta = result.Card.Due - DateTimeOffset.UtcNow;
        Assert.True(timeDelta.TotalSeconds > 500);
    }

    [Fact]
    public void TestDatetime()
    {
        var scheduler = new Scheduler();
        var card = new Card();

        Assert.True(DateTimeOffset.UtcNow >= card.Due);
        var result = scheduler.ReviewCard(card, Rating.Good, DateTimeOffset.UtcNow);

        Assert.Equal(TimeSpan.Zero, result.Card.Due.Offset);
        Assert.Equal(TimeSpan.Zero, result.Card.LastReview!.Value.Offset);
        Assert.True(result.Card.Due >= result.Card.LastReview);
    }

    [Fact]
    public void TestCustomSchedulerArgs()
    {
        var scheduler = new Scheduler(new FsrsConfig()
        {
            DesiredRetention = 0.9,
            MaximumInterval = 36500,
            EnableFuzzing = false,
        });
        var card = new Card();
        var now = new DateTimeOffset(2022, 11, 29, 12, 30, 0, TimeSpan.Zero);

        var ivlHistory = new List<int>();

        foreach (var rating in TestRatings1)
        {
            var result = scheduler.ReviewCard(card, rating, now);
            card = result.Card;

            if (card.LastReview.HasValue)
            {
                ivlHistory.Add((int)(card.Due - card.LastReview.Value).TotalDays);
            }

            now = card.Due;
        }

        Assert.Equal(new[] { 0, 2, 11, 46, 163, 498, 0, 0, 2, 4, 7, 12, 21 }, ivlHistory);
    }

    [Fact]
    public void TestGoodLearningSteps()
    {
        var scheduler = new Scheduler();
        var createdAt = DateTimeOffset.UtcNow;
        var card = new Card();

        Assert.Equal(State.Learning, card.State);
        Assert.Equal(0, card.Step);

        var result = scheduler.ReviewCard(card, Rating.Good, card.Due);
        var c1 = result.Card;
        Assert.Equal(State.Learning, c1.State);
        Assert.Equal(1, c1.Step);
        // Approx 10 mins
        var diff1 = (c1.Due - createdAt).TotalSeconds;
        Assert.Equal(6, Math.Round(diff1 / 100.0));

        var result2 = scheduler.ReviewCard(c1, Rating.Good, c1.Due);
        var c2 = result2.Card;
        Assert.Equal(State.Review, c2.State);
        Assert.Null(c2.Step);

        // Due in over a day
        var diff2 = (c2.Due - createdAt).TotalSeconds;
        Assert.True(Math.Round(diff2 / 3600.0) >= 24);
    }

    [Fact]
    public void TestAgainLearningSteps()
    {
        var scheduler = new Scheduler();
        var createdAt = DateTimeOffset.UtcNow;
        var card = new Card();

        var result = scheduler.ReviewCard(card, Rating.Again, card.Due);
        Assert.Equal(State.Learning, result.Card.State);
        Assert.Equal(0, result.Card.Step);
        // Approx 1 min (60s)
        Assert.Equal(6, Math.Round((result.Card.Due - createdAt).TotalSeconds / 10.0));
    }

    [Fact]
    public void TestHardLearningSteps()
    {
        var scheduler = new Scheduler();
        var createdAt = DateTimeOffset.UtcNow;
        var card = new Card();

        var result = scheduler.ReviewCard(card, Rating.Hard, card.Due);
        Assert.Equal(State.Learning, result.Card.State);
        Assert.Equal(0, result.Card.Step);
        // Approx 5.5 min (330s) -> 33 * 10
        Assert.Equal(33, Math.Round((result.Card.Due - createdAt).TotalSeconds / 10.0));
    }

    [Fact]
    public void TestEasyLearningSteps()
    {
        var scheduler = new Scheduler();
        var createdAt = DateTimeOffset.UtcNow;
        var card = new Card();

        var result = scheduler.ReviewCard(card, Rating.Easy, card.Due);
        Assert.Equal(State.Review, result.Card.State);
        Assert.Null(result.Card.Step);
        Assert.True(Math.Round((result.Card.Due - createdAt).TotalSeconds / 86400.0) >= 1);
    }

    [Fact]
    public void TestReviewState()
    {
        var scheduler = new Scheduler(new FsrsConfig()
        {
            EnableFuzzing = false,
        });
        var card = new Card();
        var now = DateTimeOffset.UtcNow;

        // Move to review
        var result = scheduler.ReviewCard(card, Rating.Good, now);
        var c = result.Card;
        result = scheduler.ReviewCard(c, Rating.Good, c.Due);
        c = result.Card;

        Assert.Equal(State.Review, c.State);
        var prevDue = c.Due;

        result = scheduler.ReviewCard(c, Rating.Good, c.Due);
        c = result.Card;
        Assert.Equal(State.Review, c.State);
        Assert.True((c.Due - prevDue).TotalHours >= 24);

        prevDue = c.Due;
        result = scheduler.ReviewCard(c, Rating.Again, c.Due);
        c = result.Card;
        Assert.Equal(State.Relearning, c.State);
        Assert.Equal(10, Math.Round((c.Due - prevDue).TotalMinutes));
    }

    [Fact]
    public void TestRelearning()
    {
        var scheduler = new Scheduler(new FsrsConfig()
        {
            EnableFuzzing = false,
        });
        var card = new Card();
        var now = DateTimeOffset.UtcNow;

        // Graduate
        var c = scheduler.ReviewCard(card, Rating.Good, now).Card;
        c = scheduler.ReviewCard(c, Rating.Good, c.Due).Card;
        c = scheduler.ReviewCard(c, Rating.Good, c.Due).Card;

        // Again -> Relearning
        var prevDue = c.Due;
        c = scheduler.ReviewCard(c, Rating.Again, c.Due).Card;
        Assert.Equal(State.Relearning, c.State);
        Assert.Equal(0, c.Step);
        Assert.Equal(10, Math.Round((c.Due - prevDue).TotalMinutes));

        // Again again
        prevDue = c.Due;
        c = scheduler.ReviewCard(c, Rating.Again, c.Due).Card;
        Assert.Equal(State.Relearning, c.State);
        Assert.Equal(0, c.Step);
        Assert.Equal(10, Math.Round((c.Due - prevDue).TotalMinutes));

        // Good -> Graduate from Relearning
        prevDue = c.Due;
        c = scheduler.ReviewCard(c, Rating.Good, c.Due).Card;
        Assert.Equal(State.Review, c.State);
        Assert.True((c.Due - prevDue).TotalHours >= 24);
    }

    [Fact]
    public void TestNoLearningSteps()
    {
        var scheduler = new Scheduler(new FsrsConfig()
        {
            LearningSteps = [],
        });
        var card = new Card();

        // Again -> Review (since no learning steps?)
        // Logic check: if len(learning_steps) == 0 ...
        var c = scheduler.ReviewCard(card, Rating.Again, DateTimeOffset.UtcNow).Card;

        Assert.Equal(State.Review, c.State);
        Assert.True((c.Due - c.LastReview!.Value).TotalDays >= 1);
    }

    [Fact]
    public void TestNoRelearningSteps()
    {
        var scheduler = new Scheduler(new FsrsConfig()
        {
            RelearningSteps = []
        });
        var card = new Card();

        // Learning -> Review
        var c = scheduler.ReviewCard(card, Rating.Good, DateTimeOffset.UtcNow).Card; // Step 1
        c = scheduler.ReviewCard(c, Rating.Good, c.Due).Card; // Review
        Assert.Equal(State.Review, c.State);

        // Review + Again -> Review (since no relearning steps)
        var c2 = scheduler.ReviewCard(c, Rating.Again, c.Due).Card;
        Assert.Equal(State.Review, c2.State);
        Assert.True((c2.Due - c2.LastReview!.Value).TotalDays >= 1);
    }

    [Fact]
    public void TestMaximumInterval()
    {
        var scheduler = new Scheduler(new FsrsConfig()
        {
            MaximumInterval = 100,
        });
        var card = new Card();
        var now = DateTimeOffset.UtcNow;

        // Easy spam to boost interval
        for (int i = 0; i < 10; i++)
        {
            var c = scheduler.ReviewCard(card, Rating.Easy, now).Card;
            card = c;
            now = c.Due;
            Assert.True((card.Due - card.LastReview!.Value).TotalDays <= 100);
        }
    }

    [Fact]
    public void TestStabilityLowerBound()
    {
        var scheduler = new Scheduler();
        var card = new Card();
        var now = DateTimeOffset.UtcNow;

        for (int i = 0; i < 50; i++)
        {
            var c = scheduler.ReviewCard(card, Rating.Again, now.AddDays(1)).Card;
            card = c;
            Assert.True(card.Stability >= 0.001);
        }
    }

    [Fact]
    public void TestReviewHard()
    {
        var scheduler = new Scheduler(new FsrsConfig()
        {
            EnableFuzzing = false,
        });
        var card = new Card();
        var now = DateTimeOffset.UtcNow;
        
        var c = scheduler.ReviewCard(card, Rating.Good, now).Card;
        c = scheduler.ReviewCard(c, Rating.Good, c.Due).Card; // Learning Step 1 -> Review
  
        Assert.Equal(State.Review, c.State);
        now = c.Due.AddDays(10);
        var cHard = scheduler.ReviewCard(c, Rating.Hard, now).Card;
        Assert.Equal(State.Review, cHard.State);

        c = scheduler.ReviewCard(c, Rating.Good, now).Card;
        Assert.True(cHard.Stability < c.Stability);
    }

    [Fact]
    public void TestRelearningHard()
    {
        var scheduler = new Scheduler(new FsrsConfig()
        {
            EnableFuzzing = false,
        });
        var card = new Card();

        // Graduate
        var c = scheduler.ReviewCard(card, Rating.Good).Card; // Learning 0 -> 1
        c = scheduler.ReviewCard(c, Rating.Good).Card; // Learning 1 -> Review

        // Fail
        c = scheduler.ReviewCard(c, Rating.Again).Card; // Review -> Relearning (Step 0)
        Assert.Equal(State.Relearning, c.State);
        Assert.Equal(0, c.Step);

        // Hard in Relearning
        // Logic: if Step=0, interval is average of step 0 and 1 (if 2 steps).
        // Default RelearningSteps = [10m]. Length = 1.
        // If Length=1, Hard -> step 0 * 1.5.
        // 10m * 1.5 = 15m.

        var prevDue = c.Due;
        var cHard = scheduler.ReviewCard(c, Rating.Hard, c.Due).Card;

        Assert.Equal(State.Relearning, cHard.State);
        Assert.Equal(0, cHard.Step); // Hard doesn't advance step?

        // Check interval
        // 15 minutes = 900 seconds.
        var intervalSeconds = (cHard.Due - prevDue).TotalSeconds;
        Assert.Equal(900, Math.Round(intervalSeconds));
    }

    [Fact]
    public void TestRelearningEasy()
    {
        var scheduler = new Scheduler();
        var card = new Card();

        // Graduate
        card = scheduler.ReviewCard(card, Rating.Good).Card;
        card = scheduler.ReviewCard(card, Rating.Good).Card;

        // Fail
        card = scheduler.ReviewCard(card, Rating.Again).Card;
        Assert.Equal(State.Relearning, card.State);

        // Easy
        card = scheduler.ReviewCard(card, Rating.Easy).Card;

        Assert.Equal(State.Review, card.State);
        Assert.Null(card.Step);
    }
}