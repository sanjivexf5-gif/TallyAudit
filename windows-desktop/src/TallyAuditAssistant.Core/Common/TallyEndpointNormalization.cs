using System.Text.RegularExpressions;

namespace TallyAuditAssistant.Core.Common;

public static class TallyEndpointNormalization
{
    public static (string Host, int Port, string Scheme) Normalize(string input, int defaultPort = 9000)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return ("localhost", defaultPort, "http");
        }

        var trimmed = input.Trim();
        
        // Remove scheme if present to simplify parsing
        var scheme = "http";
        if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            scheme = "https";
            trimmed = trimmed[8..];
        }
        else if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            scheme = "http";
            trimmed = trimmed[7..];
        }

        // Check if port is specified (last colon)
        var lastColonIndex = trimmed.LastIndexOf(':');
        var host = trimmed;
        var port = defaultPort;

        if (lastColonIndex != -1)
        {
            var portPart = trimmed[(lastColonIndex + 1)..];
            if (int.TryParse(portPart, out var parsedPort) && parsedPort > 0 && parsedPort <= 65535)
            {
                port = parsedPort;
                host = trimmed[..lastColonIndex];
            }
        }

        // Cleanup host (remove trailing slashes, etc.)
        host = host.TrimEnd('/');
        
        if (string.IsNullOrEmpty(host))
        {
            host = "localhost";
        }

        return (host, port, scheme);
    }

    public static string ToUrl(string host, int port, string scheme = "http")
    {
        return $"{scheme}://{host}:{port}";
    }

    public static bool IsValidHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        
        // Simple validation for IP or hostname
        return Regex.IsMatch(host, @"^(([a-zA-Z0-9]|[a-zA-Z0-9][a-zA-Z0-9\-]*[a-zA-Z0-9])\.)*([A-Za-z0-9]|[A-Za-z0-9][A-Za-z0-9\-]*[A-Za-z0-9])$") ||
               Regex.IsMatch(host, @"^(\d{1,3}\.){3}\d{1,3}$");
    }
}
