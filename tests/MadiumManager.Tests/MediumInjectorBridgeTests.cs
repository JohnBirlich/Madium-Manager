using System.Net;
using MadiumManager.Application.Injector;
using MadiumManager.Application.ProcessManagement;
using MadiumManager.Application.Servers;
using MadiumManager.Domain.Servers;
using MadiumManager.Infrastructure.Injector;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MadiumManager.Tests;

public sealed class MediumInjectorBridgeTests
{
    [Fact]
    public async Task Crash_RelaunchesOnlyMedium_WithSavedServer()
    {
        var endpoint = new ServerEndpoint(IPAddress.Parse("203.0.113.10"), 27_015);
        var launcher = new FakeProcessLauncher();
        var store = new InMemoryServerStateStore();
        var probe = new FakeReadinessProbe((_, _, _) =>
            Task.FromResult(MediumReadinessResult.Ready(endpoint, targetProcessId: 4321)));
        var bridge = CreateBridge(launcher, store, probe);
        using var cancellation = new CancellationTokenSource();

        var run = bridge.RunAsync(MediumRunRequest.Create("account-1"), cancellation.Token);

        // SaveConfirmedAsync happens before the attempt enters its Running wait.
        await store.FirstSave.Task.WaitAsync(TimeSpan.FromSeconds(2));
        launcher.Processes[0].Exit(17);
        await launcher.SecondStart.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, launcher.Commands.Count);
        Assert.All(launcher.Commands, command => Assert.Equal(@"C:\Medium\Medium.exe", command.ExecutablePath));

        var reconnect = launcher.Commands[1];
        Assert.Contains("--reconnect-ip", reconnect.Arguments);
        Assert.Contains(endpoint.Address.ToString(), reconnect.Arguments);
        Assert.Contains("--reconnect-port", reconnect.Arguments);
        Assert.Contains(endpoint.Port.ToString(), reconnect.Arguments);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.All(launcher.Processes, process => Assert.True(process.IsDisposed));
    }

    [Fact]
    public async Task StartupTimeout_ReturnsTerminalResult_AndCleansUpProcess()
    {
        var launcher = new FakeProcessLauncher();
        var store = new InMemoryServerStateStore();
        var probe = new FakeReadinessProbe(async (_, _, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return MediumReadinessResult.NotReady();
        });
        var bridge = CreateBridge(
            launcher,
            store,
            probe,
            new MediumBridgeOptions
            {
                StartupTimeout = TimeSpan.FromMilliseconds(75),
                StopTimeout = TimeSpan.FromMilliseconds(100),
                RestartDelay = TimeSpan.Zero
            });

        var result = await bridge.RunAsync(
            MediumRunRequest.Create("account-timeout"),
            CancellationToken.None);

        Assert.Equal(WatchdogExitReason.StartupTimeout, result.Reason);
        Assert.Equal(1, result.Attempts);
        var process = Assert.Single(launcher.Processes);
        Assert.True(process.WasTerminated);
        Assert.True(process.IsDisposed);
    }

    [Fact]
    public void DefaultStartupTimeout_IsTwentySeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(20), new MediumBridgeOptions().StartupTimeout);
    }

    private static MediumInjectorBridge CreateBridge(
        FakeProcessLauncher launcher,
        IServerStateStore store,
        IMediumReadinessProbe probe,
        MediumBridgeOptions? options = null) => new(
            launcher,
            probe,
            store,
            new MediumCliCommandBuilder(@"C:\Medium\Medium.exe"),
            options ?? new MediumBridgeOptions
            {
                StartupTimeout = TimeSpan.FromSeconds(1),
                StopTimeout = TimeSpan.FromMilliseconds(100),
                RestartDelay = TimeSpan.FromMilliseconds(1),
                StableRunDuration = TimeSpan.FromMinutes(1)
            },
            NullLogger<MediumInjectorBridge>.Instance);

    private sealed class FakeProcessLauncher : IProcessLauncher
    {
        public List<MediumLaunchCommand> Commands { get; } = [];

        public List<FakeProcess> Processes { get; } = [];

        public TaskCompletionSource<bool> SecondStart { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IProcessHandle> StartAsync(
            MediumLaunchCommand command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command);
            var process = new FakeProcess();
            Processes.Add(process);
            if (Processes.Count == 2)
            {
                SecondStart.TrySetResult(true);
            }

            return Task.FromResult<IProcessHandle>(process);
        }
    }

    private sealed class FakeReadinessProbe : IMediumReadinessProbe
    {
        private readonly Func<IProcessHandle, int, CancellationToken, Task<MediumReadinessResult>> _handler;
        private int _invocations;

        public FakeReadinessProbe(
            Func<IProcessHandle, int, CancellationToken, Task<MediumReadinessResult>> handler)
        {
            _handler = handler;
        }

        public Task<MediumReadinessResult> WaitForReadyAsync(
            IProcessHandle mediumProcess,
            CancellationToken cancellationToken)
        {
            var invocation = Interlocked.Increment(ref _invocations);
            return _handler(mediumProcess, invocation, cancellationToken);
        }
    }

    private sealed class FakeProcess : IProcessHandle
    {
        private readonly TaskCompletionSource<int> _exited =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Id { get; } = Random.Shared.Next(1, int.MaxValue);

        public bool WasTerminated { get; private set; }

        public bool IsDisposed { get; private set; }

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken) =>
            _exited.Task.WaitAsync(cancellationToken);

        public Task TerminateAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            WasTerminated = true;
            _exited.TrySetResult(-1);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }

        public void Exit(int exitCode)
        {
            _exited.TrySetResult(exitCode);
        }
    }

    private sealed class InMemoryServerStateStore : IServerStateStore
    {
        private readonly object _sync = new();
        private ServerStateSnapshot? _snapshot;
        private int _saveCount;

        public TaskCompletionSource<bool> FirstSave { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ServerStateSnapshot?> LoadLastConfirmedAsync(
            string accountId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                return Task.FromResult(_snapshot);
            }
        }

        public Task SaveConfirmedAsync(
            ServerStateSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                _snapshot = snapshot;
                if (Interlocked.Increment(ref _saveCount) == 1)
                {
                    FirstSave.TrySetResult(true);
                }
            }

            return Task.CompletedTask;
        }
    }
}
