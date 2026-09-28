using System;

namespace TallyAuditAssistant.Core.Interfaces;

public interface INavigationService
{
    string CurrentSection { get; }
    void Navigate(string section);
    event EventHandler<string>? Navigated;
}
