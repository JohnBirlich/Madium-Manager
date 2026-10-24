namespace MadiumManager.Application.ProcessManagement;

public sealed record MediumLaunchCommand(
    string ExecutablePath,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments);

public interface IProcessLauncher
{
    /// <summary>
    /// Starts a process without a shell. If cancellation is honored, an implementation
    /// must not leave a late-started process behind after returning or throwing.
    /// </summary>
    Task<IProcessHandle> StartAsync(MediumLaunchCommand command, CancellationToken cancellationToken);
}
