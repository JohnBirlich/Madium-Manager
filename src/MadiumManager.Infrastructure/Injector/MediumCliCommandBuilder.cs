using MadiumManager.Application.Injector;
using MadiumManager.Application.ProcessManagement;

namespace MadiumManager.Infrastructure.Injector;

/// <summary>
/// Example Medium CLI adapter. The switch names are an explicit integration contract
/// placeholder: replace them with Medium's documented CLI before production use.
/// There is intentionally no target/game executable path in this builder.
/// </summary>
public sealed class MediumCliCommandBuilder : IMediumCommandBuilder
{
    private readonly string _mediumExecutablePath;
    private readonly string _workingDirectory;

    public MediumCliCommandBuilder(string mediumExecutablePath, string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(mediumExecutablePath))
        {
            throw new ArgumentException("Medium executable path is required.", nameof(mediumExecutablePath));
        }

        _mediumExecutablePath = mediumExecutablePath;
        _workingDirectory = workingDirectory ?? Path.GetDirectoryName(mediumExecutablePath) ?? string.Empty;
    }

    public MediumLaunchCommand Build(MediumLaunchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // TODO: Confirm these example switches against the supported Medium interface.
        var arguments = new List<string>
        {
            "--account-id",
            context.AccountId
        };

        if (context.Mode == MediumLaunchMode.Reconnect)
        {
            if (context.Server is null)
            {
                throw new InvalidOperationException("A confirmed server is required for Medium reconnect.");
            }

            arguments.Add("--reconnect-ip");
            arguments.Add(context.Server.Address.ToString());
            arguments.Add("--reconnect-port");
            arguments.Add(context.Server.Port.ToString());
        }

        return new MediumLaunchCommand(_mediumExecutablePath, _workingDirectory, arguments);
    }
}
