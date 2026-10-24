using System.Diagnostics;
using MadiumManager.Application.ProcessManagement;
using MadiumManager.Application.Servers;
using MadiumManager.Domain.Servers;
using Microsoft.Extensions.Logging;

namespace MadiumManager.Application.Injector;

/// <summary>
/// Single-owner Medium process watchdog. It never starts the target/game executable:
/// every initial start and every reconnect is a new Medium launch command.
/// </summary>
public sealed class MediumInjectorBridge
{
    private readonly IProcessLauncher _processLauncher;
    private readonly IMediumReadinessProbe _readinessProbe;
    private readonly IServerStateStore _serverStateStore;
    private readonly IMediumCommandBuilder _commandBuilder;
    private readonly MediumBridgeOptions _options;
    private readonly ILogger<MediumInjectorBridge> _logger;
    private int _runState;

    public MediumInjectorBridge(
        IProcessLauncher processLauncher,
        IMediumReadinessProbe readinessProbe,
        IServerStateStore serverStateStore,
        IMediumCommandBuilder commandBuilder,
        MediumBridgeOptions options,
        ILogger<MediumInjectorBridge> logger)
    {
        _processLauncher = processLauncher ?? throw new ArgumentNullException(nameof(processLauncher));
        _readinessProbe = readinessProbe ?? throw new ArgumentNullException(nameof(readinessProbe));
        _serverStateStore = serverStateStore ?? throw new ArgumentNullException(nameof(serverStateStore));
        _commandBuilder = commandBuilder ?? throw new ArgumentNullException(nameof(commandBuilder));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options.Validate();
    }

    /// <summary>
    /// Runs until a terminal failure or cooperative cancellation. One bridge instance
    /// permits exactly one active run; the async loop is the only process-state owner.
    /// </summary>
    public async Task<WatchdogResult> RunAsync(
        MediumRunRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.AccountId))
        {
            throw new ArgumentException("Account id is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            throw new ArgumentException("Session id is required.", nameof(request));
        }

        if (Interlocked.CompareExchange(ref _runState, 1, 0) != 0)
        {
            throw new InvalidOperationException("This Medium bridge is already running.");
        }

        try
        {
            return await RunCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _runState, 0);
        }
    }

    private async Task<WatchdogResult> RunCoreAsync(
        MediumRunRequest request,
        CancellationToken cancellationToken)
    {
        var attempts = 0;
        var automaticRestarts = 0;
        var reconnect = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ServerStateSnapshot? snapshot = null;
            if (reconnect)
            {
                // This read happens after the crash and before every Medium relaunch.
                snapshot = await _serverStateStore
                    .LoadLastConfirmedAsync(request.AccountId, cancellationToken)
                    .ConfigureAwait(false);

                if (snapshot is null ||
                    !string.Equals(snapshot.SessionId, request.SessionId, StringComparison.Ordinal))
                {
                    _logger.LogError(
                        "Cannot reconnect account {AccountId}: no confirmed server for session {SessionId}.",
                        request.AccountId,
                        request.SessionId);

                    return new WatchdogResult(
                        WatchdogExitReason.ServerStateUnavailable,
                        attempts,
                        null);
                }
            }

            var command = _commandBuilder.Build(new MediumLaunchContext(
                request.AccountId,
                request.SessionId,
                reconnect ? MediumLaunchMode.Reconnect : MediumLaunchMode.Initial,
                snapshot?.Endpoint));

            attempts++;
            _logger.LogInformation(
                "Launching Medium for account {AccountId}. Attempt {Attempt}; reconnect={Reconnect}; server={Server}.",
                request.AccountId,
                attempts,
                reconnect,
                snapshot?.Endpoint.ToString() ?? "not-requested");

            var attempt = await ExecuteAttemptAsync(request, command, cancellationToken)
                .ConfigureAwait(false);

            switch (attempt.Outcome)
            {
                case AttemptOutcome.StartupTimeout:
                    _logger.LogError(
                        "Medium startup timed out after {TimeoutSeconds} seconds for account {AccountId}. " +
                        "The account coordinator should mark the account and advance the queue.",
                        _options.StartupTimeout.TotalSeconds,
                        request.AccountId);
                    return new WatchdogResult(WatchdogExitReason.StartupTimeout, attempts, null);

                case AttemptOutcome.LaunchFailed:
                    return new WatchdogResult(WatchdogExitReason.LaunchFailed, attempts, null);

                case AttemptOutcome.StartupRejected:
                    return new WatchdogResult(WatchdogExitReason.StartupRejected, attempts, null);

                case AttemptOutcome.ExitedBeforeReady:
                    _logger.LogWarning(
                        "Medium exited before readiness for account {AccountId}. ExitCode={ExitCode}.",
                        request.AccountId,
                        attempt.ExitCode);
                    return new WatchdogResult(
                        WatchdogExitReason.ExitedBeforeReady,
                        attempts,
                        attempt.ExitCode);

                case AttemptOutcome.UnexpectedExit:
                    automaticRestarts = attempt.RunningTime >= _options.StableRunDuration
                        ? 1
                        : automaticRestarts + 1;

                    if (automaticRestarts > _options.MaxAutomaticRestarts)
                    {
                        _logger.LogError(
                            "Automatic restart limit reached for account {AccountId}. Restarts={Restarts}.",
                            request.AccountId,
                            automaticRestarts - 1);
                        return new WatchdogResult(
                            WatchdogExitReason.RestartLimitExceeded,
                            attempts,
                            attempt.ExitCode);
                    }

                    reconnect = true;
                    _logger.LogWarning(
                        "Medium session exited for account {AccountId}. ExitCode={ExitCode}; " +
                        "reconnect {RestartNumber}/{MaxRestarts} will use Medium and the saved server.",
                        request.AccountId,
                        attempt.ExitCode,
                        automaticRestarts,
                        _options.MaxAutomaticRestarts);

                    await Task.Delay(_options.RestartDelay, cancellationToken).ConfigureAwait(false);
                    continue;

                default:
                    throw new InvalidOperationException($"Unknown attempt outcome: {attempt.Outcome}.");
            }
        }
    }

    private async Task<AttemptResult> ExecuteAttemptAsync(
        MediumRunRequest request,
        MediumLaunchCommand command,
        CancellationToken cancellationToken)
    {
        IProcessHandle? process = null;
        Task<int>? exitTask = null;
        Task<MediumReadinessResult>? readinessTask = null;

        using var startupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startupCts.CancelAfter(_options.StartupTimeout);

        try
        {
            process = await _processLauncher
                .StartAsync(command, startupCts.Token)
                .ConfigureAwait(false);

            // The process object/handle, rather than a process name or PID lookup,
            // remains the source of truth for this particular launch.
            exitTask = process.WaitForExitAsync(CancellationToken.None);
            readinessTask = _readinessProbe.WaitForReadyAsync(process, startupCts.Token);
            var timeoutTask = Task.Delay(Timeout.InfiniteTimeSpan, startupCts.Token);

            var completed = await Task.WhenAny(exitTask, readinessTask, timeoutTask)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (completed == timeoutTask)
            {
                return AttemptResult.TimedOut();
            }

            if (completed == exitTask)
            {
                return AttemptResult.ExitedBeforeReady(await exitTask.ConfigureAwait(false));
            }

            var readiness = await readinessTask.ConfigureAwait(false);
            if (!readiness.IsReady)
            {
                return AttemptResult.Rejected();
            }

            // Readiness won the startup race: disarm only the startup deadline.
            startupCts.CancelAfter(Timeout.InfiniteTimeSpan);

            if (readiness.ConfirmedServer is not null)
            {
                await _serverStateStore.SaveConfirmedAsync(new ServerStateSnapshot(
                        request.AccountId,
                        request.SessionId,
                        readiness.ConfirmedServer,
                        DateTimeOffset.UtcNow,
                        ServerStateSource.MediumReadinessProbe),
                    cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "Medium is ready for account {AccountId}. TargetProcessId={TargetProcessId}.",
                request.AccountId,
                readiness.TargetProcessId);

            var runningTime = Stopwatch.StartNew();
            var exitCode = await exitTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            runningTime.Stop();

            return AttemptResult.ExitedWhileRunning(exitCode, runningTime.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (startupCts.IsCancellationRequested)
        {
            return AttemptResult.TimedOut();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Medium launch/readiness failed for account {AccountId}.", request.AccountId);
            return AttemptResult.LaunchFailed();
        }
        finally
        {
            // Event tasks never initiate a restart themselves. Cancellation is scoped
            // to this attempt, so a stale probe cannot affect the next generation.
            try
            {
                if (!startupCts.IsCancellationRequested)
                {
                    startupCts.Cancel();
                }
            }
            catch (Exception exception)
            {
                // A faulty third-party cancellation callback must not skip process cleanup.
                _logger.LogError(exception, "A Medium startup cancellation callback failed.");
            }

            ObserveFault(readinessTask);
            ObserveFault(exitTask);

            if (process is not null)
            {
                await CleanupAsync(process).ConfigureAwait(false);
            }
        }
    }

    private async Task CleanupAsync(IProcessHandle process)
    {
        try
        {
            await process.TerminateAsync(_options.StopTimeout, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Dispose still has to run. An orphaned live process is logged loudly and
            // must be surfaced by a production health/telemetry adapter.
            _logger.LogError(exception, "Could not confirm orderly Medium process termination.");
        }

        try
        {
            await process.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not dispose the Medium process handle.");
        }
    }

    private static void ObserveFault(Task? task)
    {
        if (task is null)
        {
            return;
        }

        _ = task.ContinueWith(
            static completed => { _ = completed.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private enum AttemptOutcome
    {
        StartupTimeout,
        LaunchFailed,
        StartupRejected,
        ExitedBeforeReady,
        UnexpectedExit
    }

    private sealed record AttemptResult(
        AttemptOutcome Outcome,
        int? ExitCode,
        TimeSpan RunningTime)
    {
        public static AttemptResult TimedOut() => new(AttemptOutcome.StartupTimeout, null, TimeSpan.Zero);

        public static AttemptResult LaunchFailed() => new(AttemptOutcome.LaunchFailed, null, TimeSpan.Zero);

        public static AttemptResult Rejected() => new(AttemptOutcome.StartupRejected, null, TimeSpan.Zero);

        public static AttemptResult ExitedBeforeReady(int exitCode) =>
            new(AttemptOutcome.ExitedBeforeReady, exitCode, TimeSpan.Zero);

        public static AttemptResult ExitedWhileRunning(int exitCode, TimeSpan runningTime) =>
            new(AttemptOutcome.UnexpectedExit, exitCode, runningTime);
    }
}
