using FsrsSharp.Configuration;
using FsrsSharp.Models;

namespace FsrsSharp.Core;

public class Scheduler : IScheduler
{
    private readonly FsrsConfig _config;
    private readonly IFsrsCalculator _calc;
    private readonly IFuzzer _fuzzer;

    public Scheduler() : this(new FsrsConfig(), new FsrsCalculator(new FsrsParameters()), new Fuzzer())
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
        var now = reviewDatetime ?? DateTimeOffset.UtcNow;
        var nextCard = card.Copy();

        UpdateMemoryState(nextCard, card.LastReview, now, rating);

        TimeSpan interval = ScheduleNextInterval(nextCard, rating);

        if (_config.EnableFuzzing && nextCard.State == State.Review)
        {
            interval = _fuzzer.ApplyFuzz(interval, _config.MaximumInterval);
        }

        nextCard.LastReview = now;
        nextCard.Due = now + interval;

        var log = new ReviewLog(nextCard.CardId, rating, now, (int?)reviewDuration);
        return new ReviewResult()
        {
            Card = nextCard,
            ReviewLog = log,
        };
    }

    public double GetCardRetrievability(Card card)
    {
        if (card.LastReview is null || card.Stability is null)
        {
            return 0;
        }

        var elapsedDays = Math.Max(0, (DateTime.UtcNow - card.LastReview.Value).TotalDays);
        return _calc.Retrievability(elapsedDays, card.Stability.Value, _config.Parameters.Decay,
            _config.Parameters.Factor);
    }

    private void UpdateMemoryState(Card card, DateTimeOffset? lastReview, DateTimeOffset now, Rating rating)
    {
        double elapsedDays = lastReview.HasValue ? (now - lastReview.Value).TotalDays : 0;

        // First review (new card)
        if (card.Stability == null || card.Difficulty == null)
        {
            card.Stability = _calc.InitialStability(rating);
            card.Difficulty = _calc.InitialDifficulty(rating);
            return;
        }

        if (elapsedDays < 1)
        {
            card.Stability = _calc.ShortTermStability(card.Stability.Value, rating);
            card.Difficulty = _calc.NextDifficulty(card.Difficulty.Value, rating);
            return;
        }

        double r = _calc.Retrievability(elapsedDays, card.Stability.Value, _config.Parameters.Decay,
            _config.Parameters.Factor);

        card.Stability = _calc.NextStability(card.Difficulty.Value, card.Stability.Value, r, rating);
        card.Difficulty = _calc.NextDifficulty(card.Difficulty.Value, rating);
    }

    private TimeSpan ScheduleNextInterval(Card card, Rating rating)
    {
        switch (card.State)
        {
            case State.Learning:
                return ProcessLearningState(card, rating);

            case State.Review:
                return ProcessReviewState(card, rating);

            case State.Relearning:
                return ProcessRelearningState(card, rating);

            default:
                throw new InvalidOperationException($"Unknown State: {card.State}");
        }
    }

    private TimeSpan ProcessLearningState(Card card, Rating rating)
    {
        if (_config.LearningSteps.Length == 0)
        {
            return GraduateToReview(card);
        }

        switch (rating)
        {
            case Rating.Again:
                card.Step = 0;
                return _config.LearningSteps[0];

            case Rating.Hard:
                if ((card.Step ?? 0) == 0)
                {
                    if (_config.LearningSteps.Length >= 2)
                        return (_config.LearningSteps[0] + _config.LearningSteps[1]) / 2.0;
                    if (_config.LearningSteps.Length == 1)
                        return _config.LearningSteps[0] * 1.5;
                }

                return _config.LearningSteps[card.Step!.Value];

            case Rating.Good:
                card.Step = (card.Step ?? 0) + 1;
                if (card.Step >= _config.LearningSteps.Length)
                {
                    return GraduateToReview(card);
                }

                return _config.LearningSteps[card.Step.Value];

            case Rating.Easy:
                return GraduateToReview(card);

            default:
                throw new ArgumentOutOfRangeException(nameof(rating));
        }
    }

    private TimeSpan ProcessReviewState(Card card, Rating rating)
    {
        if (rating == Rating.Again)
        {
            if (_config.RelearningSteps.Length == 0)
            {
                return CalculateReviewInterval(card.Stability!.Value);
            }

            card.State = State.Relearning;
            card.Step = 0;
            return _config.RelearningSteps[0];
        }

        return CalculateReviewInterval(card.Stability!.Value);
    }

    private TimeSpan ProcessRelearningState(Card card, Rating rating)
    {
        if (_config.RelearningSteps.Length == 0)
        {
            return GraduateToReview(card);
        }

        switch (rating)
        {
            case Rating.Again:
                card.Step = 0;
                return _config.RelearningSteps[0];

            case Rating.Hard:
                if ((card.Step ?? 0) == 0)
                {
                    if (_config.RelearningSteps.Length >= 2)
                        return (_config.RelearningSteps[0] + _config.RelearningSteps[1]) / 2.0;
                    if (_config.RelearningSteps.Length == 1)
                        return _config.RelearningSteps[0] * 1.5;
                }

                return _config.RelearningSteps[card.Step!.Value];

            case Rating.Good:
                card.Step = (card.Step ?? 0) + 1;
                if (card.Step >= _config.RelearningSteps.Length)
                {
                    return GraduateToReview(card);
                }

                return _config.RelearningSteps[card.Step.Value];

            case Rating.Easy:
                return GraduateToReview(card);

            default:
                throw new ArgumentOutOfRangeException(nameof(rating));
        }
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
            _config.Parameters.Decay,
            _config.Parameters.Factor,
            _config.MaximumInterval);

        return TimeSpan.FromDays(days);
    }
}