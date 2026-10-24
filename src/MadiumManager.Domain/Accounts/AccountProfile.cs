namespace MadiumManager.Domain.Accounts;

/// <summary>
/// Non-secret account metadata shown by the desktop account manager.
/// Credentials and tokens must be kept in a dedicated secret store.
/// </summary>
public sealed record AccountProfile(
    string Id,
    string DisplayName,
    string Login,
    string Note,
    DateTimeOffset CreatedAt);
