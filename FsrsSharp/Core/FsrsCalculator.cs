using FsrsSharp.Configuration;
using FsrsSharp.Models;

namespace FsrsSharp.Core;

public class FsrsCalculator(FsrsParameters parameters) : IFsrsCalculator
{
    public double Decay { get; } = parameters.Decay;

    public double Factor { get; } = parameters.Factor;

    public double InitialStability(Rating rating)
    {
        // Guarded because this is the one place a Rating becomes an ARRAY INDEX. See Ratings.Validate.
        Ratings.Validate(rating);
        return ClampStability(parameters.Weights[(int)rating - 1]);
    }

    public double InitialDifficulty(Rating rating)
    {
        // Public method should return clamped value [1..10]
        var val = CalculateInitialDifficultyRaw(rating);
        return ClampDifficulty(val);
    }

    public double Retrievability(double elapsedDays, double stability, double decay, double factor)
    {
        return Math.Pow(1 + factor * elapsedDays / stability, decay);
    }

    public double NextInterval(double stability, double retention, double decay, double factor, int maxInterval)
    {
        double nextInterval = (stability / factor) * (Math.Pow(retention, 1 / decay) - 1);
        nextInterval = Math.Round(nextInterval);
        return Math.Min(Math.Max(nextInterval, 1), maxInterval);
    }

    public double NextDifficulty(double currentDifficulty, Rating rating)
    {
        double nextDiff = parameters.Weights[6] * ((int)rating - 3);
        double deltaDifficulty = -nextDiff;
        double linearDamping = (10.0 - currentDifficulty) * deltaDifficulty / 9.0;

        double nextDifficulty = currentDifficulty + linearDamping;

        // Deliberately the UNCLAMPED D0(Easy) — matches the reference's `_initial_difficulty(Easy, clamp=False)`.
        // With the default weights this is about -4.77, which looks wrong and is not.
        double initDiffEasyRaw = CalculateInitialDifficultyRaw(Rating.Easy);

        double meanReversion = parameters.Weights[7] * initDiffEasyRaw + (1 - parameters.Weights[7]) * nextDifficulty;

        return ClampDifficulty(meanReversion);
    }

    public double NextStability(double difficulty, double stability, double retrievability, Rating rating)
    {
        var newStability = rating == Rating.Again
            ? NextForgetStability(difficulty, stability, retrievability)
            : NextRecallStability(difficulty, stability, retrievability, rating);

        return ClampStability(newStability);
    }

    public double ShortTermStability(double stability, Rating rating)
    {
        double increase = Math.Exp(parameters.Weights[17] * ((int)rating - 3 + parameters.Weights[18])) *
                          Math.Pow(stability, -parameters.Weights[19]);

        // The floor applies to every non-Again rating, Hard included. For Hard the raw multiplier is
        // always below 1, so without this a same-day Hard would SHRINK stability by 40-60% — a card
        // rated Hard a minute after Good would come out worse than before it was reviewed.
        if (rating != Rating.Again)
        {
            increase = Math.Max(increase, 1.0);
        }

        return ClampStability(stability * increase);
    }

    private double NextForgetStability(double difficulty, double stability, double retrievability)
    {
        double longTerm = parameters.Weights[11] *
                          Math.Pow(difficulty, -parameters.Weights[12]) *
                          (Math.Pow(stability + 1, parameters.Weights[13]) - 1) *
                          Math.Exp((1 - retrievability) * parameters.Weights[14]);

        double shortTerm = stability / Math.Exp(parameters.Weights[17] * parameters.Weights[18]);

        return Math.Min(longTerm, shortTerm);
    }

    private double NextRecallStability(double difficulty, double stability, double retrievability, Rating rating)
    {
        double hardPenalty = (rating == Rating.Hard) ? parameters.Weights[15] : 1;
        double easyBonus = (rating == Rating.Easy) ? parameters.Weights[16] : 1;

        return stability * (1 + Math.Exp(parameters.Weights[8]) *
            (11 - difficulty) *
            Math.Pow(stability, -parameters.Weights[9]) *
            (Math.Exp((1 - retrievability) * parameters.Weights[10]) - 1) *
            hardPenalty *
            easyBonus);
    }

    private double CalculateInitialDifficultyRaw(Rating rating)
    {
        return parameters.Weights[4] - Math.Exp(parameters.Weights[5] * ((int)rating - 1)) + 1;
    }

    private static double ClampDifficulty(double difficulty) =>
        Math.Min(Math.Max(difficulty, FsrsParameters.DifficultyMin), FsrsParameters.DifficultyMax);

    private static double ClampStability(double stability) =>
        Math.Max(stability, FsrsParameters.StabilityMin);
}
