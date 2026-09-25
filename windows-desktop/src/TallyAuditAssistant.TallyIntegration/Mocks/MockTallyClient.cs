using Microsoft.Extensions.Logging;
using TallyAuditAssistant.Core.Domain.Tally;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.TallyIntegration.Fixtures;

namespace TallyAuditAssistant.TallyIntegration.Mocks;

public class MockTallyClient : ITallyClient
{
    private readonly ILogger<MockTallyClient> _logger;

    public MockTallyClient(ILogger<MockTallyClient> logger)
    {
        _logger = logger;
    }

    public Task<string> PostXmlAsync(string endpointUrl, string xmlPayload, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[MOCK] Received Tally XML request at {Url}:\n{Xml}", endpointUrl, xmlPayload);
        return Task.FromResult(TallyTestFixtures.CompanyListXml);
    }

    public Task<TallyRawResponse> SendAsync(string endpointUrl, string payload, TallyRequestFormat format = TallyRequestFormat.Xml, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[MOCK] SendAsync called with format {Format} at {Url}", format, endpointUrl);

        string responseContent;
        if (payload.Contains("List of Companies"))
        {
            responseContent = format == TallyRequestFormat.Json ? TallyTestFixtures.CompanyListJson : TallyTestFixtures.CompanyListXml;
        }
        else if (payload.Contains("CompanyProfileCollection"))
        {
            responseContent = TallyTestFixtures.CompanyProfileXml;
        }
        else if (payload.Contains("AuditLedgerCollection"))
        {
            responseContent = TallyTestFixtures.LedgerCollectionXml;
        }
        else if (payload.Contains("AuditVoucherCollection"))
        {
            responseContent = TallyTestFixtures.VoucherCollectionXml;
        }
        else
        {
            responseContent = TallyTestFixtures.CompanyListXml;
        }

        var response = new TallyRawResponse(
            IsSuccess: true,
            HttpStatusCode: 200,
            Content: responseContent,
            LatencyMs: 14);

        return Task.FromResult(response);
    }

    public Task<bool> PingAsync(string endpointUrl, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[MOCK] Ping successful at {Url}", endpointUrl);
        return Task.FromResult(true);
    }
}

public class MockTallyCompanyService : ITallyCompanyService
{
    public Task<IReadOnlyList<string>> GetOpenCompaniesAsync(string? endpointUrl = null, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> mockList = new List<string>
        {
            "Apex Industrial Solutions Pvt Ltd (FY 2025-26)",
            "Delta Retail Ventures LLP (FY 2025-26)"
        };
        return Task.FromResult(mockList);
    }

    public Task<string?> GetActiveCompanyAsync(string? endpointUrl = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>("Apex Industrial Solutions Pvt Ltd (FY 2025-26)");
    }

    public Task<Dictionary<string, string>> GetCompanyProfileAsync(string endpointUrl, string companyName, CancellationToken cancellationToken = default)
    {
        var profile = new Dictionary<string, string>
        {
            { "Name", companyName },
            { "GSTIN", "27AAACA9999P1Z1" },
            { "PAN", "AAACA9999P" },
            { "State", "Maharashtra (27)" },
            { "BooksBeginningFrom", "2025-04-01" }
        };
        return Task.FromResult(profile);
    }

    public Task<TallyCompanyProfile?> GetCompanyProfileTypedAsync(string companyName, string? endpointUrl = null, CancellationToken cancellationToken = default)
    {
        var profile = new TallyCompanyProfile
        {
            Name = companyName,
            FormalName = "Apex Industrial Solutions Private Limited",
            GSTIN = "27AAACA9999P1Z1",
            PAN = "AAACA9999P",
            StateName = "Maharashtra",
            StateCode = "27",
            BooksBeginningFrom = new DateTime(2025, 4, 1),
            BaseCurrencySymbol = "₹",
            AlterId = 10042
        };
        return Task.FromResult<TallyCompanyProfile?>(profile);
    }
}
