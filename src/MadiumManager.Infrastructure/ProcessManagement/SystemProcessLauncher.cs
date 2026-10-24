using System.Diagnostics;
using MadiumManager.Application.ProcessManagement;

namespace MadiumManager.Infrastructure.ProcessManagement;

public sealed class SystemProcessLauncher : IProcessLauncher
{
    public Task<IProcessHandle> StartAsync(
        MediumLaunchCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Path.IsPathFullyQualified(command.ExecutablePath))
        {
            throw new ArgumentException("The Medium executable path must be absolute.", nameof(command));
        }

        if (!File.Exists(command.ExecutablePath))
        {
            throw new FileNotFoundException("Medium executable was not found.", command.ExecutablePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = command.ExecutablePath,
            WorkingDirectory = command.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // ArgumentList prevents an endpoint/account value from becoming extra switches.
        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Process.Start returned false for Medium.");
            }

            return Task.FromResult<IProcessHandle>(new SystemProcessHandle(process));
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }
}
