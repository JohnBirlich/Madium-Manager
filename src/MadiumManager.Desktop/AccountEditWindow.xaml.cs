using System.Windows;
using MadiumManager.Desktop.ViewModels;

namespace MadiumManager.Desktop;

public partial class AccountEditWindow : Window
{
    public AccountEditWindow()
    {
        InitializeComponent();
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is AccountEditViewModel viewModel && viewModel.Validate())
        {
            DialogResult = true;
        }
    }
}
