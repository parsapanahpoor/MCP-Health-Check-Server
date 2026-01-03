using System.Net;

namespace McpHealthServer.Services;

public class SsrfProtectionService : ISsrfProtectionService
{
    private readonly ILogger<SsrfProtectionService> _logger;
    private readonly HashSet<string> _allowedSchemes = new() { "http", "https" };
    private readonly HashSet<IPAddress> _blockedPrivateRanges;
    private readonly HashSet<string>? _allowedDomains; // null means allow all public domains

    public SsrfProtectionService(ILogger<SsrfProtectionService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _blockedPrivateRanges = new HashSet<IPAddress>();
        
        var allowedDomainsConfig = configuration.GetSection("Security:AllowedDomains").Get<string[]>();
        if (allowedDomainsConfig != null && allowedDomainsConfig.Length > 0)
        {
            _allowedDomains = new HashSet<string>(allowedDomainsConfig, StringComparer.OrdinalIgnoreCase);
            _logger.LogInformation("SSRF protection: Domain allowlist enabled with {Count} domains", _allowedDomains.Count);
        }
        else
        {
            _logger.LogInformation("SSRF protection: Public domains allowed (no allowlist configured)");
        }
    }

    public bool IsUrlAllowed(string url, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(url))
        {
            errorMessage = "URL cannot be empty";
            return false;
        }

        // Parse URL
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            errorMessage = "Invalid URL format";
            return false;
        }

        if (!_allowedSchemes.Contains(uri.Scheme.ToLowerInvariant()))
        {
            errorMessage = $"Scheme '{uri.Scheme}' is not allowed. Only HTTP and HTTPS are permitted";
            return false;
        }

        if (_allowedDomains != null)
        {
            var host = uri.Host.ToLowerInvariant();
            if (!_allowedDomains.Contains(host))
            {
                errorMessage = $"Domain '{host}' is not in the allowed list";
                return false;
            }
        }

        try
        {
            var hostEntry = Dns.GetHostEntry(uri.Host);
            foreach (var ip in hostEntry.AddressList)
            {
                if (IsPrivateIp(ip))
                {
                    errorMessage = $"IP address {ip} is in a private/loopback range and is blocked";
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve host {Host} for SSRF check", uri.Host);
        }

        return true;
    }

    private bool IsPrivateIp(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        
        if (bytes[0] == 127)
            return true;
        
        if (bytes[0] == 10)
            return true;
        
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            return true;
        
        if (bytes[0] == 192 && bytes[1] == 168)
            return true;
        
        if (bytes[0] == 169 && bytes[1] == 254)
            return true;
        
        if (IPAddress.IsLoopback(ip))
            return true;
        
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var ipv6Bytes = ip.GetAddressBytes();
            if (ipv6Bytes[0] == 0xFE && (ipv6Bytes[1] & 0xC0) == 0x80)
                return true;
        }
        
        return false;
    }
}

