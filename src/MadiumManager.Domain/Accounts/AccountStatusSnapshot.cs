namespace MadiumManager.Domain.Accounts;

public enum AccountServerStatus
{
    Unknown,
    Active,
    Banned,
    InvalidCredentials,
    RateLimited,
    ServiceUnavailable
}

public sealed record AccountStatusSnapshot(
    string AccountId,
    AccountServerStatus Status,
    DateTimeOffset CheckedAt,
    string? Diagnostic = null);
