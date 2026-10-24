using MadiumManager.Desktop.Models;
using MadiumManager.Desktop.Services;
using MadiumManager.Desktop.ViewModels;
using MadiumManager.Domain.Accounts;
using Xunit;

namespace MadiumManager.Desktop.Tests;

public sealed class DemoAccountRuntimeServiceTests
{
    [Fact]
    public async Task Start_ChecksAccountAndEntersRunningState()
    {
        var service = new DemoAccountRuntimeService();
        var account = CreateAccount("demo.ok");

        var started = await service.StartAsync(account, true, 20, CancellationToken.None);

        Assert.True(started);
        Assert.Equal(AccountServerStatus.Active, account.Health);
        Assert.Equal(SessionState.Running, account.SessionState);
        Assert.NotNull(account.Server);
        Assert.NotNull(account.StartedAt);
    }

    [Fact]
    public async Task Start_RejectsBannedAccount()
    {
        var service = new DemoAccountRuntimeService();
        var account = CreateAccount("banned.demo");

        var started = await service.StartAsync(account, true, 20, CancellationToken.None);

        Assert.False(started);
        Assert.Equal(AccountServerStatus.Banned, account.Health);
        Assert.Equal(SessionState.Faulted, account.SessionState);
        Assert.NotNull(account.LastError);
    }

    [Fact]
    public async Task Crash_ReconnectsToTheSameServer()
    {
        var service = new DemoAccountRuntimeService();
        var account = CreateAccount("demo.reconnect");
        await service.StartAsync(account, true, 20, CancellationToken.None);
        var server = account.Server;

        var reconnected = await service.SimulateCrashAsync(account, 3, CancellationToken.None);

        Assert.True(reconnected);
        Assert.Equal(SessionState.Running, account.SessionState);
        Assert.Equal(server, account.Server);
        Assert.Equal(1, account.RestartCount);
    }

    [Fact]
    public async Task Crash_EnforcesConfiguredRestartLimit()
    {
        var service = new DemoAccountRuntimeService();
        var account = CreateAccount("demo.limit");
        await service.StartAsync(account, true, 20, CancellationToken.None);

        var reconnected = await service.SimulateCrashAsync(account, 0, CancellationToken.None);

        Assert.False(reconnected);
        Assert.Equal(SessionState.Faulted, account.SessionState);
        Assert.NotNull(account.LastError);
        Assert.Contains("лимит", account.LastError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Start_TimeoutScenarioUsesConfiguredTimeout()
    {
        var service = new DemoAccountRuntimeService();
        var account = CreateAccount("demo.timeout");

        var started = await service.StartAsync(account, true, 37, CancellationToken.None);

        Assert.False(started);
        Assert.Equal(SessionState.Faulted, account.SessionState);
        Assert.NotNull(account.LastError);
        Assert.Contains("37", account.LastError);
    }

    private static AccountItemViewModel CreateAccount(string login) => new()
    {
        DisplayName = "Test account",
        Login = login
    };
}
