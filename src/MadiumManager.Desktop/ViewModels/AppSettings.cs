using CommunityToolkit.Mvvm.ComponentModel;

namespace MadiumManager.Desktop.ViewModels;

public partial class AppSettings : ObservableObject
{
    [ObservableProperty]
    private string _mediumExecutablePath = string.Empty;

    [ObservableProperty]
    private string _workingDirectory = string.Empty;

    [ObservableProperty]
    private int _startupTimeoutSeconds = 20;

    [ObservableProperty]
    private int _maxAutomaticRestarts = 3;

    [ObservableProperty]
    private bool _checkAccountsBeforeStart = true;

    public void Normalize()
    {
        StartupTimeoutSeconds = Math.Clamp(StartupTimeoutSeconds, 3, 300);
        MaxAutomaticRestarts = Math.Clamp(MaxAutomaticRestarts, 0, 20);
        MediumExecutablePath = MediumExecutablePath?.Trim() ?? string.Empty;
        WorkingDirectory = WorkingDirectory?.Trim() ?? string.Empty;
    }
}
