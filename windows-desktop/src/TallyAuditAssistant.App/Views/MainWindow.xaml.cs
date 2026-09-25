using System.Windows;
using TallyAuditAssistant.App.ViewModels;

namespace TallyAuditAssistant.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
