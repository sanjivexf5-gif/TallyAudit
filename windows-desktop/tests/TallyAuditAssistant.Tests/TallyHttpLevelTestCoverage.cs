using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Domain.Sync;
using TallyAuditAssistant.Core.Domain.Tally;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.TallyIntegration;
using Xunit;

namespace TallyAuditAssistant.Tests
{
    public class TallyHttpLevelTestCoverage
    {
        private readonly NullLogger<TallyResponseParser> _parserLogger = NullLogger<TallyResponseParser>.Instance;
        private readonly NullLogger<TallyCompanyService> _companyServiceLogger = NullLogger<TallyCompanyService>.Instance;
        private readonly NullLogger<TallyMasterService> _masterServiceLogger = NullLogger<TallyMasterService>.Instance;
        private readonly NullLogger<TallyVoucherService> _voucherServiceLogger = NullLogger<TallyVoucherService>.Instance;
        private readonly NullLogger<TallyClient> _clientLogger = NullLogger<TallyClient>.Instance;

        private HttpClient CreateMockHttpClient(Func<HttpRequestMessage, HttpResponseMessage> handlerFunc)
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync((HttpRequestMessage req, CancellationToken ct) => handlerFunc(req));

            return new HttpClient(handlerMock.Object);
        }

        // 1. Successful company discovery
        [Fact]
        public async Task Test_SuccessfulCompanyDiscovery()
        {
            const string responseXml = @"<ENVELOPE>
  <HEADER><VERSION>1</VERSION><STATUS>1</STATUS></HEADER>
  <BODY>
    <DATA>
      <COLLECTION>
        <COMPANY NAME=""Sanjiv Sinha Pvt Ltd""/>
        <COMPANY NAME=""Delta Retail Ventures LLP""/>
      </COLLECTION>
    </DATA>
  </BODY>
</ENVELOPE>";

            var httpClient = CreateMockHttpClient(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseXml, Encoding.UTF8, "text/xml")
            });

            var parser = new TallyResponseParser(_parserLogger);
            var client = new TallyClient(httpClient, _clientLogger, parser);
            var mockSettings = new Mock<ISettingsService>();
            mockSettings.Setup(s => s.GetTallyHostAsync()).ReturnsAsync("localhost");
            mockSettings.Setup(s => s.GetTallyPortAsync()).ReturnsAsync(9000);

            var companyService = new TallyCompanyService(client, new TallyRequestBuilder(), parser, mockSettings.Object, _companyServiceLogger);

            var companies = await companyService.GetOpenCompaniesAsync("http://localhost:9000");

            Assert.Equal(2, companies.Count);
            Assert.Contains("Sanjiv Sinha Pvt Ltd", companies);
            Assert.Contains("Delta Retail Ventures LLP", companies);
        }

        // 2. Successful company selection & 3. SVCurrentCompany included
        [Fact]
        public async Task Test_SuccessfulCompanySelection_And_SVCurrentCompanyIncluded()
        {
            const string responseXml = @"<ENVELOPE>
  <HEADER><VERSION>1</VERSION><STATUS>1</STATUS></HEADER>
  <BODY>
    <DATA>
      <COLLECTION>
        <COMPANY NAME=""Sanjiv Sinha Pvt Ltd"">
          <FORMALNAME>Sanjiv Sinha Pvt Ltd</FORMALNAME>
          <GSTIN>27AAACS9999P1Z1</GSTIN>
          <PAN>AAACS9999P</PAN>
          <STATENAME>Maharashtra</STATENAME>
          <BOOKSBEGINNINGFROM>20250401</BOOKSBEGINNINGFROM>
          <ALTERID>12345</ALTERID>
        </COMPANY>
      </COLLECTION>
    </DATA>
  </BODY>
</ENVELOPE>";

            string capturedRequestPayload = string.Empty;

            var httpClient = CreateMockHttpClient(req =>
            {
                capturedRequestPayload = req.Content.ReadAsStringAsync().Result;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseXml, Encoding.UTF8, "text/xml")
                };
            });

            var parser = new TallyResponseParser(_parserLogger);
            var client = new TallyClient(httpClient, _clientLogger, parser);
            var mockSettings = new Mock<ISettingsService>();

            var companyService = new TallyCompanyService(client, new TallyRequestBuilder(), parser, mockSettings.Object, _companyServiceLogger);

            var profile = await companyService.GetCompanyProfileTypedAsync("Sanjiv Sinha Pvt Ltd", "http://localhost:9000");

            Assert.NotNull(profile);
            Assert.Equal("Sanjiv Sinha Pvt Ltd", profile.Name);
            Assert.Equal("27AAACS9999P1Z1", profile.GSTIN);

            // Verify SVCurrentCompany static variable is included and properly structured
            Assert.Contains("<SVCurrentCompany>Sanjiv Sinha Pvt Ltd</SVCurrentCompany>", capturedRequestPayload);
            Assert.Contains("<SVCURRENTCOMPANY>Sanjiv Sinha Pvt Ltd</SVCURRENTCOMPANY>", capturedRequestPayload);
        }

        // 4. Successful ledger query
        [Fact]
        public async Task Test_SuccessfulLedgerQuery()
        {
            const string responseXml = @"<ENVELOPE>
  <HEADER><VERSION>1</VERSION><STATUS>1</STATUS></HEADER>
  <BODY>
    <DATA>
      <COLLECTION>
        <LEDGER NAME=""Capital Account"">
          <PARENT>Capital Account</PARENT>
          <OPENINGBALANCE>100000</OPENINGBALANCE>
          <CLOSINGBALANCE>100000</CLOSINGBALANCE>
          <ALTERID>54321</ALTERID>
        </LEDGER>
      </COLLECTION>
    </DATA>
  </BODY>
</ENVELOPE>";

            var httpClient = CreateMockHttpClient(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseXml, Encoding.UTF8, "text/xml")
            });

            var parser = new TallyResponseParser(_parserLogger);
            var client = new TallyClient(httpClient, _clientLogger, parser);
            var mockSettings = new Mock<ISettingsService>();
            mockSettings.Setup(s => s.GetTallyHostAsync()).ReturnsAsync("localhost");
            mockSettings.Setup(s => s.GetTallyPortAsync()).ReturnsAsync(9000);

            var masterService = new TallyMasterService(client, new TallyRequestBuilder(), parser, mockSettings.Object, _masterServiceLogger);

            var ledgers = await masterService.GetLedgersAsync("Sanjiv Sinha Pvt Ltd");

            Assert.Single(ledgers);
            Assert.Equal("Capital Account", ledgers[0].Name);
            Assert.Equal(54321, ledgers[0].AlterId);
        }

        // 5. Successful voucher query
        [Fact]
        public async Task Test_SuccessfulVoucherQuery()
        {
            const string responseXml = @"<ENVELOPE>
  <HEADER><VERSION>1</VERSION><STATUS>1</STATUS></HEADER>
  <BODY>
    <DATA>
      <COLLECTION>
        <VOUCHER GUID=""VOUCH-001"" VOUCHERNUMBER=""V-101"">
          <DATE>20250415</DATE>
          <VOUCHERTYPENAME>Receipt</VOUCHERTYPENAME>
          <PARTYLEDGERNAME>Cash</PARTYLEDGERNAME>
          <AMOUNT>-15000</AMOUNT>
          <NARRATION>Received cash from client</NARRATION>
          <ALLLEDGERENTRIES.LIST>
            <LEDGERNAME>Cash</LEDGERNAME>
            <AMOUNT>-15000</AMOUNT>
            <ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE>
          </ALLLEDGERENTRIES.LIST>
        </VOUCHER>
      </COLLECTION>
    </DATA>
  </BODY>
</ENVELOPE>";

            var httpClient = CreateMockHttpClient(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseXml, Encoding.UTF8, "text/xml")
            });

            var parser = new TallyResponseParser(_parserLogger);
            var client = new TallyClient(httpClient, _clientLogger, parser);
            var mockSettings = new Mock<ISettingsService>();
            mockSettings.Setup(s => s.GetTallyHostAsync()).ReturnsAsync("localhost");
            mockSettings.Setup(s => s.GetTallyPortAsync()).ReturnsAsync(9000);

            var voucherService = new TallyVoucherService(client, new TallyRequestBuilder(), parser, mockSettings.Object, _voucherServiceLogger);

            var vouchers = await voucherService.GetVouchersAsync("Sanjiv Sinha Pvt Ltd", new DateTime(2025, 4, 1), new DateTime(2025, 4, 30));

            Assert.Single(vouchers);
            Assert.Equal("V-101", vouchers[0].VoucherNumber);
            Assert.Equal("Receipt", vouchers[0].VoucherType);
            Assert.Equal(15000m, vouchers[0].TotalAmount);
        }

        // 6. Tally STATUS failure
        [Fact]
        public async Task Test_TallyStatusFailure_PropagatesError()
        {
            const string errorXml = @"<ENVELOPE>
  <HEADER>
    <VERSION>1</VERSION>
    <STATUS>0</STATUS>
  </HEADER>
  <BODY>
    <DATA>
      <DESCRIPTION>Requested object not found in current schema</DESCRIPTION>
    </DATA>
  </BODY>
</ENVELOPE>";

            var httpClient = CreateMockHttpClient(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(errorXml, Encoding.UTF8, "text/xml")
            });

            var parser = new TallyResponseParser(_parserLogger);
            var client = new TallyClient(httpClient, _clientLogger, parser);
            var mockSettings = new Mock<ISettingsService>();
            mockSettings.Setup(s => s.GetTallyHostAsync()).ReturnsAsync("localhost");
            mockSettings.Setup(s => s.GetTallyPortAsync()).ReturnsAsync(9000);

            var masterService = new TallyMasterService(client, new TallyRequestBuilder(), parser, mockSettings.Object, _masterServiceLogger);

            var ex = await Assert.ThrowsAsync<TallySynchronizationException>(() =>
                masterService.GetLedgersAsync("Sanjiv Sinha Pvt Ltd"));

            Assert.Contains("Status 0", ex.Message);
            Assert.Contains("Requested object not found", ex.Message);
        }

        // 7. Tally LINEERROR
        [Fact]
        public async Task Test_TallyLineError_PropagatesError()
        {
            const string lineErrorXml = @"<ENVELOPE>
  <HEADER><VERSION>1</VERSION><STATUS>1</STATUS></HEADER>
  <BODY>
    <LINEERROR>Unknown symbol 'BooksBeginningFrom' reference found during script evaluation</LINEERROR>
  </BODY>
</ENVELOPE>";

            var httpClient = CreateMockHttpClient(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(lineErrorXml, Encoding.UTF8, "text/xml")
            });

            var parser = new TallyResponseParser(_parserLogger);
            var client = new TallyClient(httpClient, _clientLogger, parser);
            var mockSettings = new Mock<ISettingsService>();

            var companyService = new TallyCompanyService(client, new TallyRequestBuilder(), parser, mockSettings.Object, _companyServiceLogger);

            var ex = await Assert.ThrowsAsync<TallySynchronizationException>(() =>
                companyService.GetCompanyProfileTypedAsync("Sanjiv Sinha Pvt Ltd", "http://localhost:9000"));

            Assert.Contains("Unknown symbol", ex.Message);
        }

        // 8. HTTP failure
        [Fact]
        public async Task Test_HttpFailure_PropagatesError()
        {
            var httpClient = CreateMockHttpClient(req => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                ReasonPhrase = "Internal Server Error",
                Content = new StringContent("Unable to allocate resources", Encoding.UTF8, "text/plain")
            });

            var parser = new TallyResponseParser(_parserLogger);
            var client = new TallyClient(httpClient, _clientLogger, parser);
            var mockSettings = new Mock<ISettingsService>();

            var companyService = new TallyCompanyService(client, new TallyRequestBuilder(), parser, mockSettings.Object, _companyServiceLogger);

            var ex = await Assert.ThrowsAsync<TallySynchronizationException>(() =>
                companyService.GetCompanyProfileTypedAsync("Sanjiv Sinha Pvt Ltd", "http://localhost:9000"));

            Assert.Equal(500, ex.HttpStatusCode);
            Assert.Contains("HTTP 500", ex.Message);
        }

        // 9. Malformed XML
        [Fact]
        public async Task Test_MalformedXml_ThrowsException()
        {
            const string malformedXml = @"<ENVELOPE><HEADER><VERSION>1</VERSION></HEADER><BODY><DATA>Broken Tag";

            var httpClient = CreateMockHttpClient(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(malformedXml, Encoding.UTF8, "text/xml")
            });

            var parser = new TallyResponseParser(_parserLogger);
            var client = new TallyClient(httpClient, _clientLogger, parser);
            var mockSettings = new Mock<ISettingsService>();

            var companyService = new TallyCompanyService(client, new TallyRequestBuilder(), parser, mockSettings.Object, _companyServiceLogger);

            await Assert.ThrowsAsync<TallySynchronizationException>(() =>
                companyService.GetCompanyProfileTypedAsync("Sanjiv Sinha Pvt Ltd", "http://localhost:9000"));
        }

        // 10. Successful query returning zero records
        [Fact]
        public async Task Test_SuccessfulQuery_ReturningZeroRecords_DoesNotThrow()
        {
            const string emptyXml = @"<ENVELOPE>
  <HEADER><VERSION>1</VERSION><STATUS>1</STATUS></HEADER>
  <BODY>
    <DATA>
      <COLLECTION>
      </COLLECTION>
    </DATA>
  </BODY>
</ENVELOPE>";

            var httpClient = CreateMockHttpClient(req => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(emptyXml, Encoding.UTF8, "text/xml")
            });

            var parser = new TallyResponseParser(_parserLogger);
            var client = new TallyClient(httpClient, _clientLogger, parser);
            var mockSettings = new Mock<ISettingsService>();
            mockSettings.Setup(s => s.GetTallyHostAsync()).ReturnsAsync("localhost");
            mockSettings.Setup(s => s.GetTallyPortAsync()).ReturnsAsync(9000);

            var masterService = new TallyMasterService(client, new TallyRequestBuilder(), parser, mockSettings.Object, _masterServiceLogger);

            var ledgers = await masterService.GetLedgersAsync("Sanjiv Sinha Pvt Ltd");

            Assert.Empty(ledgers); // Legitimate query, returns empty collection without errors
        }

        // 11. Sync failure at ledger stage
        [Fact]
        public async Task Test_SyncFailureAtLedgerStage()
        {
            var mockConnection = new Mock<ITallyConnection>();
            mockConnection.Setup(c => c.TestConnectionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                          .ReturnsAsync(true);

            var mockCompanyService = new Mock<ITallyCompanyService>();
            mockCompanyService.Setup(s => s.GetCompanyProfileTypedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                              .ReturnsAsync(new TallyCompanyProfile
                              {
                                  Name = "Sanjiv Sinha Pvt Ltd",
                                  BooksBeginningFrom = new DateTime(2025, 4, 1)
                              });

            var mockMasterService = new Mock<ITallyMasterService>();
            mockMasterService.Setup(s => s.GetGroupsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                             .ReturnsAsync(new List<string> { "Primary" });
            mockMasterService.Setup(s => s.GetLedgersAsync(It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
                             .ThrowsAsync(new TallySynchronizationException("READ LEDGERS", "Sanjiv Sinha Pvt Ltd", "http://localhost:9000", "Failed to parse ledgers"));

            var mockVoucherService = new Mock<ITallyVoucherService>();
            var mockSyncRepo = new Mock<ISyncRepository>();
            var mockAuditRepo = new Mock<IAuditRepository>();
            var mockSettings = new Mock<ISettingsService>();
            mockSettings.Setup(s => s.GetTallyHostAsync()).ReturnsAsync("localhost");
            mockSettings.Setup(s => s.GetTallyPortAsync()).ReturnsAsync(9000);
            mockSettings.Setup(s => s.IsMockModeEnabledAsync()).ReturnsAsync(false);

            var syncManager = new SyncManager(
                mockConnection.Object,
                mockCompanyService.Object,
                mockMasterService.Object,
                mockVoucherService.Object,
                mockSyncRepo.Object,
                mockAuditRepo.Object,
                mockSettings.Object,
                NullLogger<SyncManager>.Instance);

            var result = await syncManager.StartSyncAsync("Sanjiv Sinha Pvt Ltd", SyncMode.Full);

            Assert.False(result.IsSuccess);
            Assert.Contains("Failed to parse ledgers", result.ErrorMessage);
            Assert.Equal(SyncStage.Failed, syncManager.CurrentMetrics.CurrentStage);
        }

        // 12. Sync failure at voucher stage
        [Fact]
        public async Task Test_SyncFailureAtVoucherStage()
        {
            var mockConnection = new Mock<ITallyConnection>();
            mockConnection.Setup(c => c.TestConnectionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                          .ReturnsAsync(true);

            var mockCompanyService = new Mock<ITallyCompanyService>();
            mockCompanyService.Setup(s => s.GetCompanyProfileTypedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                              .ReturnsAsync(new TallyCompanyProfile
                              {
                                  Name = "Sanjiv Sinha Pvt Ltd",
                                  BooksBeginningFrom = new DateTime(2025, 4, 1)
                              });

            var mockMasterService = new Mock<ITallyMasterService>();
            mockMasterService.Setup(s => s.GetGroupsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                             .ReturnsAsync(new List<string> { "Primary" });
            mockMasterService.Setup(s => s.GetLedgersAsync(It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
                             .ReturnsAsync(new List<TallyLedgerDto>());

            var mockVoucherService = new Mock<ITallyVoucherService>();
            mockVoucherService.Setup(s => s.StreamVouchersChunkedAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                              .Throws(new TallySynchronizationException("READ VOUCHERS", "Sanjiv Sinha Pvt Ltd", "http://localhost:9000", "Voucher streaming failed"));

            var mockSyncRepo = new Mock<ISyncRepository>();
            var mockAuditRepo = new Mock<IAuditRepository>();
            var mockSettings = new Mock<ISettingsService>();
            mockSettings.Setup(s => s.GetTallyHostAsync()).ReturnsAsync("localhost");
            mockSettings.Setup(s => s.GetTallyPortAsync()).ReturnsAsync(9000);
            mockSettings.Setup(s => s.IsMockModeEnabledAsync()).ReturnsAsync(false);

            var syncManager = new SyncManager(
                mockConnection.Object,
                mockCompanyService.Object,
                mockMasterService.Object,
                mockVoucherService.Object,
                mockSyncRepo.Object,
                mockAuditRepo.Object,
                mockSettings.Object,
                NullLogger<SyncManager>.Instance);

            var result = await syncManager.StartSyncAsync("Sanjiv Sinha Pvt Ltd", SyncMode.Full);

            Assert.False(result.IsSuccess);
            Assert.Contains("Voucher streaming failed", result.ErrorMessage);
            Assert.Equal(SyncStage.Failed, syncManager.CurrentMetrics.CurrentStage);
        }

        // 13. Full successful synchronization & 14. Selected company preserved through sync
        [Fact]
        public async Task Test_FullSuccessfulSynchronization_PreservesSelectedCompany()
        {
            var mockConnection = new Mock<ITallyConnection>();
            mockConnection.Setup(c => c.TestConnectionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                          .ReturnsAsync(true);

            var mockCompanyService = new Mock<ITallyCompanyService>();
            mockCompanyService.Setup(s => s.GetCompanyProfileTypedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                              .ReturnsAsync(new TallyCompanyProfile
                              {
                                  Name = "Sanjiv Sinha Pvt Ltd",
                                  BooksBeginningFrom = new DateTime(2025, 4, 1)
                              });

            var mockMasterService = new Mock<ITallyMasterService>();
            mockMasterService.Setup(s => s.GetGroupsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                             .ReturnsAsync(new List<string> { "Sundry Debtors" });
            mockMasterService.Setup(s => s.GetLedgersAsync(It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
                             .ReturnsAsync(new List<TallyLedgerDto>
                             {
                                 new TallyLedgerDto { Name = "Customer A", ParentGroup = "Sundry Debtors" }
                             });

            var mockVoucherService = new Mock<ITallyVoucherService>();
            mockVoucherService.Setup(s => s.StreamVouchersChunkedAsync("Sanjiv Sinha Pvt Ltd", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                              .Returns(new List<TallyVoucherDto>
                              {
                                  new TallyVoucherDto { Guid = "V1", VoucherNumber = "1", TotalAmount = 500, VoucherDate = new DateTime(2025,4,10), VoucherType = "Sales" }
                              }.ToAsyncEnumerable());

            var mockSyncRepo = new Mock<ISyncRepository>();
            var mockAuditRepo = new Mock<IAuditRepository>();
            var mockSettings = new Mock<ISettingsService>();
            mockSettings.Setup(s => s.GetTallyHostAsync()).ReturnsAsync("localhost");
            mockSettings.Setup(s => s.GetTallyPortAsync()).ReturnsAsync(9000);
            mockSettings.Setup(s => s.IsMockModeEnabledAsync()).ReturnsAsync(false);

            var syncManager = new SyncManager(
                mockConnection.Object,
                mockCompanyService.Object,
                mockMasterService.Object,
                mockVoucherService.Object,
                mockSyncRepo.Object,
                mockAuditRepo.Object,
                mockSettings.Object,
                NullLogger<SyncManager>.Instance);

            var result = await syncManager.StartSyncAsync("Sanjiv Sinha Pvt Ltd", SyncMode.Full);

            Assert.True(result.IsSuccess);
            Assert.Equal(SyncStage.Complete, syncManager.CurrentMetrics.CurrentStage);
            
            // Verify company preservation - SyncManager requested and retrieved specifically for Sanjiv Sinha Pvt Ltd
            mockVoucherService.Verify(s => s.StreamVouchersChunkedAsync("Sanjiv Sinha Pvt Ltd", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        // 15. No demo fallback when real Tally is connected and fails
        [Fact]
        public async Task Test_NoDemoFallbackOnFailure()
        {
            var mockConnection = new Mock<ITallyConnection>();
            mockConnection.Setup(c => c.TestConnectionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                          .ReturnsAsync(true);

            var mockCompanyService = new Mock<ITallyCompanyService>();
            // Real Tally fails to return the company profile
            mockCompanyService.Setup(s => s.GetCompanyProfileTypedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                              .ReturnsAsync((TallyCompanyProfile)null);

            var mockMasterService = new Mock<ITallyMasterService>();
            var mockVoucherService = new Mock<ITallyVoucherService>();
            var mockSyncRepo = new Mock<ISyncRepository>();
            var mockAuditRepo = new Mock<IAuditRepository>();
            
            var mockSettings = new Mock<ISettingsService>();
            mockSettings.Setup(s => s.GetTallyHostAsync()).ReturnsAsync("localhost");
            mockSettings.Setup(s => s.GetTallyPortAsync()).ReturnsAsync(9000);
            // Real connection mode, not mock/demo mode
            mockSettings.Setup(s => s.IsMockModeEnabledAsync()).ReturnsAsync(false);

            var syncManager = new SyncManager(
                mockConnection.Object,
                mockCompanyService.Object,
                mockMasterService.Object,
                mockVoucherService.Object,
                mockSyncRepo.Object,
                mockAuditRepo.Object,
                mockSettings.Object,
                NullLogger<SyncManager>.Instance);

            // Sync should fail and THROW/PROPAGATE the exception rather than returning mock success or using mock fallback
            var result = await syncManager.StartSyncAsync("Sanjiv Sinha Pvt Ltd", SyncMode.Full);

            Assert.False(result.IsSuccess);
            Assert.Contains("Could not load company profile", result.ErrorMessage);
        }

        // 16. Strict read-only policy behavior
        [Fact]
        public void Test_StrictReadOnlyPolicy()
        {
            var policy = new TallyReadOnlyPolicy();
            
            // Verify that policy does not permit writing/modifying operations on TallyPrime
            Assert.True(policy.IsReadOnly);
            Assert.False(policy.AllowsWriting);
            Assert.False(policy.CanWriteAccountingData);
            Assert.Throws<InvalidOperationException>(() => policy.AssertCanWrite());
        }
    }

    public static class AsyncEnumerableExtensions
    {
        public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
        {
            foreach (var item in source)
            {
                await Task.Yield();
                yield return item;
            }
        }
    }
}
