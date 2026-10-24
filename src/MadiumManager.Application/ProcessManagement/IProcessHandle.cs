namespace MadiumManager.Application.ProcessManagement;

/// <summary>
/// Owns exactly one OS process handle. Implementations must make DisposeAsync
/// idempotent and must not dispose the handle while an exit wait is still using it.
/// </summary>
public interface IProcessHandle : IAsyncDisposable
{
    int Id { get; }

    Task<int> WaitForExitAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Tries an orderly close first and then terminates only this launched process
    /// tree. The caller supplies a bounded cleanup timeout.
    /// </summary>
    Task TerminateAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
