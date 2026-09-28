using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TallyAuditAssistant.Core.Domain.Audit;
using TallyAuditAssistant.Core.Domain.Companies;
using TallyAuditAssistant.Core.Domain.Corrections;
using TallyAuditAssistant.Core.Domain.Security;
using TallyAuditAssistant.Core.Interfaces;
using TallyAuditAssistant.Data;
using TallyAuditAssistant.Data.Repositories;
using TallyAuditAssistant.TallyIntegration.Services;
using Xunit;

namespace TallyAuditAssistant.Tests;

public class TallyCorrectionWorkflowTests : IAsyncLifetime
{
    private readonly string _testDbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly DatabaseInitializer _initializer;
    private readonly TallyCorrectionRepository _repository;

    public TallyCorrectionWorkflowTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"correction_test_{Guid.NewGuid():N}.db");
        _factory = new SqliteConnectionFactory(_testDbPath);
        _initializer = new DatabaseInitializer(_factory, NullLogger<DatabaseInitializer>.Instance, _testDbPath);
        _repository = new TallyCorrectionRepository(_factory);
    }

    public async Task InitializeAsync()
    {
        await _initializer.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (File.Exists(_testDbPath)) File.Delete(_testDbPath);
        }
        catch { }
    }

    [Fact]
    public async Task CreateCorrection_SavesInDraftState()
    {
        var companyId = "COMP-CORR-01";
        await SaveCompanyAsync(companyId);

        var correction = new TallyCorrection
        {
            CompanyId = companyId,
            VoucherNumber = "PUR-015",
            FieldName = "GSTIN",
            OriginalValue = "27AAAAA0000A1Z5",
            ProposedValue = "27AAACA9999P1Z1",
            Reason = "Supplier GSTIN update",
            Status = CorrectionStatus.Draft
        };

        await _repository.SaveCorrectionAsync(correction);
        var loaded = await _repository.GetCorrectionByIdAsync(correction.Id);

        Assert.NotNull(loaded);
        Assert.Equal(CorrectionStatus.Draft, loaded.Status);
        Assert.Equal("PUR-015", loaded.VoucherNumber);
    }

    [Fact]
    public async Task SubmitCorrection_TransitionsToPendingApproval()
    {
        var companyId = "COMP-CORR-02";
        await SaveCompanyAsync(companyId);

        var correction = new TallyCorrection { CompanyId = companyId, Status = CorrectionStatus.Draft };
        await _repository.SaveCorrectionAsync(correction);

        await _repository.UpdateCorrectionStatusAsync(correction.Id, CorrectionStatus.PendingApproval, "Auditor1", "Submitting for review");
        var loaded = await _repository.GetCorrectionByIdAsync(correction.Id);

        Assert.NotNull(loaded);
        Assert.Equal(CorrectionStatus.PendingApproval, loaded.Status);
    }

    [Fact]
    public async Task ApproveCorrection_TransitionsToApproved()
    {
        var companyId = "COMP-CORR-03";
        await SaveCompanyAsync(companyId);

        var correction = new TallyCorrection { CompanyId = companyId, Status = CorrectionStatus.PendingApproval };
        await _repository.SaveCorrectionAsync(correction);

        await _repository.UpdateCorrectionStatusAsync(correction.Id, CorrectionStatus.Approved, "SeniorAuditor", "Approved after review");
        var loaded = await _repository.GetCorrectionByIdAsync(correction.Id);

        Assert.NotNull(loaded);
        Assert.Equal(CorrectionStatus.Approved, loaded.Status);
    }

    [Fact]
    public async Task RejectCorrection_TransitionsToRejected()
    {
        var companyId = "COMP-CORR-04";
        await SaveCompanyAsync(companyId);

        var correction = new TallyCorrection { CompanyId = companyId, Status = CorrectionStatus.PendingApproval };
        await _repository.SaveCorrectionAsync(correction);

        await _repository.UpdateCorrectionStatusAsync(correction.Id, CorrectionStatus.Rejected, "SeniorAuditor", "Insufficient evidence");
        var loaded = await _repository.GetCorrectionByIdAsync(correction.Id);

        Assert.NotNull(loaded);
        Assert.Equal(CorrectionStatus.Rejected, loaded.Status);
    }

    [Fact]
    public async Task CancelCorrection_TransitionsToCancelled()
    {
        var companyId = "COMP-CORR-05";
        await SaveCompanyAsync(companyId);

        var correction = new TallyCorrection { CompanyId = companyId, Status = CorrectionStatus.Draft };
        await _repository.SaveCorrectionAsync(correction);

        await _repository.UpdateCorrectionStatusAsync(correction.Id, CorrectionStatus.Cancelled, "Auditor1", "User cancelled proposal");
        var loaded = await _repository.GetCorrectionByIdAsync(correction.Id);

        Assert.NotNull(loaded);
        Assert.Equal(CorrectionStatus.Cancelled, loaded.Status);
    }

    [Fact]
    public async Task ApplyRequiresApproval_RejectsDraftOrPendingCorrection()
    {
        var companyId = "COMP-CORR-06";
        await SaveCompanyAsync(companyId);

        var mockConn = new Mock<ITallyConnection>();
        var mockAuth = new Mock<IAuthorizationService>();
        mockAuth.Setup(a => a.CanPerformAction(It.IsAny<AppUser>(), PermissionAction.ApplyTallyCorrection)).Returns(true);

        var writeService = new TallyWriteService(mockConn.Object, _repository, mockAuth.Object, NullLogger<TallyWriteService>.Instance);

        var correction = new TallyCorrection { CompanyId = companyId, Status = CorrectionStatus.Draft };
        await _repository.SaveCorrectionAsync(correction);

        var result = await writeService.ApplyCorrectionAsync(correction, "User1", userConfirmedExplicitly: true);

        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_STATE", result.FailureCode);
    }

    [Fact]
    public async Task ApplyRequiresExplicitConfirmation_FailsWhenUnconfirmed()
    {
        var companyId = "COMP-CORR-07";
        await SaveCompanyAsync(companyId);

        var mockConn = new Mock<ITallyConnection>();
        var mockAuth = new Mock<IAuthorizationService>();
        mockAuth.Setup(a => a.CanPerformAction(It.IsAny<AppUser>(), PermissionAction.ApplyTallyCorrection)).Returns(true);

        var writeService = new TallyWriteService(mockConn.Object, _repository, mockAuth.Object, NullLogger<TallyWriteService>.Instance);

        var correction = new TallyCorrection { CompanyId = companyId, Status = CorrectionStatus.Approved };
        await _repository.SaveCorrectionAsync(correction);

        var result = await writeService.ApplyCorrectionAsync(correction, "User1", userConfirmedExplicitly: false);

        Assert.False(result.IsSuccess);
        Assert.Equal("EXPLICIT_CONFIRMATION_REQUIRED", result.FailureCode);
    }

    [Fact]
    public async Task ReadOnlyCannotApply_FailsWithUnauthorized()
    {
        var companyId = "COMP-CORR-08";
        await SaveCompanyAsync(companyId);

        var mockConn = new Mock<ITallyConnection>();
        var mockAuth = new Mock<IAuthorizationService>();
        mockAuth.Setup(a => a.CanPerformAction(It.IsAny<AppUser>(), PermissionAction.ApplyTallyCorrection)).Returns(false);

        var writeService = new TallyWriteService(mockConn.Object, _repository, mockAuth.Object, NullLogger<TallyWriteService>.Instance);

        var correction = new TallyCorrection { CompanyId = companyId, Status = CorrectionStatus.Approved };
        await _repository.SaveCorrectionAsync(correction);

        var result = await writeService.ApplyCorrectionAsync(correction, "ReadOnlyUser", userConfirmedExplicitly: true);

        Assert.False(result.IsSuccess);
        Assert.Equal("UNAUTHORIZED", result.FailureCode);
    }

    [Fact]
    public async Task TallyDisconnectedDoesNotApply_FailsWhenDisconnected()
    {
        var companyId = "COMP-CORR-09";
        await SaveCompanyAsync(companyId);

        var mockConn = new Mock<ITallyConnection>();
        mockConn.Setup(c => c.CheckIfProcessRunningAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        mockConn.Setup(c => c.ActiveEndpoint).Returns((TallyEndpointInfo?)null);

        var mockAuth = new Mock<IAuthorizationService>();
        mockAuth.Setup(a => a.CanPerformAction(It.IsAny<AppUser>(), PermissionAction.ApplyTallyCorrection)).Returns(true);

        var writeService = new TallyWriteService(mockConn.Object, _repository, mockAuth.Object, NullLogger<TallyWriteService>.Instance);

        var correction = new TallyCorrection { CompanyId = companyId, Status = CorrectionStatus.Approved };
        await _repository.SaveCorrectionAsync(correction);

        var result = await writeService.ApplyCorrectionAsync(correction, "Auditor1", userConfirmedExplicitly: true);

        Assert.False(result.IsSuccess);
        Assert.Equal("TALLY_DISCONNECTED", result.FailureCode);
    }

    [Fact]
    public async Task AcceptResolveDoesNotModifyTally_IsAuditDatabaseOnly()
    {
        var companyId = "COMP-CORR-10";
        await SaveCompanyAsync(companyId);

        var auditRepo = new AuditRepository(_factory);
        var ex = new AuditException
        {
            Id = "EXC-TEST-10",
            CompanyId = companyId,
            RuleId = "ACC-SEQ-01",
            RuleName = "Test Rule",
            Category = AuditCategory.Accounting,
            Severity = AuditSeverity.Medium,
            EvidenceJson = "{}",
            Status = AuditExceptionStatus.Open
        };
        await auditRepo.SaveExceptionsAsync(new[] { ex });

        await auditRepo.UpdateExceptionStatusAsync(ex.Id, AuditExceptionStatus.Resolved, "Resolved in audit DB only");

        var updated = await auditRepo.GetExceptionByIdAsync(ex.Id);
        Assert.NotNull(updated);
        Assert.Equal(AuditExceptionStatus.Resolved, updated.Status);
    }

    [Fact]
    public async Task VerificationSuccess_MatchesExpectedValue()
    {
        var companyId = "COMP-CORR-11";
        await SaveCompanyAsync(companyId);

        var mockConn = new Mock<ITallyConnection>();
        var mockAuth = new Mock<IAuthorizationService>();
        var writeService = new TallyWriteService(mockConn.Object, _repository, mockAuth.Object, NullLogger<TallyWriteService>.Instance);

        var correction = new TallyCorrection
        {
            CompanyId = companyId,
            Status = CorrectionStatus.Applied,
            ProposedValue = "27AAACA9999P1Z1"
        };
        await _repository.SaveCorrectionAsync(correction);

        var verifyResult = await writeService.VerifyCorrectionAsync(correction);

        Assert.True(verifyResult.IsVerified);
        Assert.Equal(CorrectionStatus.Verified, correction.Status);
    }

    [Fact]
    public async Task DuplicateApplicationPrevented_RejectsAlreadyAppliedCorrection()
    {
        var companyId = "COMP-CORR-12";
        await SaveCompanyAsync(companyId);

        var mockConn = new Mock<ITallyConnection>();
        var mockAuth = new Mock<IAuthorizationService>();
        mockAuth.Setup(a => a.CanPerformAction(It.IsAny<AppUser>(), PermissionAction.ApplyTallyCorrection)).Returns(true);

        var writeService = new TallyWriteService(mockConn.Object, _repository, mockAuth.Object, NullLogger<TallyWriteService>.Instance);

        var correction = new TallyCorrection { CompanyId = companyId, Status = CorrectionStatus.Applied };
        await _repository.SaveCorrectionAsync(correction);

        var result = await writeService.ApplyCorrectionAsync(correction, "Auditor1", userConfirmedExplicitly: true);

        Assert.False(result.IsSuccess);
        Assert.Equal("ALREADY_APPLIED", result.FailureCode);
    }

    private async Task SaveCompanyAsync(string companyId)
    {
        var companyRepo = new AuditRepository(_factory);
        await companyRepo.SaveCompanyAsync(new Company
        {
            Id = companyId,
            TallyCompanyName = $"Company {companyId}",
            BooksFromDate = new DateTime(2025, 4, 1)
        });
    }
}
