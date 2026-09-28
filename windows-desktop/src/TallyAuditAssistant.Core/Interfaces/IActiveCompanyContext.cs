using System;
using System.Threading;
using System.Threading.Tasks;
using TallyAuditAssistant.Core.Domain.Companies;

namespace TallyAuditAssistant.Core.Interfaces;

/// <summary>
/// Centralized active company context for the entire application workspace.
/// Coordinates active company selection, persistence, and synchronization notifications across all ViewModels.
/// </summary>
public interface IActiveCompanyContext
{
    string? ActiveCompanyName { get; }
    string? ActiveCompanyId { get; }
    Company? CurrentCompany { get; }
    event EventHandler<Company?>? ActiveCompanyChanged;

    Task<Company?> GetActiveCompanyAsync(CancellationToken cancellationToken = default);
    Task SetActiveCompanyAsync(Company company, CancellationToken cancellationToken = default);
    Task SetActiveCompanyNameAsync(string companyName, CancellationToken cancellationToken = default);
    Task<Company?> EnsureAndInitializeActiveCompanyAsync(CancellationToken cancellationToken = default);
}
