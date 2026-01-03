using FsrsSharp.Models;

namespace FsrsSharp;

public interface IFsrsCalculator
{
    double InitialStability(Rating rating);
    double InitialDifficulty(Rating rating);
    double Retrievability(double elapsedDays, double stability, double decay, double factor);
    double NextInterval(double stability, double retention, double decay, double factor, int maxInterval);
    double NextDifficulty(double currentDifficulty, Rating rating);
    double NextStability(double difficulty, double stability, double retrievability, Rating rating);
    double ShortTermStability(double stability, Rating rating);
}