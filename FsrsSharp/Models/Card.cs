namespace FsrsSharp.Models;

/// <summary>
/// Represents a flashcard in the FSRS system.
/// </summary>
public sealed class Card
{
    public Guid CardId { get; internal set; }
    public State State { get; internal set; }
    public int? Step { get; internal set; }
    public double? Stability { get; internal set; }
    public double? Difficulty { get; internal set; }
    public DateTimeOffset Due { get; internal set; }
    public DateTimeOffset? LastReview { get; internal set; }

    public Card(
        Guid? cardId = null,
        State state = State.Learning,
        int? step = null,
        double? stability = null,
        double? difficulty = null,
        DateTimeOffset? due = null,
        DateTimeOffset? lastReview = null)
    {
        CardId = cardId ?? Guid.NewGuid();
        State = state;

        if (State == State.Learning && step is null)
        {
            step = 0;
        }

        Step = step;
        Stability = stability;
        Difficulty = difficulty;
        Due = due ?? DateTimeOffset.UtcNow;
        LastReview = lastReview;
    }

    public Card Copy()
    {
        return new Card()
        {
            CardId = CardId,
            State = State,
            Step = Step,
            Stability = Stability,
            Difficulty = Difficulty,
            Due = Due,
            LastReview = LastReview,
        };
    }
}