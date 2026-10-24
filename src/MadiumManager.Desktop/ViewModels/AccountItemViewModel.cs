using CommunityToolkit.Mvvm.ComponentModel;
using MadiumManager.Desktop.Models;
using MadiumManager.Domain.Accounts;

namespace MadiumManager.Desktop.ViewModels;

public partial class AccountItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _login = string.Empty;

    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    private DateTimeOffset _createdAt = DateTimeOffset.UtcNow;

    [ObservableProperty]
    private AccountServerStatus _health = AccountServerStatus.Unknown;

    [ObservableProperty]
    private SessionState _sessionState = SessionState.Stopped;

    [ObservableProperty]
    private string? _server;

    [ObservableProperty]
    private DateTimeOffset? _lastChecked;

    [ObservableProperty]
    private DateTimeOffset? _startedAt;

    [ObservableProperty]
    private string? _lastError;

    [ObservableProperty]
    private int _restartCount;

    [ObservableProperty]
    private string _uptimeDisplay = "00:00:00";

    public string ServerDisplay => Server ?? "—";

    public string LastCheckedDisplay => LastChecked?.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") ?? "Не проверялся";

    public string HealthText => Health switch
    {
        AccountServerStatus.Active => "Активен",
        AccountServerStatus.Banned => "Бан",
        AccountServerStatus.InvalidCredentials => "Ошибка доступа",
        AccountServerStatus.RateLimited => "Лимит API",
        AccountServerStatus.ServiceUnavailable => "API недоступен",
        _ => "Не проверен"
    };

    public string SessionText => SessionState switch
    {
        SessionState.Checking => "Проверка",
        SessionState.Starting => "Запуск Medium",
        SessionState.Running => "Работает",
        SessionState.Reconnecting => "Reconnect",
        SessionState.Stopping => "Остановка",
        SessionState.Faulted => "Ошибка",
        _ => "Остановлен"
    };

    public System.Windows.Media.Brush HealthBackground => Health switch
    {
        AccountServerStatus.Active => StatusPalette.SuccessBackground,
        AccountServerStatus.Banned or AccountServerStatus.InvalidCredentials => StatusPalette.DangerBackground,
        AccountServerStatus.RateLimited or AccountServerStatus.ServiceUnavailable => StatusPalette.WarningBackground,
        _ => StatusPalette.MutedBackground
    };

    public System.Windows.Media.Brush HealthForeground => Health switch
    {
        AccountServerStatus.Active => StatusPalette.Success,
        AccountServerStatus.Banned or AccountServerStatus.InvalidCredentials => StatusPalette.Danger,
        AccountServerStatus.RateLimited or AccountServerStatus.ServiceUnavailable => StatusPalette.Warning,
        _ => StatusPalette.Muted
    };

    public System.Windows.Media.Brush SessionBackground => SessionState switch
    {
        SessionState.Running => StatusPalette.SuccessBackground,
        SessionState.Checking or SessionState.Starting or SessionState.Reconnecting => StatusPalette.InfoBackground,
        SessionState.Faulted => StatusPalette.DangerBackground,
        SessionState.Stopping => StatusPalette.WarningBackground,
        _ => StatusPalette.MutedBackground
    };

    public System.Windows.Media.Brush SessionForeground => SessionState switch
    {
        SessionState.Running => StatusPalette.Success,
        SessionState.Checking or SessionState.Starting or SessionState.Reconnecting => StatusPalette.Info,
        SessionState.Faulted => StatusPalette.Danger,
        SessionState.Stopping => StatusPalette.Warning,
        _ => StatusPalette.Muted
    };

    public bool CanStart => SessionState is SessionState.Stopped or SessionState.Faulted;

    public bool CanStop => SessionState is not SessionState.Stopped;

    public bool CanSimulateCrash => SessionState == SessionState.Running;

    public bool HasActiveSession => SessionState is
        SessionState.Checking or
        SessionState.Starting or
        SessionState.Running or
        SessionState.Reconnecting or
        SessionState.Stopping;

    public AccountProfile ToProfile() => new(Id, DisplayName, Login, Note, CreatedAt);

    public void Tick(DateTimeOffset now)
    {
        if (StartedAt is null || !HasActiveSession)
        {
            return;
        }

        var elapsed = now - StartedAt.Value;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        UptimeDisplay = elapsed.TotalHours >= 24
            ? $"{(int)elapsed.TotalDays}д {elapsed.Hours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Hours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    public static AccountItemViewModel FromProfile(AccountProfile profile) => new()
    {
        Id = profile.Id,
        DisplayName = profile.DisplayName,
        Login = profile.Login,
        Note = profile.Note,
        CreatedAt = profile.CreatedAt
    };

    partial void OnHealthChanged(AccountServerStatus value)
    {
        OnPropertyChanged(nameof(HealthText));
        OnPropertyChanged(nameof(HealthBackground));
        OnPropertyChanged(nameof(HealthForeground));
    }

    partial void OnSessionStateChanged(SessionState value)
    {
        OnPropertyChanged(nameof(SessionText));
        OnPropertyChanged(nameof(SessionBackground));
        OnPropertyChanged(nameof(SessionForeground));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanSimulateCrash));
        OnPropertyChanged(nameof(HasActiveSession));
    }

    partial void OnServerChanged(string? value)
    {
        OnPropertyChanged(nameof(ServerDisplay));
    }

    partial void OnLastCheckedChanged(DateTimeOffset? value)
    {
        OnPropertyChanged(nameof(LastCheckedDisplay));
    }
}
