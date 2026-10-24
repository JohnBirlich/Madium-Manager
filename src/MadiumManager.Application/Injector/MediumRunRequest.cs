namespace MadiumManager.Application.Injector;

public sealed record MediumRunRequest(string AccountId, string SessionId)
{
    public static MediumRunRequest Create(string accountId) =>
        new(accountId, Guid.NewGuid().ToString("N"));
}
