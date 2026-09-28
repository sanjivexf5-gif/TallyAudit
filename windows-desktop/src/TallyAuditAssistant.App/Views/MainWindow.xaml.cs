using System;
using System.Windows;
using System.Windows.Media.Imaging;
using TallyAuditAssistant.App.ViewModels;

namespace TallyAuditAssistant.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Ensure runtime icon assignment for Alt+Tab, Taskbar, and Application Switcher
        try
        {
            if (Icon == null)
            {
                var iconUri = new Uri("pack://application:,,,/Assets/TallyAuditAssistant.ico", UriKind.Absolute);
                Icon = BitmapFrame.Create(iconUri);
            }
        }
        catch
        {
            // Fallback gracefully if running under test harness
        }
    }
}
