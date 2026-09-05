namespace FsrsSharp.Configuration;

public class FsrsConfig
{
    public double DesiredRetention { get; init; } = 0.9;
    public int MaximumInterval { get; init; } = 36500;
    public bool EnableFuzzing { get; init; } = true;
    public TimeSpan[] LearningSteps { get; init; } = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10)];
    public TimeSpan[] RelearningSteps { get; init; } = [TimeSpan.FromMinutes(10)];

    public FsrsParameters Parameters { get; init; } = new();
}