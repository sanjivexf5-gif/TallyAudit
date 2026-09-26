namespace TallyAuditAssistant.Core.Domain.Diagnostics;

public class DiagnosticCheckItem
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string Details { get; set; } = string.Empty;
}

public class SystemDiagnosticsReport
{
    public string AppVersion { get; set; } = "1.4.0";
    public string DotNetRuntime { get; set; } = Environment.Version.ToString();
    public string OsDescription { get; set; } = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
    public bool Is64Bit { get; set; } = Environment.Is64BitProcess;
    
    public string DatabasePath { get; set; } = string.Empty;
    public long DatabaseSizeBytes { get; set; }
    public string DatabaseSchemaVersion { get; set; } = "1.4.0";
    public bool IsDatabaseHealthy { get; set; }
    
    public string EvidenceDirectoryPath { get; set; } = string.Empty;
    public bool EvidenceDirectoryExists { get; set; }
    
    public string BackupDirectoryPath { get; set; } = string.Empty;
    public bool BackupDirectoryExists { get; set; }
    
    public string LogDirectoryPath { get; set; } = string.Empty;
    public bool LogDirectoryExists { get; set; }
    
    public string TallyHost { get; set; } = "localhost";
    public int TallyPort { get; set; } = 9000;
    public bool IsTallyReachable { get; set; }
    
    public long AvailableDiskSpaceMb { get; set; }
    public DateTime ReportTimestamp { get; set; } = DateTime.UtcNow;
    public List<DiagnosticCheckItem> Checks { get; set; } = new();
}
