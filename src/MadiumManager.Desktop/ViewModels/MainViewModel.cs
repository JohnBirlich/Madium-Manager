using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MadiumManager.Application.Accounts;
using MadiumManager.Desktop.Models;
using MadiumManager.Desktop.Services;
using MadiumManager.Domain.Accounts;

namespace MadiumManager.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IAccountRepository _accountRepository;
    private readonly ISettingsStore _settingsStore;
    private readonly IAccountRuntimeService _runtimeService;
    private readonly IUserDialogService _dialogService;
    private readonly Dictionary<string, CancellationTokenSource> _sessionCancellation = new();
    private readonly DispatcherTimer _uptimeTimer;

    [ObservableProperty]
    private AppPage _currentPage = AppPage.Dashboard;

    [ObservableProperty]
    private AccountItemViewModel? _selectedAccount;

    [ObservableProperty]
    private AppSettings _settings = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _eventSearchText = string.Empty;

    [ObservableProperty]
    private string _selectedLogLevel = "Все уровни";

    [ObservableProperty]
    private string _lastOperation = "Интерфейс готов к работе";

    [ObservableProperty]
    private int _totalAccounts;

    [ObservableProperty]
    private int _activeAccounts;

    [ObservableProperty]
    private int _runningAccounts;

    [ObservableProperty]
    private int _issueAccounts;

    [ObservableProperty]
    private bool _hasAccounts;

    [ObservableProperty]
    private bool _hasSessions;

    [ObservableProperty]
    private bool _hasEvents;

    public MainViewModel(
        IAccountRepository accountRepository,
        ISettingsStore settingsStore,
        IAccountRuntimeService runtimeService,
        IUserDialogService dialogService)
    {
        _accountRepository = accountRepository;
        _settingsStore = settingsStore;
        _runtimeService = runtimeService;
        _dialogService = dialogService;

        AccountsView = CollectionViewSource.GetDefaultView(Accounts);
        AccountsView.Filter = FilterAccount;
        AccountsView.SortDescriptions.Add(new SortDescription(
            nameof(AccountItemViewModel.DisplayName),
            ListSortDirection.Ascending));

        SessionsView = new CollectionViewSource { Source = Accounts }.View;
        SessionsView.Filter = item => item is AccountItemViewModel account && account.HasActiveSession;

        EventsView = CollectionViewSource.GetDefaultView(Events);
        EventsView.Filter = FilterEvent;

        _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uptimeTimer.Tick += (_, _) =>
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var account in Accounts)
            {
                account.Tick(now);
            }
        };
    }

    public ObservableCollection<AccountItemViewModel> Accounts { get; } = [];

    public ObservableCollection<LogEntryViewModel> Events { get; } = [];

    public ICollectionView AccountsView { get; }

    public ICollectionView SessionsView { get; }

    public ICollectionView EventsView { get; }

    public IReadOnlyList<string> LogLevelOptions { get; } =
        ["Все уровни", "Информация", "Успех", "Предупреждения", "Ошибки"];

    public string CurrentPageTitle => CurrentPage switch
    {
        AppPage.Accounts => "Аккаунты",
        AppPage.Events => "Журнал событий",
        AppPage.Settings => "Настройки",
        _ => "Обзор"
    };

    public string CurrentPageSubtitle => CurrentPage switch
    {
        AppPage.Accounts => "Пул аккаунтов, server-side статусы и управление сессиями",
        AppPage.Events => "Диагностика запуска, reconnect и действий пользователя",
        AppPage.Settings => "Medium, таймауты и поведение менеджера",
        _ => "Оперативное состояние менеджера и рабочих сессий"
    };

    public async Task InitializeAsync()
    {
        Settings = await _settingsStore.LoadAsync(CancellationToken.None);
        var profiles = await _accountRepository.LoadAsync(CancellationToken.None);
        foreach (var profile in profiles)
        {
            AddAccountToCollection(AccountItemViewModel.FromProfile(profile));
        }

        if (Accounts.Count == 0)
        {
            AddDemoAccounts();
            await SaveAccountsAsync();
        }

        SelectedAccount = Accounts.FirstOrDefault();
        RefreshViews();
        AddEvent(UiLogLevel.Information, "Интерактивный демонстрационный runtime запущен. OS-процессы не создаются.");
        _uptimeTimer.Start();
    }

    public void Shutdown()
    {
        _uptimeTimer.Stop();
        foreach (var cancellation in _sessionCancellation.Values.ToArray())
        {
            cancellation.Cancel();
        }
    }

    [RelayCommand]
    private void Navigate(AppPage page)
    {
        CurrentPage = page;
    }

    [RelayCommand]
    private Task AddAccountAsync() => RunSafelyAsync(async () =>
    {
        var result = _dialogService.ShowAccountEditor(null);
        if (result is null)
        {
            return;
        }

        if (IsDuplicateLogin(result.Login, null))
        {
            _dialogService.ShowError("Дубликат", "Аккаунт с таким логином уже существует.");
            return;
        }

        var account = new AccountItemViewModel
        {
            DisplayName = result.DisplayName,
            Login = result.Login,
            Note = result.Note
        };
        AddAccountToCollection(account);
        await SaveAccountsAsync();
        SelectedAccount = account;
        CurrentPage = AppPage.Accounts;
        RefreshViews();
        AddEvent(UiLogLevel.Success, "Аккаунт добавлен.", account.DisplayName);
        LastOperation = $"Добавлен аккаунт {account.DisplayName}";
    });

    [RelayCommand]
    private Task EditAccountAsync(AccountItemViewModel? account) => RunSafelyAsync(async () =>
    {
        account ??= SelectedAccount;
        if (account is null)
        {
            return;
        }

        if (account.HasActiveSession)
        {
            _dialogService.ShowError("Сессия активна", "Остановите сессию перед изменением аккаунта.");
            return;
        }

        var result = _dialogService.ShowAccountEditor(account.ToProfile());
        if (result is null)
        {
            return;
        }

        if (IsDuplicateLogin(result.Login, account.Id))
        {
            _dialogService.ShowError("Дубликат", "Аккаунт с таким логином уже существует.");
            return;
        }

        var loginChanged = !string.Equals(account.Login, result.Login, StringComparison.OrdinalIgnoreCase);
        account.DisplayName = result.DisplayName;
        account.Login = result.Login;
        account.Note = result.Note;
        if (loginChanged)
        {
            account.Health = AccountServerStatus.Unknown;
            account.LastChecked = null;
        }

        await SaveAccountsAsync();
        RefreshViews();
        AddEvent(UiLogLevel.Success, "Данные аккаунта обновлены.", account.DisplayName);
        LastOperation = "Изменения аккаунта сохранены";
    });

    [RelayCommand]
    private Task DeleteAccountAsync(AccountItemViewModel? account) => RunSafelyAsync(async () =>
    {
        account ??= SelectedAccount;
        if (account is null)
        {
            return;
        }

        if (account.HasActiveSession)
        {
            _dialogService.ShowError("Сессия активна", "Остановите сессию перед удалением аккаунта.");
            return;
        }

        if (!_dialogService.Confirm(
                "Удалить аккаунт",
                $"Удалить «{account.DisplayName}» из локального пула? Секреты из внешнего хранилища не затрагиваются."))
        {
            return;
        }

        account.PropertyChanged -= OnAccountPropertyChanged;
        Accounts.Remove(account);
        await SaveAccountsAsync();
        SelectedAccount = Accounts.FirstOrDefault();
        RefreshViews();
        AddEvent(UiLogLevel.Warning, "Аккаунт удалён из локального пула.", account.DisplayName);
        LastOperation = "Аккаунт удалён";
    });

    [RelayCommand]
    private async Task StartAccountAsync(AccountItemViewModel? account)
    {
        account ??= SelectedAccount;
        if (account is null || !account.CanStart)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        if (!_sessionCancellation.TryAdd(account.Id, cancellation))
        {
            cancellation.Dispose();
            return;
        }

        try
        {
            LastOperation = $"Запуск Medium для {account.DisplayName}";
            var started = await _runtimeService.StartAsync(
                account,
                Settings.CheckAccountsBeforeStart,
                Settings.StartupTimeoutSeconds,
                cancellation.Token);

            if (started)
            {
                AddEvent(
                    UiLogLevel.Success,
                    $"Medium запущен. Подтверждён сервер {account.ServerDisplay}.",
                    account.DisplayName);
                LastOperation = $"Сессия {account.DisplayName} работает";
            }
            else
            {
                AddEvent(UiLogLevel.Error, account.LastError ?? "Запуск отклонён.", account.DisplayName);
                LastOperation = "Аккаунт помечен и пропущен";
            }
        }
        catch (OperationCanceledException)
        {
            AddEvent(UiLogLevel.Information, "Запуск отменён пользователем.", account.DisplayName);
            LastOperation = "Запуск отменён";
        }
        catch (Exception exception)
        {
            account.SessionState = SessionState.Faulted;
            account.LastError = exception.Message;
            AddEvent(UiLogLevel.Error, $"Сбой запуска: {exception.Message}", account.DisplayName);
            LastOperation = "Ошибка запуска";
        }
        finally
        {
            _sessionCancellation.Remove(account.Id);
            cancellation.Dispose();
            RefreshViews();
        }
    }

    [RelayCommand]
    private Task StopAccountAsync(AccountItemViewModel? account) => RunSafelyAsync(async () =>
    {
        account ??= SelectedAccount;
        if (account is null || account.SessionState == SessionState.Stopped)
        {
            return;
        }

        if (_sessionCancellation.TryGetValue(account.Id, out var cancellation))
        {
            cancellation.Cancel();
            LastOperation = $"Отмена операции для {account.DisplayName}";
            return;
        }

        await _runtimeService.StopAsync(account, CancellationToken.None);
        AddEvent(UiLogLevel.Information, "Сессия остановлена, Medium и целевой процесс не запускались в демо.", account.DisplayName);
        LastOperation = $"Сессия {account.DisplayName} остановлена";
        RefreshViews();
    });

    [RelayCommand]
    private Task CheckAccountAsync(AccountItemViewModel? account) => RunSafelyAsync(async () =>
    {
        account ??= SelectedAccount;
        if (account is null || account.HasActiveSession)
        {
            return;
        }

        LastOperation = $"Проверка статуса {account.DisplayName}";
        await _runtimeService.CheckAsync(account, CancellationToken.None);
        AddEvent(
            account.Health == AccountServerStatus.Banned ? UiLogLevel.Warning : UiLogLevel.Success,
            $"Server-side статус: {account.HealthText} (демо).",
            account.DisplayName);
        LastOperation = $"Проверка завершена: {account.HealthText}";
        RefreshViews();
    });

    [RelayCommand]
    private async Task SimulateCrashAsync(AccountItemViewModel? account)
    {
        account ??= SelectedAccount;
        if (account is null || !account.CanSimulateCrash)
        {
            return;
        }

        var server = account.ServerDisplay;
        var cancellation = new CancellationTokenSource();
        if (!_sessionCancellation.TryAdd(account.Id, cancellation))
        {
            cancellation.Dispose();
            return;
        }

        try
        {
            AddEvent(
                UiLogLevel.Warning,
                $"Обнаружено падение. Reconnect только через Medium к сохранённому {server} (демо).",
                account.DisplayName);
            LastOperation = $"Reconnect {account.DisplayName}";
            var reconnected = await _runtimeService.SimulateCrashAsync(
                account,
                Settings.MaxAutomaticRestarts,
                cancellation.Token);
            if (reconnected)
            {
                AddEvent(
                    UiLogLevel.Success,
                    $"Соединение восстановлено через Medium на {account.ServerDisplay}. Restart #{account.RestartCount}.",
                    account.DisplayName);
                LastOperation = "Reconnect завершён";
            }
            else
            {
                AddEvent(UiLogLevel.Error, account.LastError ?? "Reconnect не выполнен.", account.DisplayName);
            }
        }
        catch (OperationCanceledException)
        {
            AddEvent(UiLogLevel.Information, "Reconnect отменён пользователем.", account.DisplayName);
        }
        catch (Exception exception)
        {
            account.SessionState = SessionState.Faulted;
            account.LastError = exception.Message;
            AddEvent(UiLogLevel.Error, $"Сбой reconnect: {exception.Message}", account.DisplayName);
        }
        finally
        {
            _sessionCancellation.Remove(account.Id);
            cancellation.Dispose();
            RefreshViews();
        }
    }

    [RelayCommand]
    private async Task StartNextAsync()
    {
        var account = Accounts.FirstOrDefault(item =>
            item.CanStart && item.Health != AccountServerStatus.Banned);
        if (account is null)
        {
            LastOperation = "Нет аккаунтов, доступных для запуска";
            return;
        }

        CurrentPage = AppPage.Accounts;
        SelectedAccount = account;
        await StartAccountAsync(account);
    }

    [RelayCommand]
    private Task StopAllAsync() => RunSafelyAsync(async () =>
    {
        foreach (var account in Accounts.Where(item => item.HasActiveSession).ToArray())
        {
            await StopAccountAsync(account);
        }

        LastOperation = "Все активные операции остановлены";
    });

    [RelayCommand]
    private Task CheckAllAsync() => RunSafelyAsync(async () =>
    {
        foreach (var account in Accounts.Where(item => !item.HasActiveSession).ToArray())
        {
            await CheckAccountAsync(account);
        }

        LastOperation = "Проверка доступных аккаунтов завершена";
    });

    [RelayCommand]
    private void BrowseMedium()
    {
        var path = _dialogService.BrowseForMediumExecutable();
        if (path is null)
        {
            return;
        }

        Settings.MediumExecutablePath = path;
        if (string.IsNullOrWhiteSpace(Settings.WorkingDirectory))
        {
            Settings.WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty;
        }

        LastOperation = "Путь к Medium обновлён. Нажмите «Сохранить настройки».";
    }

    [RelayCommand]
    private Task SaveSettingsAsync() => RunSafelyAsync(async () =>
    {
        Settings.Normalize();
        await _settingsStore.SaveAsync(Settings, CancellationToken.None);
        AddEvent(UiLogLevel.Success, "Настройки сохранены локально.");
        LastOperation = "Настройки сохранены";
    });

    [RelayCommand]
    private Task ExportEventsAsync() => RunSafelyAsync(async () =>
    {
        var path = _dialogService.BrowseForLogExport();
        if (path is null)
        {
            return;
        }

        var lines = Events.OrderBy(item => item.Timestamp).Select(item => item.ToString());
        await File.WriteAllLinesAsync(path, lines, Encoding.UTF8, CancellationToken.None);
        LastOperation = $"Журнал экспортирован: {Path.GetFileName(path)}";
    });

    [RelayCommand]
    private void ClearEvents()
    {
        Events.Clear();
        HasEvents = false;
        EventsView.Refresh();
        LastOperation = "Журнал очищен";
    }

    [RelayCommand]
    private Task ResetDemoDataAsync() => RunSafelyAsync(async () =>
    {
        if (Accounts.Any(account => account.HasActiveSession))
        {
            _dialogService.ShowError("Активные сессии", "Остановите все сессии перед сбросом демонстрационных данных.");
            return;
        }

        if (!_dialogService.Confirm("Сбросить данные", "Удалить текущий локальный пул и создать три демо-аккаунта?"))
        {
            return;
        }

        foreach (var account in Accounts)
        {
            account.PropertyChanged -= OnAccountPropertyChanged;
        }

        Accounts.Clear();
        AddDemoAccounts();
        await SaveAccountsAsync();
        SelectedAccount = Accounts.FirstOrDefault();
        RefreshViews();
        AddEvent(UiLogLevel.Warning, "Локальный пул восстановлен к демонстрационным данным.");
        LastOperation = "Демонстрационные данные восстановлены";
    });

    private async Task RunSafelyAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            LastOperation = "Операция отменена";
        }
        catch (Exception exception)
        {
            AddEvent(UiLogLevel.Error, exception.Message);
            LastOperation = "Операция завершилась ошибкой";
            _dialogService.ShowError("Madium Manager", exception.Message);
        }
    }

    private void AddAccountToCollection(AccountItemViewModel account)
    {
        account.PropertyChanged += OnAccountPropertyChanged;
        Accounts.Add(account);
    }

    private void AddDemoAccounts()
    {
        var now = DateTimeOffset.UtcNow;
        AddAccountToCollection(new AccountItemViewModel
        {
            DisplayName = "North · Demo",
            Login = "demo.north",
            Note = "Демонстрационная запись, не содержит реальных credentials.",
            CreatedAt = now
        });
        AddAccountToCollection(new AccountItemViewModel
        {
            DisplayName = "Beta · Demo",
            Login = "demo.beta",
            Note = "Используйте «Тест reconnect» после запуска.",
            CreatedAt = now
        });
        AddAccountToCollection(new AccountItemViewModel
        {
            DisplayName = "Banned · Demo",
            Login = "banned.demo",
            Note = "Демонстрирует пропуск аккаунта после server-side проверки.",
            CreatedAt = now
        });
    }

    private bool IsDuplicateLogin(string login, string? exceptAccountId) => Accounts.Any(account =>
        !string.Equals(account.Id, exceptAccountId, StringComparison.Ordinal) &&
        string.Equals(account.Login, login.Trim(), StringComparison.OrdinalIgnoreCase));

    private Task SaveAccountsAsync() => _accountRepository.SaveAsync(
        Accounts.Select(account => account.ToProfile()).ToArray(),
        CancellationToken.None);

    private bool FilterAccount(object item)
    {
        if (item is not AccountItemViewModel account || string.IsNullOrWhiteSpace(SearchText))
        {
            return item is AccountItemViewModel;
        }

        return account.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               account.Login.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               account.ServerDisplay.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private bool FilterEvent(object item)
    {
        if (item is not LogEntryViewModel entry)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(EventSearchText) &&
            !entry.Message.Contains(EventSearchText, StringComparison.OrdinalIgnoreCase) &&
            !(entry.AccountName?.Contains(EventSearchText, StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return false;
        }

        return SelectedLogLevel switch
        {
            "Информация" => entry.Level == UiLogLevel.Information,
            "Успех" => entry.Level == UiLogLevel.Success,
            "Предупреждения" => entry.Level == UiLogLevel.Warning,
            "Ошибки" => entry.Level == UiLogLevel.Error,
            _ => true
        };
    }

    private void AddEvent(UiLogLevel level, string message, string? accountName = null)
    {
        Events.Insert(0, new LogEntryViewModel(DateTimeOffset.UtcNow, level, message, accountName));
        while (Events.Count > 400)
        {
            Events.RemoveAt(Events.Count - 1);
        }

        HasEvents = true;
        EventsView.Refresh();
    }

    private void RefreshViews()
    {
        AccountsView.Refresh();
        SessionsView.Refresh();
        UpdateStatistics();
    }

    private void UpdateStatistics()
    {
        TotalAccounts = Accounts.Count;
        ActiveAccounts = Accounts.Count(account => account.Health == AccountServerStatus.Active);
        RunningAccounts = Accounts.Count(account => account.SessionState == SessionState.Running);
        IssueAccounts = Accounts.Count(account =>
            account.SessionState == SessionState.Faulted || account.Health == AccountServerStatus.Banned);
        HasAccounts = Accounts.Count > 0;
        HasSessions = Accounts.Any(account => account.HasActiveSession);
    }

    private void OnAccountPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(AccountItemViewModel.SessionState) or
            nameof(AccountItemViewModel.Health))
        {
            UpdateStatistics();
            SessionsView.Refresh();
        }
    }

    partial void OnCurrentPageChanged(AppPage value)
    {
        OnPropertyChanged(nameof(CurrentPageTitle));
        OnPropertyChanged(nameof(CurrentPageSubtitle));
    }

    partial void OnSearchTextChanged(string value)
    {
        AccountsView.Refresh();
    }

    partial void OnEventSearchTextChanged(string value)
    {
        EventsView.Refresh();
    }

    partial void OnSelectedLogLevelChanged(string value)
    {
        EventsView.Refresh();
    }
}
