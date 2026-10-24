using MadiumManager.Domain.Accounts;

namespace MadiumManager.Application.Accounts;

public interface IAccountRepository
{
    Task<IReadOnlyList<AccountProfile>> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(IReadOnlyCollection<AccountProfile> accounts, CancellationToken cancellationToken);
}
