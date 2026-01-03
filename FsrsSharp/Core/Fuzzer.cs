namespace FsrsSharp.Core;

public class Fuzzer : IFuzzer
{
    private readonly Random _random = new();

    private static readonly (double, double, double)[] FuzzRanges =
    [
        (2.5, 7.0, 0.15), (7.0, 20.0, 0.1), (20.0, double.PositiveInfinity, 0.05)
    ];

    public TimeSpan ApplyFuzz(TimeSpan interval, int maxInterval)
    {
        double days = interval.TotalDays;
        if (days < 2.5) return interval;

        double delta = 1.0;
        foreach (var (start, end, factor) in FuzzRanges)
        {
            delta += factor * Math.Max(Math.Min(days, end) - start, 0.0);
        }

        double minIvl = Math.Max(2, Math.Round(days - delta));
        double maxIvl = Math.Min(Math.Round(days + delta), maxInterval);
        minIvl = Math.Min(minIvl, maxIvl);

        double fuzzed = (_random.NextDouble() * (maxIvl - minIvl + 1)) + minIvl;
        return TimeSpan.FromDays(Math.Min(Math.Round(fuzzed), maxInterval));
    }
}