namespace MadiumManager.Application.Injector;

public enum WatchdogExitReason
{
    StartupTimeout,
    LaunchFailed,
    StartupRejected,
    ExitedBeforeReady,
    ServerStateUnavailable,
    RestartLimitExceeded
}

/// <summary>
/// A terminal result for the account coordinator. StartupTimeout should normally
/// quarantine/mark the account and advance the queue rather than retrying forever.
/// </summary>
public sealed record WatchdogResult(
    WatchdogExitReason Reason,
    int Attempts,
    int? LastExitCode);
