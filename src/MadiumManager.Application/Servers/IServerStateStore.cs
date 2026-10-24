using MadiumManager.Domain.Servers;

namespace MadiumManager.Application.Servers;

public interface IServerStateStore
{
    Task<ServerStateSnapshot?> LoadLastConfirmedAsync(string accountId, CancellationToken cancellationToken);

    Task SaveConfirmedAsync(ServerStateSnapshot snapshot, CancellationToken cancellationToken);
}
