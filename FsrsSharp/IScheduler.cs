using FsrsSharp.Models;

namespace FsrsSharp;

public interface IScheduler
{
    ReviewResult ReviewCard(Card card, Rating rating, DateTimeOffset? reviewDatetime = null, long? reviewDuration = null);

    /// <summary>
    /// Predicted probability that the card is recalled at <paramref name="currentDateTime"/>
    /// (default: now). Pass the time explicitly to keep callers deterministic and testable.
    /// </summary>
    double GetCardRetrievability(Card card, DateTimeOffset? currentDateTime = null);
}
