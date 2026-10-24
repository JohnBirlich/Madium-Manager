using MadiumManager.Desktop.Models;
using MadiumManager.Desktop.ViewModels;
using MadiumManager.Domain.Accounts;

namespace MadiumManager.Desktop.Services;

/// <summary>
/// Deterministic interactive runtime for UI evaluation. It never starts an OS process.
/// The application displays a persistent DEMO badge while this service is in use.
/// </summary>
public sealed class DemoAccountRuntimeService : IAccountRuntimeService
{
    private static readonly string[] DemoServers =
    [
        "203.0.113.10:27015",
        "203.0.113.24:27016",
        "[2001:db8::20]:27015"
    ];

    public async Task CheckAsync(AccountItemViewModel account, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(650), cancellationToken).ConfigureAwait(true);
        account.Health = account.Login.Contains("ban", StringComparison.OrdinalIgnoreCase)
            ? AccountServerStatus.Banned
            : AccountServerStatus.Active;
        account.LastChecked = DateTimeOffset.UtcNow;
    }

    public async Task<bool> StartAsync(
        AccountItemViewModel account,
        bool checkBeforeStart,
        int startupTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        account.LastError = null;
        try
        {
            if (checkBeforeStart)
            {
                account.SessionState = SessionState.Checking;
                await CheckAsync(account, cancellationToken).ConfigureAwait(true);
                if (account.Health != AccountServerStatus.Active)
                {
                    account.SessionState = SessionState.Faulted;
                    account.LastError = "Запуск пропущен: аккаунт не прошёл server-side проверку.";
                    return false;
                }
            }

            account.SessionState = SessionState.Starting;
            await Task.Delay(TimeSpan.FromMilliseconds(1100), cancellationToken).ConfigureAwait(true);

            if (account.Login.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                account.SessionState = SessionState.Faulted;
                account.LastError = $"Medium не подтвердил readiness за {startupTimeoutSeconds} секунд (демонстрационный сценарий).";
                return false;
            }

            account.Server = GetDemoServer(account.Login);
            account.StartedAt = DateTimeOffset.UtcNow;
            account.RestartCount = 0;
            account.SessionState = SessionState.Running;
            return true;
        }
        catch (OperationCanceledException)
        {
            account.StartedAt = null;
            account.UptimeDisplay = "00:00:00";
            account.SessionState = SessionState.Stopped;
            throw;
        }
    }

    public async Task StopAsync(AccountItemViewModel account, CancellationToken cancellationToken)
    {
        if (account.SessionState == SessionState.Stopped)
        {
            return;
        }

        account.SessionState = SessionState.Stopping;
        await Task.Delay(TimeSpan.FromMilliseconds(450), cancellationToken).ConfigureAwait(true);
        account.StartedAt = null;
        account.UptimeDisplay = "00:00:00";
        account.LastError = null;
        account.SessionState = SessionState.Stopped;
    }

    public async Task<bool> SimulateCrashAsync(
        AccountItemViewModel account,
        int maxAutomaticRestarts,
        CancellationToken cancellationToken)
    {
        if (account.SessionState != SessionState.Running)
        {
            return false;
        }

        try
        {
            account.SessionState = SessionState.Reconnecting;
            account.RestartCount++;
            if (account.RestartCount > maxAutomaticRestarts)
            {
                account.SessionState = SessionState.Faulted;
                account.LastError = $"Достигнут лимит автоматических перезапусков: {maxAutomaticRestarts}.";
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(1400), cancellationToken).ConfigureAwait(true);

            if (account.Server is null)
            {
                account.SessionState = SessionState.Faulted;
                account.LastError = "Не найден подтверждённый сервер текущей сессии.";
                return false;
            }

            account.StartedAt = DateTimeOffset.UtcNow;
            account.SessionState = SessionState.Running;
            return true;
        }
        catch (OperationCanceledException)
        {
            account.StartedAt = null;
            account.UptimeDisplay = "00:00:00";
            account.SessionState = SessionState.Stopped;
            throw;
        }
    }

    private static string GetDemoServer(string login)
    {
        var index = Math.Abs(login.Sum(character => character)) % DemoServers.Length;
        return DemoServers[index];
    }
}
