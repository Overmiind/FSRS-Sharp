using FsrsSharp.Models;

namespace FsrsSharp;

public interface IScheduler
{
    ReviewResult ReviewCard(Card card, Rating rating, DateTimeOffset? reviewDatetime = null, long? reviewDuration = null);
    double GetCardRetrievability(Card card);
}