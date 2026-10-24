using MadiumManager.Application.ProcessManagement;
using MadiumManager.Domain.Servers;

namespace MadiumManager.Application.Injector;

public enum MediumLaunchMode
{
    Initial,
    Reconnect
}

public sealed record MediumLaunchContext(
    string AccountId,
    string SessionId,
    MediumLaunchMode Mode,
    ServerEndpoint? Server);

public interface IMediumCommandBuilder
{
    MediumLaunchCommand Build(MediumLaunchContext context);
}
