using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Tally;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.TallyIntegration;
using TallyAuditAssistant.TallyIntegration.Fixtures;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class TallyIntegrationTests
{
    private readonly NullLogger<TallyResponseParser> _parserLogger = NullLogger<TallyResponseParser>.Instance;
    private readonly NullLogger<TallyCompanyService> _companyServiceLogger = NullLogger<TallyCompanyService>.Instance;
    private readonly NullLogger<TallyConnection> _connectionLogger = NullLogger<TallyConnection>.Instance;

    [Fact]
    public async Task ConnectionTest_Success_When_Endpoint_Responds()
    {
        // Arrange
        var mockClient = new Mock<ITallyClient>();
        mockClient.Setup(c => c.PingAsync("http://localhost:9000", It.IsAny<CancellationToken>()))
                  .ReturnsAsync(true);

        var connection = new TallyConnection(mockClient.Object, _connectionLogger);

        // Act
        var result = await connection.TestConnectionAsync("localhost", 9000);

        // Assert
        Assert.True(result);
        Assert.Equal(ConnectionStatus.Connected, connection.CurrentStatus);
        Assert.NotNull(connection.ActiveEndpoint);
        Assert.Equal(9000, connection.ActiveEndpoint.Port);
    }

    [Fact]
    public async Task TallyUnavailableTest_PortClosed_Sets_Disconnected_Status()
    {
        // Arrange
        var mockClient = new Mock<ITallyClient>();
        mockClient.Setup(c => c.PingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(false);

        var connection = new TallyConnection(mockClient.Object, _connectionLogger);

        // Act
        var result = await connection.TestConnectionAsync("localhost", 9000);

        // Assert
        Assert.False(result);
        Assert.Equal(ConnectionStatus.Disconnected, connection.CurrentStatus);
    }

    [Fact]
    public void CompanyDetectionTest_Parses_Xml_And_Json_Fixtures()
    {
        // Arrange
        var parser = new TallyResponseParser(_parserLogger);

        // Act: XML parsing
        var xmlCompanies = parser.ParseCompanyList(TallyTestFixtures.CompanyListXml, TallyRequestFormat.Xml);

        // Assert: XML
        Assert.Equal(2, xmlCompanies.Count);
        Assert.Contains("Apex Industrial Solutions Pvt Ltd (FY 2025-26)", xmlCompanies);
        Assert.Contains("Delta Retail Ventures LLP (FY 2025-26)", xmlCompanies);

        // Act: JSON parsing
        var jsonCompanies = parser.ParseCompanyList(TallyTestFixtures.CompanyListJson, TallyRequestFormat.Json);

        // Assert: JSON
        Assert.Equal(2, jsonCompanies.Count);
        Assert.Equal("Apex Industrial Solutions Pvt Ltd (FY 2025-26)", jsonCompanies[0]);
    }

    [Fact]
    public void CompanyProfileTest_Extracts_GSTIN_PAN_And_BooksDate()
    {
        // Arrange
        var parser = new TallyResponseParser(_parserLogger);

        // Act
        var profile = parser.ParseCompanyProfile(TallyTestFixtures.CompanyProfileXml, TallyRequestFormat.Xml);

        // Assert
        Assert.NotNull(profile);
        Assert.Equal("Apex Industrial Solutions Pvt Ltd", profile.Name);
        Assert.Equal("27AAACA9999P1Z1", profile.GSTIN);
        Assert.Equal("AAACA9999P", profile.PAN);
        Assert.Equal("Maharashtra", profile.StateName);
        Assert.Equal(new DateTime(2025, 4, 1), profile.BooksBeginningFrom);
        Assert.Equal(10042, profile.AlterId);
    }

    [Fact]
    public void InvalidResponseTest_Detects_LineError_And_StatusZero()
    {
        // Arrange
        var parser = new TallyResponseParser(_parserLogger);

        // Act 1: LINEERROR
        var (hasError1, msg1) = parser.CheckForTallyErrors(TallyTestFixtures.TallyLineErrorXml, TallyRequestFormat.Xml);
        Assert.True(hasError1);
        Assert.NotNull(msg1);
        Assert.Contains("Unknown symbol", msg1);

        // Act 2: PARSERROR
        var (hasError2, msg2) = parser.CheckForTallyErrors(TallyTestFixtures.TallyParseErrorXml, TallyRequestFormat.Xml);
        Assert.True(hasError2);
        Assert.NotNull(msg2);
        Assert.Contains("Invalid XML format", msg2);

        // Act 3: Malformed XML should not throw exception
        var (hasError3, _) = parser.CheckForTallyErrors(TallyTestFixtures.MalformedXml, TallyRequestFormat.Xml);
        var companies = parser.ParseCompanyList(TallyTestFixtures.MalformedXml, TallyRequestFormat.Xml);
        Assert.Empty(companies);
    }

    [Fact]
    public async Task TimeoutTest_Returns_Graceful_Error_Without_Crashing()
    {
        // Arrange
        var mockClient = new Mock<ITallyClient>();
        mockClient.Setup(c => c.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TallyRequestFormat>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new TallyRawResponse(
                      IsSuccess: false,
                      HttpStatusCode: 408,
                      Content: string.Empty,
                      LatencyMs: 15000,
                      ErrorMessage: "Request to TallyPrime timed out after 15000ms."));

        var builder = new TallyRequestBuilder();
        var parser = new TallyResponseParser(_parserLogger);
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.GetTallyHostAsync()).ReturnsAsync("localhost");
        mockSettings.Setup(s => s.GetTallyPortAsync()).ReturnsAsync(9000);

        var companyService = new TallyCompanyService(mockClient.Object, builder, parser, mockSettings.Object, _companyServiceLogger);

        // Act
        var companies = await companyService.GetOpenCompaniesAsync("http://localhost:9000");

        // Assert
        Assert.Empty(companies); // Handled gracefully with zero unhandled exceptions
    }

    [Fact]
    public void Ledger_And_Voucher_Parsers_Extract_Accurate_Financial_Data()
    {
        // Arrange
        var parser = new TallyResponseParser(_parserLogger);

        // Act: Ledgers
        var ledgers = parser.ParseLedgers(TallyTestFixtures.LedgerCollectionXml);
        Assert.Equal(4, ledgers.Count);

        var debtor = ledgers.First(l => l.Name == "Mehta Fabrication Works");
        Assert.Equal("27AABCM8888Q1Z2", debtor.GSTIN);
        Assert.Equal(-85000m, debtor.ClosingBalance);

        // Act: Vouchers
        var vouchers = parser.ParseVouchers(TallyTestFixtures.VoucherCollectionXml);
        Assert.Single(vouchers);

        var v = vouchers[0];
        Assert.Equal("PUR/25-26/089", v.VoucherNumber);
        Assert.Equal("Purchase", v.VoucherType);
        Assert.Equal(142800m, v.TotalAmount);
        Assert.Equal(3, v.Entries.Count);
        Assert.Contains(v.Entries, e => e.LedgerName == "Input IGST 18%");
    }
}
