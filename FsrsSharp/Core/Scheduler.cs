using FsrsSharp.Configuration;
using FsrsSharp.Models;

namespace FsrsSharp.Core;

public class Scheduler : IScheduler
{
    private readonly FsrsConfig _config;
    private readonly IFsrsCalculator _calc;
    private readonly IFuzzer _fuzzer;

    public Scheduler() : this(new FsrsConfig())
    {
    }

    public Scheduler(FsrsConfig config) : this(config, new FsrsCalculator(config.Parameters), new Fuzzer())
    {
    }

    public Scheduler(FsrsConfig config, IFsrsCalculator calc, IFuzzer fuzzer)
    {
        _config = config;
        _calc = calc;
        _fuzzer = fuzzer;
    }

    public ReviewResult ReviewCard(
        Card card,
        Rating rating,
        DateTimeOffset? reviewDatetime = null,
        long? reviewDuration = null)
    {
        // Validate once, at the entry point, so an out-of-range rating fails with a clear message
        // instead of surfacing as an IndexOutOfRangeException deep inside the weight lookup (or, on the
        // long-term path, as a silent difficulty swing that never throws at all).
        Ratings.Validate(rating);

        var now = reviewDatetime ?? DateTimeOffset.UtcNow;
        var nextCard = card.Copy();

        // State.New is this port's addition - FSRS itself has only Learning/Review/Relearning. Promote it
        // before scheduling, so a New card that already carries memory state (imported, or restored from a
        // backup) takes the ordinary Learning path instead of falling through to the "unknown state" throw.
        if (nextCard.State == State.New)
        {
            nextCard.State = State.Learning;
            nextCard.Step ??= 0;
        }

        UpdateMemoryState(nextCard, card.LastReview, now, rating);

        TimeSpan interval = ScheduleNextInterval(nextCard, rating);

        if (_config.EnableFuzzing && nextCard.State == State.Review)
        {
            interval = _fuzzer.ApplyFuzz(interval, _config.MaximumInterval);
        }

        nextCard.LastReview = now;
        nextCard.Due = now + interval;

        var log = new ReviewLog(nextCard.CardId, rating, now, reviewDuration);
        return new ReviewResult()
        {
            Card = nextCard,
            ReviewLog = log,
        };
    }

    public double GetCardRetrievability(Card card, DateTimeOffset? currentDateTime = null)
    {
        if (card.LastReview is null || card.Stability is null)
        {
            return 0;
        }

        var now = currentDateTime ?? DateTimeOffset.UtcNow;
        return _calc.Retrievability(
            ElapsedWholeDays(card.LastReview.Value, now), card.Stability.Value, _calc.Decay, _calc.Factor);
    }

    /// <summary>
    /// Whole days between two reviews. FSRS weights are fitted on day-granular elapsed time, so this
    /// truncates rather than using the exact fraction: feeding 3.9 days where the model expects 3
    /// understates retrievability and inflates the stability gain on every long-term review.
    /// </summary>
    private static double ElapsedWholeDays(DateTimeOffset lastReview, DateTimeOffset now) =>
        Math.Max(0, Math.Floor((now - lastReview).TotalDays));

    private void UpdateMemoryState(Card card, DateTimeOffset? lastReview, DateTimeOffset now, Rating rating)
    {
        // First review - there is no prior memory state to build on.
        if (card.Stability is null || card.Difficulty is null)
        {
            card.Stability = _calc.InitialStability(rating);
            card.Difficulty = _calc.InitialDifficulty(rating);
            return;
        }

        // A missing last review is not "zero days ago": there is no interval to measure at all, which the
        // reference treats as a long-term review with R = 0 rather than as a same-day one.
        double? daysSinceLastReview = lastReview.HasValue
            ? ElapsedWholeDays(lastReview.Value, now)
            : null;

        if (daysSinceLastReview is < 1)
        {
            card.Stability = _calc.ShortTermStability(card.Stability.Value, rating);
            card.Difficulty = _calc.NextDifficulty(card.Difficulty.Value, rating);
            return;
        }

        // Read retrievability off the card before its stability is overwritten below.
        double r = GetCardRetrievability(card, now);

        card.Stability = _calc.NextStability(card.Difficulty.Value, card.Stability.Value, r, rating);
        card.Difficulty = _calc.NextDifficulty(card.Difficulty.Value, rating);
    }

    private TimeSpan ScheduleNextInterval(Card card, Rating rating)
    {
        switch (card.State)
        {
            case State.Learning:
                return ProcessSteps(card, rating, _config.LearningSteps);

            case State.Review:
                return ProcessReviewState(card, rating);

            case State.Relearning:
                return ProcessSteps(card, rating, _config.RelearningSteps);

            default:
                throw new InvalidOperationException(
                    $"State {card.State} is not schedulable by FSRS. Expected New, Learning, Review or Relearning.");
        }
    }

    /// <summary>
    /// Shared step ladder for the Learning and Relearning states - the two differ only in which step
    /// array they walk.
    /// </summary>
    private TimeSpan ProcessSteps(Card card, Rating rating, TimeSpan[] steps)
    {
        int step = card.Step ?? 0;

        // Graduate when there are no steps at all, or when the card carries a step index from a scheduler
        // configured with more steps than this one. Without the second clause a Hard rating would index
        // past the end of the array.
        if (steps.Length == 0 || (step >= steps.Length && rating != Rating.Again))
        {
            return GraduateToReview(card);
        }

        switch (rating)
        {
            case Rating.Again:
                card.Step = 0;
                return steps[0];

            case Rating.Hard:
                // The step stays where it is; only the delay changes.
                if (step == 0)
                {
                    return steps.Length >= 2
                        ? (steps[0] + steps[1]) / 2.0
                        : steps[0] * 1.5;
                }

                return steps[step];

            case Rating.Good:
                if (step + 1 >= steps.Length)
                {
                    return GraduateToReview(card);
                }

                card.Step = step + 1;
                return steps[step + 1];

            case Rating.Easy:
                return GraduateToReview(card);

            default:
                throw new ArgumentOutOfRangeException(nameof(rating), rating, "Unknown rating.");
        }
    }

    private TimeSpan ProcessReviewState(Card card, Rating rating)
    {
        if (rating == Rating.Again && _config.RelearningSteps.Length > 0)
        {
            card.State = State.Relearning;
            card.Step = 0;
            return _config.RelearningSteps[0];
        }

        // With no relearning steps configured a lapse keeps the card in the Review state.
        return CalculateReviewInterval(card.Stability!.Value);
    }

    private TimeSpan GraduateToReview(Card card)
    {
        card.State = State.Review;
        card.Step = null;
        return CalculateReviewInterval(card.Stability!.Value);
    }

    private TimeSpan CalculateReviewInterval(double stability)
    {
        double days = _calc.NextInterval(
            stability,
            _config.DesiredRetention,
            _calc.Decay,
            _calc.Factor,
            _config.MaximumInterval);

        return TimeSpan.FromDays(days);
    }
}
