using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Serilog;

namespace TallyAuditAssistant.App.Views;

public partial class AuditFinalizationView : UserControl
{
    public AuditFinalizationView()
    {
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            // Prevent a WPF/XAML construction failure from terminating the desktop shell.
            // The full exception is written to the normal application log for diagnosis.
            Log.Error(ex, "Audit Finalization view failed during InitializeComponent.");

            Content = new Border
            {
                Padding = new Thickness(24),
                Background = Brushes.Transparent,
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Audit Finalization could not be loaded.",
                            FontSize = 22,
                            FontWeight = FontWeights.Bold,
                            Foreground = Brushes.White,
                            Margin = new Thickness(0, 0, 0, 10)
                        },
                        new TextBlock
                        {
                            Text = "The application is still running. Open the application log for the detailed error.",
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = Brushes.LightGray
                        }
                    }
                }
            };
        }
    }
}