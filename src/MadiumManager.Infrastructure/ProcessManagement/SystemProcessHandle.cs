using System.ComponentModel;
using System.Diagnostics;
using MadiumManager.Application.ProcessManagement;

namespace MadiumManager.Infrastructure.ProcessManagement;

public sealed class SystemProcessHandle : IProcessHandle
{
    private readonly Process _process;
    private int _disposed;

    internal SystemProcessHandle(Process process)
    {
        _process = process;
    }

    public int Id => _process.Id;

    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
    {
        await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return _process.ExitCode;
    }

    public async Task TerminateAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        try
        {
            if (_process.HasExited)
            {
                return;
            }

            // A GUI Medium build gets one bounded chance to close cleanly. Console
            // builds normally skip CloseMainWindow and go straight to owned-tree kill.
            if (_process.MainWindowHandle != IntPtr.Zero && _process.CloseMainWindow())
            {
                if (await TryWaitForExitAsync(timeout, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }

            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }

            await _process.WaitForExitAsync(cancellationToken)
                .WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // The process lost the HasExited/Kill race and is already gone.
        }
        catch (Win32Exception) when (HasExited())
        {
            // Same race at the Win32 boundary: termination is complete if it exited.
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _process.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private async Task<bool> TryWaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            await _process.WaitForExitAsync(cancellationToken)
                .WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private bool HasExited()
    {
        try
        {
            return _process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
}
