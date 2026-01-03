using FsrsSharp.Configuration;
using FsrsSharp.Core;
using FsrsSharp.Models;
using Xunit;

namespace Tests;

public class CalculatorTests
{
    [Fact]
    public void TestMemoState()
    {
        var scheduler = new Scheduler();
        var ratings = new[]
        {
            Rating.Again,
            Rating.Good,
            Rating.Good,
            Rating.Good,
            Rating.Good,
            Rating.Good,
        };
        var ivlHistory = new[] { 0, 0, 1, 3, 8, 21 };

        var card = new Card();
        var reviewDatetime = new DateTimeOffset(2022, 11, 29, 12, 30, 0, TimeSpan.Zero);

        for (int i = 0; i < ratings.Length; i++)
        {
            reviewDatetime = reviewDatetime.AddDays(ivlHistory[i]);
            var result = scheduler.ReviewCard(card, ratings[i], reviewDatetime);
            card = result.Card;
        }

        Assert.Equal(53.62691, card.Stability!.Value, 4); // pytest.approx(..., abs=1e-4) -> precision 4
        Assert.Equal(6.3574867, card.Difficulty!.Value, 4);
    }

    [Fact]
    public void TestRetrievability()
    {
        // 1. Arrange: Create components
        var config = new FsrsConfig();
        // Use default parameters (2 learning steps: 1m, 10m)
        // config.LearningSteps = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10)]; 

        var calculator = new FsrsCalculator(config.Parameters);
        var scheduler = new Scheduler(config, calculator, new Fuzzer());
        
        var card = new Card();

        // Helper: Local function to replace the old scheduler.GetCardRetrievability(card)
        double GetCurrentRetrievability(Card c)
        {
            if (c.Stability == null || c.LastReview == null) return 0;

            // Assume 0 time has passed since the last review for this test
            // or use DateTimeOffset.UtcNow if the review was in the past
            var elapsedDays = (DateTimeOffset.UtcNow - c.LastReview.Value).TotalDays;
            // Protection against negative time if the test runs too fast
            elapsedDays = Math.Max(0, elapsedDays);

            return calculator.Retrievability(
                elapsedDays,
                c.Stability.Value,
                config.Parameters.Decay,
                config.Parameters.Factor
            );
        }

        // 2. Act & Assert

        // --- Initial State ---
        Assert.Equal(State.Learning, card.State);
        Assert.Equal(0, GetCurrentRetrievability(card));

        // --- 1st Review (Learning Step 0 -> Step 1) ---
        // New -> Good (move to next learning step)
        var result = scheduler.ReviewCard(card, Rating.Good);

        // Immediately after review, R should be 1.0 (we remember 100%)
        // Assert.InRange allows small deviations if elapsedDays is slightly > 0
        Assert.InRange(GetCurrentRetrievability(result.Card), 0.99, 1.0);

        // --- 2nd Review (Learning Step 1 -> Graduate to Review) ---
        // Good -> Review (steps finished)
        var result2 = scheduler.ReviewCard(result.Card, Rating.Good);

        Assert.Equal(State.Review, result2.Card.State);
        Assert.InRange(GetCurrentRetrievability(result2.Card), 0.99, 1.0);

        // --- 3rd Review (Lapse: Review -> Relearning) ---
        // Review -> Again (forgot card)
        var result3 = scheduler.ReviewCard(result2.Card, Rating.Again);

        Assert.Equal(State.Relearning, result3.Card.State);
        // Even in Relearning, immediately after answer (Review) we "remembered" it (or saw the answer),
        // so R resets to a high value at this moment.
        Assert.InRange(GetCurrentRetrievability(result3.Card), 0.99, 1.0);
    }

    [Fact]
    public void TestSchedulerParameterValidation()
    {
        var defaultParams = new FsrsParameters().Defaults;

        var paramsHigh = defaultParams.ToArray(); // Copy array
        paramsHigh[6] = 100.0; // Break a parameter (e.g. w[6])

        // Assert
        // Expect error specifically when creating parameters object
        Assert.Throws<ArgumentException>(() => new FsrsParameters(paramsHigh));
        
        var paramsLow = defaultParams.ToArray();
        paramsLow[10] = -42.0; // Break another parameter
        Assert.Throws<ArgumentException>(() => new FsrsParameters(paramsLow));
    }
}