using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace TallyAuditAssistant.Core.Common;

/// <summary>
/// Normalizes and validates TallyPrime HTTP endpoints, hosts, and ports.
/// </summary>
public static class TallyEndpointNormalization
{
    /// <summary>
    /// Normalizes an input endpoint string into its canonical Host, Port, and Scheme components.
    /// Rejects malformed or invalid endpoints by throwing <see cref="ArgumentException"/>.
    /// </summary>
    /// <param name="input">The host, URL, or host:port string to normalize.</param>
    /// <param name="defaultPort">The fallback port if none is specified in the input string (default 9000).</param>
    /// <returns>A tuple containing (Host, Port, Scheme).</returns>
    /// <exception cref="ArgumentException">Thrown when the endpoint is malformed, has multiple schemes, invalid port, or invalid host.</exception>
    public static (string Host, int Port, string Scheme) Normalize(string? input, int defaultPort = 9000)
    {
        if (defaultPort <= 0 || defaultPort > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultPort), "Default port must be between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            return ("localhost", defaultPort, "http");
        }

        var trimmed = input.Trim();

        // Detect multiple schemes or repeated http://, e.g. http://http://localhost:9000
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

        // After stripping the leading scheme, if there are still scheme delimiters, reject (e.g. http://http:// or ftp://)
        if (trimmed.Contains("://", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Malformed Tally endpoint with duplicate or invalid scheme: '{input}'", nameof(input));
        }

        // Trim trailing slashes
        trimmed = trimmed.TrimEnd('/');

        // Reject if empty after removing scheme and slashes (e.g. "http://" or "http:///")
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException($"Endpoint must specify a valid host name: '{input}'", nameof(input));
        }

        // Tally endpoints are host:port. They must not contain URL path components, credentials, query parameters, or fragments
        if (trimmed.Contains('/') || trimmed.Contains('\\') || trimmed.Contains('@') || trimmed.Contains('?') || trimmed.Contains('#'))
        {
            throw new ArgumentException($"Tally endpoint must not contain path, user info, or query parameters: '{input}'", nameof(input));
        }

        var host = trimmed;
        var port = defaultPort;

        // Check for port (separated by colon)
        var colonIndex = trimmed.IndexOf(':');
        if (colonIndex != -1)
        {
            // Ensure there is only one colon (host:port)
            if (trimmed.IndexOf(':', colonIndex + 1) != -1)
            {
                throw new ArgumentException($"Endpoint contains multiple port delimiters: '{input}'", nameof(input));
            }

            var hostPart = trimmed[..colonIndex];
            var portPart = trimmed[(colonIndex + 1)..];

            if (!int.TryParse(portPart, out var parsedPort) || parsedPort <= 0 || parsedPort > 65535)
            {
                throw new ArgumentException($"Invalid port number '{portPart}' in endpoint '{input}'. Port must be between 1 and 65535.", nameof(input));
            }

            port = parsedPort;
            host = hostPart;
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException($"Endpoint host cannot be empty: '{input}'", nameof(input));
        }

        // Normalize well-known loopback names
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            host = "localhost";
        }
        else if (host.Equals("127.0.0.1", StringComparison.Ordinal))
        {
            host = "127.0.0.1";
        }
        else if (!IsValidHost(host))
        {
            throw new ArgumentException($"Invalid host format in endpoint: '{host}'", nameof(input));
        }

        return (host, port, scheme);
    }

    /// <summary>
    /// Attempts to normalize the input endpoint without throwing exceptions.
    /// </summary>
    public static bool TryNormalize(string? input, out string host, out int port, out string scheme, int defaultPort = 9000)
    {
        try
        {
            (host, port, scheme) = Normalize(input, defaultPort);
            return true;
        }
        catch
        {
            host = string.Empty;
            port = 0;
            scheme = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// Formats the normalized host, port, and scheme into a standard URL string.
    /// </summary>
    public static string ToUrl(string host, int port, string scheme = "http")
    {
        return $"{scheme}://{host}:{port}";
    }

    /// <summary>
    /// Validates whether the given string is a valid hostname, FQDN, or IPv4 address.
    /// </summary>
    public static bool IsValidHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;

        // Check for whitespace
        if (host.Any(char.IsWhiteSpace)) return false;

        // Valid host via Uri.CheckHostName
        var hostType = Uri.CheckHostName(host);
        if (hostType == UriHostNameType.Dns || hostType == UriHostNameType.IPv4)
        {
            // Additional check: disallow consecutive dots
            if (host.Contains("..")) return false;
            return true;
        }

        return false;
    }
}
