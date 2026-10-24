using System.IO;
using System.Windows;
using MadiumManager.Desktop.Services;
using MadiumManager.Desktop.ViewModels;
using MadiumManager.Infrastructure.Accounts;

namespace MadiumManager.Desktop;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MadiumManager");
            Directory.CreateDirectory(dataDirectory);

            var accountRepository = new JsonAccountRepository(Path.Combine(dataDirectory, "accounts.json"));
            var settingsStore = new JsonSettingsStore(Path.Combine(dataDirectory, "settings.json"));
            var dialogService = new UserDialogService();

            // The UI is fully interactive, while this explicitly labelled runtime is
            // used until the real Medium CLI/API contract is supplied by the user.
            var runtimeService = new DemoAccountRuntimeService();
            var viewModel = new MainViewModel(
                accountRepository,
                settingsStore,
                runtimeService,
                dialogService);
            await viewModel.InitializeAsync();

            var window = new MainWindow { DataContext = viewModel };
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Не удалось инициализировать приложение:\n{exception.Message}",
                "Madium Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
}
