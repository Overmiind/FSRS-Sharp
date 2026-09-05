namespace FsrsSharp.Models;

public sealed class ReviewResult
{
    public required Card Card { get; init; }
    public required ReviewLog ReviewLog { get; init; }
}
