namespace TallyAuditAssistant.Core.Interfaces;

public class DiagnosticsExportOptions
{
    public bool IncludeDatabaseSchema { get; set; } = true;
    public bool IncludeSanitizedLogs { get; set; } = true;
    public bool IncludeSystemInfo { get; set; } = true;
    public bool IncludeTallyConnectionStatus { get; set; } = true;
    public string OutputDirectory { get; set; } = string.Empty;
}

public class DiagnosticsExportResult
{
    public bool Success { get; set; }
    public string PackageFilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string Summary { get; set; } = string.Empty;
    public IReadOnlyList<string> SanitizedItems { get; set; } = Array.Empty<string>();
}

public interface IDiagnosticsExportService
{
    Task<DiagnosticsExportResult> GenerateSanitizedDiagnosticsBundleAsync(DiagnosticsExportOptions options, CancellationToken ct = default);
    Task<string> GetDiagnosticsPreviewJsonAsync(CancellationToken ct = default);
}
