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

public class FsrsParameters
{
    public double StabilityMin = 0.001;
    public double StabilityMax = 100.0;
    public double DifficultyMin = 1;
    public double DifficultyMax = 10.0;
    public double FsrsDefaultDecay = 0.1542;
    public double Decay => -Weights[20];
    public double Factor => Math.Pow(0.9, 1.0 / Decay) - 1;
    public double[] Weights;
    public readonly double[] Defaults;
    public readonly double[] LowerBounds;
    public readonly double[] UpperBounds;

    public FsrsParameters(
        double[]? weights = null, 
        double[]? defaults = null, 
        double[]? lowerBounds = null,
        double[]? upperBounds = null)
    {
        Defaults = defaults ?? GetDefaults();
        Weights = weights ?? Defaults;
        LowerBounds = lowerBounds ?? GetLowerBounds();
        UpperBounds = upperBounds ?? GetUpperBounds();
        Validate();
    }

    private double[] GetUpperBounds()
    {
        return
        [
            StabilityMax, StabilityMax, StabilityMax, StabilityMax, 10.0, 4.0, 4.0, 0.75, 4.5, 0.8,
            3.5, 5.0, 0.25, 0.9, 4.0, 1.0, 6.0, 2.0, 2.0, 0.8, 0.8,
        ];
    }

    private double[] GetLowerBounds()
    {
        return
        [
            StabilityMin, StabilityMin, StabilityMin, StabilityMin, 1.0, 0.001, 0.001, 0.001,
            0.0, 0.0, 0.001, 0.001, 0.001, 0.001, 0.0, 0.0,
            1.0, 0.0, 0.0, 0.0, 0.1
        ];
    }

    private double[] GetDefaults()
    {
        return
        [
            0.212, 1.2931, 2.3065, 8.2956, 6.4133, 0.8334, 3.0194, 0.001,
            1.8722, 0.1666, 0.796, 1.4835, 0.0614, 0.2629, 1.6483, 0.6014,
            1.8729, 0.5425, 0.0912, 0.0658, 0.1542
        ];
    }

    private void Validate()
    {
        if (Weights.Length != LowerBounds.Length)
            throw new ArgumentException(
                $"Expected parameters count mismatch. Expected {LowerBounds.Length}, got {Weights.Length}");

        for (int i = 0; i < Weights.Length; i++)
        {
            if (Weights[i] < LowerBounds[i] || Weights[i] > UpperBounds[i])
            {
                throw new ArgumentException(
                    $"Parameter[{i}]={Weights[i]} is out of bounds. Range: ({LowerBounds[i]}, {UpperBounds[i]})");
            }
        }
    }
}