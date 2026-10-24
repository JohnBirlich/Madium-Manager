using System.ComponentModel;
using System.Windows;
using MadiumManager.Desktop.ViewModels;

namespace MadiumManager.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Shutdown();
        }

        base.OnClosing(e);
    }
}
