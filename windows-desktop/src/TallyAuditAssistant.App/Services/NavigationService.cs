using System;
using TallyAuditAssistant.Core.Interfaces;

namespace TallyAuditAssistant.App.Services;

public class NavigationService : INavigationService
{
    public string CurrentSection { get; private set; } = "Dashboard";
    public event EventHandler<string>? Navigated;

    public void Navigate(string section)
    {
        if (string.IsNullOrWhiteSpace(section)) return;
        CurrentSection = section;
        Navigated?.Invoke(this, section);
    }
}
