using MadiumManager.Application.ProcessManagement;
using MadiumManager.Domain.Servers;

namespace MadiumManager.Application.Injector;

public sealed record MediumReadinessResult(
    bool IsReady,
    ServerEndpoint? ConfirmedServer = null,
    int? TargetProcessId = null)
{
    public static MediumReadinessResult Ready(
        ServerEndpoint? confirmedServer = null,
        int? targetProcessId = null) => new(true, confirmedServer, targetProcessId);

    public static MediumReadinessResult NotReady() => new(false);
}

/// <summary>
/// Waits for a real readiness signal (Medium API/log handshake and, optionally,
/// target PID). Process.Start alone is not considered application readiness.
/// </summary>
public interface IMediumReadinessProbe
{
    Task<MediumReadinessResult> WaitForReadyAsync(
        IProcessHandle mediumProcess,
        CancellationToken cancellationToken);
}
