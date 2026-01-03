using System.Text.Json.Serialization;

namespace McpHealthServer.Models;

// Initialize Request/Response
public record InitializeRequest();

public record InitializeResponse(
    string SessionId,
    McpProtocolInfo Protocol,
    McpServerCapabilities Capabilities,
    McpTool[] Tools,
    string SseStreamUrl
);

public record McpProtocolInfo(
    string Version = "2024-11-05",
    string ProtocolVersion = "1.0"
);

public record McpServerCapabilities(
    bool Tools = true
);

public record McpTool(
    string Name,
    string Description,
    McpToolInputSchema? InputSchema = null
);

public record McpToolInputSchema(
    string Type,
    Dictionary<string, object> Properties,
    string[] Required
);

// Handshake Request/Response
public record HandshakeRequest(
    string SessionId
);

// Tool Invocation
public record ToolInvocationRequest(
    string Name,
    Dictionary<string, object> Input
);

public record ToolInvocationResponse(
    string ToolName,
    object? Result,
    string? Error
);

// Health Check Result
public record HealthCheckResult(
    string Url,
    string Status, // UP or DOWN
    [property: JsonPropertyName("http_status")] int? HttpStatus = null,
    [property: JsonPropertyName("latency_ms")] long? LatencyMs = null,
    string? Error = null,
    [property: JsonPropertyName("checked_at")] DateTime CheckedAt = default
)
{
    public HealthCheckResult() : this(string.Empty, "DOWN", null, null, null, DateTime.UtcNow)
    {
    }
}

