namespace FsrsSharp.Models;

public sealed class ReviewLog(
    Guid cardId,
    Rating rating,
    DateTimeOffset reviewDatetime,
    long? reviewDuration)
{
    /// <summary>
    /// The id of the card being reviewed.
    /// </summary>
    public Guid CardId { get; private set; } = cardId;

    /// <summary>
    /// The rating given to the card during the review.
    /// </summary>
    public Rating Rating { get; private set; } = rating;

    /// <summary>
    /// The date and time of the review.
    /// </summary>
    public DateTimeOffset ReviewDatetime { get; private set; } = reviewDatetime;

    /// <summary>
    /// The number of milliseconds it took to review the card or None if unspecified.
    /// </summary>
    public long? ReviewDuration { get; private set; } = reviewDuration;
}