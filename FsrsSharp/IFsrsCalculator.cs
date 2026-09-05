using FsrsSharp.Models;

namespace FsrsSharp;

public interface IFsrsCalculator
{
    /// <summary>Forgetting-curve decay (-w[20]). Exposed so the scheduler reads it from the same
    /// weight set the stability formulas use, rather than from a config that may hold a different one.</summary>
    double Decay { get; }

    /// <summary>Companion to <see cref="Decay"/>, chosen so R(S) == 0.9.</summary>
    double Factor { get; }

    double InitialStability(Rating rating);
    double InitialDifficulty(Rating rating);
    double Retrievability(double elapsedDays, double stability, double decay, double factor);
    double NextInterval(double stability, double retention, double decay, double factor, int maxInterval);
    double NextDifficulty(double currentDifficulty, Rating rating);
    double NextStability(double difficulty, double stability, double retrievability, Rating rating);
    double ShortTermStability(double stability, Rating rating);
}
