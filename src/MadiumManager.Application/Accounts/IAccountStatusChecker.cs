using MadiumManager.Domain.Accounts;

namespace MadiumManager.Application.Accounts;

/// <summary>
/// Implemented by a provider-specific HTTP/profile adapter. Unknown and rate-limited
/// responses must never be converted into a false "banned" verdict.
/// </summary>
public interface IAccountStatusChecker
{
    Task<AccountStatusSnapshot> CheckAsync(string accountId, CancellationToken cancellationToken);
}
