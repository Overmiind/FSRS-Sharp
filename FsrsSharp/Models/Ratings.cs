namespace FsrsSharp.Models;

/// <summary>
/// Range guard for <see cref="Rating"/>.
/// </summary>
/// <remarks>
/// A .NET enum is just an <c>int</c>, and neither model binding nor <c>JsonStringEnumConverter</c>
/// range-checks a numeric value — <c>{"rating": 7}</c> deserializes happily. That matters here more
/// than for most enums because the rating is used as an ARRAY INDEX
/// (<c>Weights[(int)rating - 1]</c> in <see cref="Core.FsrsCalculator.InitialStability"/>): rating 0
/// indexes <c>[-1]</c> and throws, while rating 7 quietly reads w6 — the difficulty-delta weight —
/// and uses it as an initial stability. The first is a 500, the second is silent memory-state
/// corruption behind a 200.
///
/// Callers should validate at their own boundary so the client gets a useful message; these guards
/// exist so the library can never index out of its weight array regardless of who calls it.
/// </remarks>
public static class Ratings
{
    public static bool IsDefined(Rating rating) =>
        rating is Rating.Again or Rating.Hard or Rating.Good or Rating.Easy;

    /// <summary>Throws <see cref="ArgumentOutOfRangeException"/> unless the rating is one of the four.</summary>
    public static Rating Validate(Rating rating, string paramName = "rating")
    {
        if (!IsDefined(rating))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                rating,
                $"Rating must be Again ({(int)Rating.Again}), Hard ({(int)Rating.Hard}), " +
                $"Good ({(int)Rating.Good}) or Easy ({(int)Rating.Easy}).");
        }

        return rating;
    }

    /// <summary>
    /// The lower (worse) of two ratings, treating <c>null</c> as "nothing recorded yet".
    /// <see cref="Rating"/> is ordered Again &lt; Hard &lt; Good &lt; Easy, so this is a plain minimum.
    /// </summary>
    public static Rating Worst(Rating? left, Rating right) =>
        left is null ? right : (Rating)Math.Min((int)left.Value, (int)right);
}
