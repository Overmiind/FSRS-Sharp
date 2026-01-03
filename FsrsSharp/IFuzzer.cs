namespace FsrsSharp;

public interface IFuzzer
{
    TimeSpan ApplyFuzz(TimeSpan interval, int maxInterval);
}