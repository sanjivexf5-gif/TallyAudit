using System.Threading.Tasks;

namespace TallyAuditAssistant.App.ViewModels;

public interface INavigationAware
{
    Task OnNavigatedToAsync();
}
