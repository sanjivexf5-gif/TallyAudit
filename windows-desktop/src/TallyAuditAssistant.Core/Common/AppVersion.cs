namespace TallyAuditAssistant.Core.Common;

/// <summary>
/// Central single-source-of-truth for application versioning across UI, Diagnostics, Reports, and Installers.
/// </summary>
public static class AppVersion
{
    public const string Version = "1.0.0";
    public const string BuildNumber = "2026.09.26.101";
    public const string ReleaseDate = "2026-09-26";
    public const string Channel = "Stable";
    public const string TargetRuntime = ".NET 8.0 (win-x64 Self-Contained)";
    public const string ApplicationName = "Tally Audit Assistant";
    public const string Description = "Tally Audit Assistant is an audit intelligence and review application that analyzes synchronized accounting data from TallyPrime.";
    public const string Copyright = "© 2026 Tally Audit Assistant Systems. All rights reserved.";
    public const string SupportEmail = "support@tallyauditassistant.local";
    public const string Website = "https://github.com/sanjivexf5-gif/TallyAudit";

    public static string FullVersionString => $"{Version} (Build {BuildNumber}) - {Channel}";
    public static string DisplayString => $"{ApplicationName} v{Version}";
}
