using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using TallyAuditAssistant.App.ViewModels;

namespace TallyAuditAssistant.App.Views;

public partial class InvestigationView : UserControl
{
    public InvestigationView()
    {
        try
        {
            Debug.WriteLine("Investigation navigation requested");
            Debug.WriteLine("CurrentViewModel type: InvestigationViewModel");
            Debug.WriteLine("Investigation view template selected");

            InitializeComponent();

            Debug.WriteLine("InvestigationView instantiated");
            
            this.DataContextChanged += (s, e) =>
            {
                if (e.NewValue != null)
                {
                    Debug.WriteLine($"InvestigationView DataContext type: {e.NewValue.GetType().Name}");
                }
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CRITICAL ERROR: Failed to instantiate InvestigationView: {ex}");
            if (ex.InnerException != null)
            {
                Debug.WriteLine($"Inner Exception: {ex.InnerException}");
            }
            throw;
        }
    }
}
