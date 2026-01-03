namespace McpHealthServer.Services;

public interface ISsrfProtectionService
{
    /// <summary>
    /// Checks if a URL is allowed (SSRF protection)
    /// </summary>
    /// <param name="url">The URL to check</param>
    /// <param name="errorMessage">Output error message if URL is not allowed</param>
    /// <returns>True if URL is allowed, false otherwise</returns>
    bool IsUrlAllowed(string url, out string? errorMessage);
}

