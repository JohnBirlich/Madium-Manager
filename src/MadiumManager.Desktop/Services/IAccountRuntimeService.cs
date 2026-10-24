using MadiumManager.Desktop.ViewModels;

namespace MadiumManager.Desktop.Services;

public interface IAccountRuntimeService
{
    Task CheckAsync(AccountItemViewModel account, CancellationToken cancellationToken);

    Task<bool> StartAsync(
        AccountItemViewModel account,
        bool checkBeforeStart,
        int startupTimeoutSeconds,
        CancellationToken cancellationToken);

    Task StopAsync(AccountItemViewModel account, CancellationToken cancellationToken);

    Task<bool> SimulateCrashAsync(
        AccountItemViewModel account,
        int maxAutomaticRestarts,
        CancellationToken cancellationToken);
}
