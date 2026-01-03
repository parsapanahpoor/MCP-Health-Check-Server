using McpHealthServer.Models;
using System.Net;
using System.Net.NetworkInformation;

namespace McpHealthServer.Services;

/// <summary>
/// Service for managing and executing MCP tools.
/// Currently supports the check_api_status tool for health checking API endpoints.
/// </summary>
public class ToolService : IToolService
{
    private readonly ILogger<ToolService> _logger;
    private readonly ISsrfProtectionService _ssrfProtection;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly int _defaultTimeoutMs;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolService"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="ssrfProtection">Service for SSRF protection validation.</param>
    /// <param name="httpClientFactory">HTTP client factory for creating HttpClient instances.</param>
    /// <param name="configuration">Application configuration.</param>
    public ToolService(
        ILogger<ToolService> logger, 
        ISsrfProtectionService ssrfProtection, 
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _ssrfProtection = ssrfProtection;
        _httpClientFactory = httpClientFactory;
        _defaultTimeoutMs = configuration.GetValue<int>("ToolSettings:DefaultTimeoutMs", 3000);
        _logger.LogInformation("ToolService initialized with timeout: {TimeoutMs}ms", _defaultTimeoutMs);
    }

    /// <summary>
    /// Gets all available MCP tools.
    /// </summary>
    /// <returns>Array of available tools, including check_api_status.</returns>
    public McpTool[] GetAvailableTools()
    {
        return new[]
        {
            new McpTool(
                Name: "check_api_status",
                Description: "Checks the health status of an API endpoint by making an HTTP request",
                InputSchema: new McpToolInputSchema(
                    Type: "object",
                    Properties: new Dictionary<string, object>
                    {
                        ["url"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "The URL of the API endpoint to check",
                            ["format"] = "uri"
                        }
                    },
                    Required: new[] { "url" }
                )
            )
        };
    }

    /// <summary>
    /// Executes a tool by name with the provided input.
    /// </summary>
    /// <param name="toolName">Name of the tool to execute (e.g., "check_api_status").</param>
    /// <param name="input">Input parameters for the tool.</param>
    /// <returns>Tool execution result (e.g., HealthCheckResult for check_api_status).</returns>
    /// <exception cref="ArgumentException">Thrown when tool name is unknown.</exception>
    public async Task<object> ExecuteToolAsync(string toolName, Dictionary<string, object> input)
    {
        _logger.LogDebug("Executing tool: {ToolName}", toolName);
        return toolName switch
        {
            "check_api_status" => await CheckApiStatusAsync(input),
            _ => throw new ArgumentException($"Unknown tool: {toolName}", nameof(toolName))
        };
    }

    /// <summary>
    /// Checks the health status of an API endpoint by making an HTTP request.
    /// Performs SSRF protection validation before making the request.
    /// </summary>
    /// <param name="input">Input dictionary containing "url" key with the endpoint URL.</param>
    /// <returns>
    /// HealthCheckResult with:
    /// - Status: "UP" or "DOWN"
    /// - HTTP status code (if reachable)
    /// - Latency in milliseconds
    /// - Timestamp of check
    /// - Error message (if failed)
    /// </returns>
    private async Task<HealthCheckResult> CheckApiStatusAsync(Dictionary<string, object> input)
    {
        string url;
        
        if (!input.TryGetValue("url", out var urlObj))
        {
            return new HealthCheckResult
            {
                Url = "unknown",
                Status = "DOWN",
                Error = "Missing 'url' parameter"
            };
        }
        
        if (urlObj is string urlString)
        {
            url = urlString;
        }
        else if (urlObj is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            url = jsonElement.GetString() ?? string.Empty;
        }
        else
        {
            url = urlObj.ToString() ?? string.Empty;
        }
        
        if (string.IsNullOrWhiteSpace(url))
        {
            return new HealthCheckResult
            {
                Url = "unknown",
                Status = "DOWN",
                Error = "Invalid 'url' parameter"
            };
        }

        if (!_ssrfProtection.IsUrlAllowed(url, out var ssrfError))
        {
            return new HealthCheckResult
            {
                Url = url,
                Status = "DOWN",
                Error = ssrfError
            };
        }

        var startTime = DateTime.UtcNow;
        
        try
        {
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromMilliseconds(_defaultTimeoutMs);

            using var cts = new CancellationTokenSource(_defaultTimeoutMs);
            HttpResponseMessage? response = null;
            
            // Try HEAD first (more efficient), fallback to GET if HEAD fails
            try
            {
                response = await httpClient.SendAsync(
                    new HttpRequestMessage(HttpMethod.Head, url),
                    cts.Token);
            }
            catch (HttpRequestException)
            {
                // Some servers don't support HEAD, fallback to GET
                response = await httpClient.SendAsync(
                    new HttpRequestMessage(HttpMethod.Get, url),
                    cts.Token);
            }

            var endTime = DateTime.UtcNow;
            var latencyMs = (long)(endTime - startTime).TotalMilliseconds;
            var statusCode = (int)response.StatusCode;
            var status = statusCode >= 200 && statusCode < 400 ? "UP" : "DOWN";

            return new HealthCheckResult
            {
                Url = url,
                Status = status,
                HttpStatus = statusCode,
                LatencyMs = latencyMs,
                CheckedAt = endTime
            };
        }
        catch (TaskCanceledException)
        {
            return new HealthCheckResult
            {
                Url = url,
                Status = "DOWN",
                Error = $"Timeout after {_defaultTimeoutMs}ms",
                CheckedAt = DateTime.UtcNow
            };
        }
        catch (HttpRequestException ex)
        {
            return new HealthCheckResult
            {
                Url = url,
                Status = "DOWN",
                Error = ex.Message,
                CheckedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking API status for URL: {Url}", url);
            return new HealthCheckResult
            {
                Url = url,
                Status = "DOWN",
                Error = $"Unexpected error: {ex.Message}",
                CheckedAt = DateTime.UtcNow
            };
        }
    }
}

