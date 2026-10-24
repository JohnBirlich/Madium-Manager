using MadiumManager.Desktop.ViewModels;
using MadiumManager.Domain.Accounts;

namespace MadiumManager.Desktop.Services;

public interface IUserDialogService
{
    AccountEditResult? ShowAccountEditor(AccountProfile? account);

    bool Confirm(string title, string message);

    void ShowError(string title, string message);

    string? BrowseForMediumExecutable();

    string? BrowseForLogExport();
}
