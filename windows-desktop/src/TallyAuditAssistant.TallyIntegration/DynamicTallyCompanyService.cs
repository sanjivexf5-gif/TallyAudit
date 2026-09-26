using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TallyAuditAssistant.Core.Domain.Tally;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.TallyIntegration.Mocks;

namespace TallyAuditAssistant.TallyIntegration;

public class DynamicTallyCompanyService : ITallyCompanyService
{
    private readonly TallyCompanyService _realService;
    private readonly MockTallyCompanyService _mockService;
    private readonly ISettingsService _settingsService;

    public DynamicTallyCompanyService(TallyCompanyService realService, MockTallyCompanyService mockService, ISettingsService settingsService)
    {
        _realService = realService;
        _mockService = mockService;
        _settingsService = settingsService;
    }

    private async Task<ITallyCompanyService> GetActiveServiceAsync()
    {
        return await _settingsService.IsMockModeEnabledAsync() ? _mockService : _realService;
    }

    public async Task<IReadOnlyList<string>> GetOpenCompaniesAsync(string? endpointUrl = null, CancellationToken cancellationToken = default)
    {
        var service = await GetActiveServiceAsync();
        return await service.GetOpenCompaniesAsync(endpointUrl, cancellationToken);
    }

    public async Task<string?> GetActiveCompanyAsync(string? endpointUrl = null, CancellationToken cancellationToken = default)
    {
        var service = await GetActiveServiceAsync();
        return await service.GetActiveCompanyAsync(endpointUrl, cancellationToken);
    }

    public async Task<Dictionary<string, string>> GetCompanyProfileAsync(string endpointUrl, string companyName, CancellationToken cancellationToken = default)
    {
        var service = await GetActiveServiceAsync();
        return await service.GetCompanyProfileAsync(endpointUrl, companyName, cancellationToken);
    }

    public async Task<TallyCompanyProfile?> GetCompanyProfileTypedAsync(string companyName, string? endpointUrl = null, CancellationToken cancellationToken = default)
    {
        var service = await GetActiveServiceAsync();
        return await service.GetCompanyProfileTypedAsync(companyName, endpointUrl, cancellationToken);
    }
}
