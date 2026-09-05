using System.Globalization;
using System.Text.Json;
using FsrsSharp.Configuration;
using FsrsSharp.Core;
using FsrsSharp.Models;

namespace FsrsSharp.Tests;

/// <summary>
/// Locks the scheduler to the reference implementation (py-fsrs 6.3.2).
///
/// Golden/expected.csv was produced by running py-fsrs itself over Golden/scenarios.json - 64 scenarios,
/// 692 reviews, fuzzing off, explicit review timestamps. Every row is the reference's own output. If a
/// change to the C# makes any row drift, this fails.
///
/// The scenarios deliberately mix sub-day gaps (which exercise the short-term stability path), exact
/// whole-day gaps, and fractional multi-day gaps such as 3.875 or 10.58 days. The fractional ones matter:
/// FSRS measures elapsed time in WHOLE days, and an earlier version of this port passed the fraction
/// straight through, which silently inflated stability on every long-term review.
/// </summary>
public class ReferenceParityTests
{
    private sealed record Step(int Rating, double OffsetHours);

    private sealed record Expectation(
        string Scenario, int Index, int State, int? CardStep, double Stability, double Difficulty, double IntervalSeconds);

    private static readonly DateTimeOffset Start =
        DateTimeOffset.Parse("2026-01-01T00:00:00+00:00", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    [Fact]
    public void MatchesReferenceImplementationOnEveryGoldenReview()
    {
        var scenarios = LoadScenarios();
        var expected = LoadExpectations();

        Assert.NotEmpty(expected);
        Assert.Equal(expected.Count, scenarios.Sum(s => s.Value.Count));

        var scheduler = new Scheduler(new FsrsConfig { EnableFuzzing = false });
        var failures = new List<string>();

        foreach (var (name, steps) in scenarios)
        {
            var card = new Card();
            var now = Start;

            for (int i = 0; i < steps.Count; i++)
            {
                now = now.AddHours(steps[i].OffsetHours);
                card = scheduler.ReviewCard(card, (Rating)steps[i].Rating, now).Card;

                var want = expected[(name, i)];
                Check(failures, name, i, "state", (int)card.State, want.State);
                Check(failures, name, i, "step", card.Step, want.CardStep);
                Check(failures, name, i, "stability", card.Stability!.Value, want.Stability);
                Check(failures, name, i, "difficulty", card.Difficulty!.Value, want.Difficulty);
                Check(failures, name, i, "interval", (card.Due - now).TotalSeconds, want.IntervalSeconds);
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} divergence(s) from py-fsrs 6.3.2:{Environment.NewLine}" +
            string.Join(Environment.NewLine, failures.Take(25)));
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(5.0)]
    [InlineData(37.5)]
    [InlineData(400.0)]
    public void RetrievabilityIsExactlyPointNineAfterOneStabilityPeriod(double stability)
    {
        // R(S) == 0.9 is the identity the decay/factor pair is derived from, so this pins both at once.
        // Asserted on the calculator rather than the scheduler: the scheduler floors elapsed time to whole
        // days by design, which would make a fractional stability miss 0.9 for the right reason.
        var calc = new FsrsCalculator(new FsrsParameters());

        var r = calc.Retrievability(stability, stability, calc.Decay, calc.Factor);

        Assert.Equal(0.9, r, 12);
    }

    [Fact]
    public void SchedulerReportsPointNineRetrievabilityAtTheScheduledDueDate()
    {
        const double stability = 37.0;
        var scheduler = new Scheduler(new FsrsConfig { EnableFuzzing = false });
        var card = new Card(
            state: State.Review, stability: stability, difficulty: 5.0, due: Start, lastReview: Start);

        Assert.Equal(0.9, scheduler.GetCardRetrievability(card, Start.AddDays(stability)), 4);
    }

    private static void Check<T>(List<string> failures, string scenario, int index, string field, T actual, T want)
    {
        bool ok = (actual, want) switch
        {
            (double a, double w) => Math.Abs(a - w) <= 1e-9 * Math.Max(1.0, Math.Abs(w)),
            _ => EqualityComparer<T>.Default.Equals(actual, want),
        };

        if (!ok)
        {
            failures.Add($"  {scenario}[{index}].{field}: expected {want}, got {actual}");
        }
    }

    private static Dictionary<string, List<Step>> LoadScenarios()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(GoldenPath("scenarios.json")));
        return doc.RootElement.GetProperty("scenarios").EnumerateArray().ToDictionary(
            s => s.GetProperty("name").GetString()!,
            s => s.GetProperty("steps").EnumerateArray()
                .Select(x => new Step(x.GetProperty("rating").GetInt32(), x.GetProperty("offset_hours").GetDouble()))
                .ToList());
    }

    private static Dictionary<(string, int), Expectation> LoadExpectations()
    {
        return File.ReadAllLines(GoldenPath("expected.csv"))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split(','))
            .Select(f => new Expectation(
                f[0],
                int.Parse(f[1], CultureInfo.InvariantCulture),
                int.Parse(f[2], CultureInfo.InvariantCulture),
                string.IsNullOrEmpty(f[3]) ? null : int.Parse(f[3], CultureInfo.InvariantCulture),
                double.Parse(f[4], CultureInfo.InvariantCulture),
                double.Parse(f[5], CultureInfo.InvariantCulture),
                double.Parse(f[6], CultureInfo.InvariantCulture)))
            .ToDictionary(e => (e.Scenario, e.Index));
    }

    private static string GoldenPath(string file) =>
        Path.Combine(AppContext.BaseDirectory, "Golden", file);
}
