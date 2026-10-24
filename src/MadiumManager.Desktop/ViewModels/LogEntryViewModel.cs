using System.Windows.Media;
using MadiumManager.Desktop.Models;

namespace MadiumManager.Desktop.ViewModels;

public sealed record LogEntryViewModel(
    DateTimeOffset Timestamp,
    UiLogLevel Level,
    string Message,
    string? AccountName)
{
    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm:ss");

    public string LevelText => Level switch
    {
        UiLogLevel.Success => "УСПЕХ",
        UiLogLevel.Warning => "ВНИМАНИЕ",
        UiLogLevel.Error => "ОШИБКА",
        _ => "ИНФО"
    };

    public Brush LevelBrush => Level switch
    {
        UiLogLevel.Success => StatusPalette.Success,
        UiLogLevel.Warning => StatusPalette.Warning,
        UiLogLevel.Error => StatusPalette.Danger,
        _ => StatusPalette.Info
    };

    public override string ToString() =>
        $"{Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss} [{LevelText}] {Message}" +
        (string.IsNullOrWhiteSpace(AccountName) ? string.Empty : $" — {AccountName}");
}
