namespace Gatehouse.Agent.Transport;

public sealed class Backoff(Random? random = null)
{
    private static readonly TimeSpan Max = TimeSpan.FromSeconds(60);

    private readonly Random _random = random ?? Random.Shared;

    public int Attempt { get; private set; }

    /// <summary>1 s doubling to 60 s, with +-20% jitter, never above 60 s.</summary>
    public TimeSpan Next(TimeSpan? retryAfter = null)
    {
        var baseSeconds = Math.Min(Math.Pow(2, Attempt), Max.TotalSeconds);
        Attempt = Math.Min(Attempt + 1, 30);
        var delay = TimeSpan.FromSeconds(baseSeconds * (0.8 + (0.4 * _random.NextDouble())));
        if (delay > Max)
        {
            delay = Max;
        }

        return retryAfter is { } ra && ra > delay ? ra : delay;
    }

    public void Reset() => Attempt = 0;
}
