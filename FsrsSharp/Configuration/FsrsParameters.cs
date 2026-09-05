namespace FsrsSharp.Configuration;

public class FsrsParameters
{
    public const double StabilityMin = 0.001;
    public const double StabilityMax = 100.0;
    public const double DifficultyMin = 1.0;
    public const double DifficultyMax = 10.0;
    public const double FsrsDefaultDecay = 0.1542;

    private readonly double[] _weights;

    /// <summary>
    /// The 21 FSRS-6 model weights. Exposed read-only: the bounds check in <see cref="Validate"/> runs
    /// once at construction, so letting callers swap or edit the array afterwards would silently admit
    /// out-of-range weights — and a wrong-length one would throw deep inside the scheduler instead.
    /// </summary>
    public IReadOnlyList<double> Weights => _weights;

    public IReadOnlyList<double> Defaults { get; }
    public IReadOnlyList<double> LowerBounds { get; }
    public IReadOnlyList<double> UpperBounds { get; }

    /// <summary>Precomputed once — these are read on every interval and retrievability calculation.</summary>
    public double Decay { get; }

    public double Factor { get; }

    public FsrsParameters(
        double[]? weights = null,
        double[]? defaults = null,
        double[]? lowerBounds = null,
        double[]? upperBounds = null)
    {
        var defaultWeights = defaults ?? GetDefaults();
        // Every array is cloned. A caller keeping a reference to one it passed in could otherwise edit it
        // after Validate has run, which is the one thing this type promises cannot happen.
        Defaults = (double[])defaultWeights.Clone();
        _weights = (double[])(weights ?? defaultWeights).Clone();
        LowerBounds = (double[])(lowerBounds ?? GetLowerBounds()).Clone();
        UpperBounds = (double[])(upperBounds ?? GetUpperBounds()).Clone();
        Validate();

        Decay = -_weights[20];
        Factor = Math.Pow(0.9, 1.0 / Decay) - 1;
    }

    private static double[] GetUpperBounds()
    {
        return
        [
            StabilityMax, StabilityMax, StabilityMax, StabilityMax, 10.0, 4.0, 4.0, 0.75, 4.5, 0.8,
            3.5, 5.0, 0.25, 0.9, 4.0, 1.0, 6.0, 2.0, 2.0, 0.8, 0.8,
        ];
    }

    private static double[] GetLowerBounds()
    {
        return
        [
            StabilityMin, StabilityMin, StabilityMin, StabilityMin, 1.0, 0.001, 0.001, 0.001,
            0.0, 0.0, 0.001, 0.001, 0.001, 0.001, 0.0, 0.0,
            1.0, 0.0, 0.0, 0.0, 0.1
        ];
    }

    private static double[] GetDefaults()
    {
        return
        [
            0.212, 1.2931, 2.3065, 8.2956, 6.4133, 0.8334, 3.0194, 0.001,
            1.8722, 0.1666, 0.796, 1.4835, 0.0614, 0.2629, 1.6483, 0.6014,
            1.8729, 0.5425, 0.0912, 0.0658, FsrsDefaultDecay
        ];
    }

    private void Validate()
    {
        if (_weights.Length != LowerBounds.Count || _weights.Length != UpperBounds.Count)
            throw new ArgumentException(
                $"Expected parameters count mismatch. Expected {LowerBounds.Count}, got {_weights.Length}");

        for (int i = 0; i < _weights.Length; i++)
        {
            if (_weights[i] < LowerBounds[i] || _weights[i] > UpperBounds[i])
            {
                throw new ArgumentException(
                    $"Parameter[{i}]={_weights[i]} is out of bounds. Range: [{LowerBounds[i]}, {UpperBounds[i]}]");
            }
        }
    }
}
