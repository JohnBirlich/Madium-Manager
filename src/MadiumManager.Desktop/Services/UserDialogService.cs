using System.Windows;
using MadiumManager.Desktop.ViewModels;
using MadiumManager.Domain.Accounts;
using Microsoft.Win32;

namespace MadiumManager.Desktop.Services;

public sealed class UserDialogService : IUserDialogService
{
    public AccountEditResult? ShowAccountEditor(AccountProfile? account)
    {
        var viewModel = new AccountEditViewModel(account);
        var window = new AccountEditWindow
        {
            DataContext = viewModel,
            Owner = System.Windows.Application.Current?.MainWindow
        };

        return window.ShowDialog() == true
            ? new AccountEditResult(viewModel.DisplayName.Trim(), viewModel.Login.Trim(), viewModel.Note.Trim())
            : null;
    }

    public bool Confirm(string title, string message) => MessageBox.Show(
        message,
        title,
        MessageBoxButton.YesNo,
        MessageBoxImage.Question,
        MessageBoxResult.No) == MessageBoxResult.Yes;

    public void ShowError(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public string? BrowseForMediumExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите Medium.exe",
            Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? BrowseForLogExport()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Экспорт журнала",
            Filter = "Text files (*.txt)|*.txt|Log files (*.log)|*.log",
            FileName = $"madium-events-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            AddExtension = true,
            OverwritePrompt = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
