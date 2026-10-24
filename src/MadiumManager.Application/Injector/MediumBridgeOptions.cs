namespace MadiumManager.Application.Injector;

public sealed record MediumBridgeOptions
{
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan RestartDelay { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan StableRunDuration { get; init; } = TimeSpan.FromMinutes(5);

    public int MaxAutomaticRestarts { get; init; } = 3;

    internal void Validate()
    {
        if (StartupTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(StartupTimeout));
        }

        if (StopTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(StopTimeout));
        }

        if (RestartDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(RestartDelay));
        }

        if (StableRunDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(StableRunDuration));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(MaxAutomaticRestarts);
    }
}
