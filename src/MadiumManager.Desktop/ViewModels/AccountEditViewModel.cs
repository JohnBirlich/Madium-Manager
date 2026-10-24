using CommunityToolkit.Mvvm.ComponentModel;
using MadiumManager.Domain.Accounts;

namespace MadiumManager.Desktop.ViewModels;

public sealed record AccountEditResult(string DisplayName, string Login, string Note);

public partial class AccountEditViewModel : ObservableObject
{
    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _login = string.Empty;

    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public AccountEditViewModel(AccountProfile? account)
    {
        Title = account is null ? "Новый аккаунт" : "Изменить аккаунт";
        if (account is not null)
        {
            DisplayName = account.DisplayName;
            Login = account.Login;
            Note = account.Note;
        }
    }

    public string Title { get; }

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            ErrorMessage = "Введите отображаемое имя.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Login))
        {
            ErrorMessage = "Введите логин или внешний идентификатор аккаунта.";
            return false;
        }

        if (DisplayName.Trim().Length > 60 || Login.Trim().Length > 120 || Note.Trim().Length > 500)
        {
            ErrorMessage = "Превышена допустимая длина одного из полей.";
            return false;
        }

        ErrorMessage = null;
        return true;
    }
}
