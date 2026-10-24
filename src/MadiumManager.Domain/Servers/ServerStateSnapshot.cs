namespace MadiumManager.Domain.Servers;

public enum ServerStateSource
{
    MediumApi,
    MediumLog,
    MediumConfig,
    MediumReadinessProbe,
    Manual
}

/// <summary>
/// The last explicitly confirmed server state. SessionId prevents an automatic
/// reconnect from reusing an endpoint captured during an older account session.
/// </summary>
public sealed record ServerStateSnapshot(
    string AccountId,
    string SessionId,
    ServerEndpoint Endpoint,
    DateTimeOffset CapturedAt,
    ServerStateSource Source);
