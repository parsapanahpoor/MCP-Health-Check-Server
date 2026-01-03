using Microsoft.AspNetCore.Mvc;
using System.Text;
using McpHealthServer.Models;
using McpHealthServer.Services;
using System.Text.Json;

namespace McpHealthServer.Controllers;

/// <summary>
/// Controller for MCP (Model Context Protocol) endpoints.
/// Handles session initialization, handshake, SSE streaming, and tool invocations.
/// </summary>
[ApiController]
[Route("api/mcp")]
[Produces("application/json")]
public class McpController : ControllerBase
{
    private readonly ISessionService _sessionService;
    private readonly IToolService _toolService;
    private readonly ILogger<McpController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpController"/> class.
    /// </summary>
    /// <param name="sessionService">Service for managing MCP sessions.</param>
    /// <param name="toolService">Service for executing MCP tools.</param>
    /// <param name="logger">Logger instance for this controller.</param>
    public McpController(
        ISessionService sessionService,
        IToolService toolService,
        ILogger<McpController> logger)
    {
        _sessionService = sessionService;
        _toolService = toolService;
        _logger = logger;
    }

    /// <summary>
    /// Initializes a new MCP session.
    /// Creates a new session ID, returns available tools, and provides the SSE stream URL.
    /// </summary>
    /// <param name="request">Initialize request (can be empty).</param>
    /// <returns>
    /// An <see cref="InitializeResponse"/> containing:
    /// - Session ID for this MCP session
    /// - Protocol information and server capabilities
    /// - List of available tools (including check_api_status)
    /// - SSE stream URL for this session
    /// </returns>
    /// <response code="200">Session initialized successfully.</response>
    [HttpPost("initialize")]
    [ProducesResponseType(typeof(InitializeResponse), StatusCodes.Status200OK)]
    public IActionResult Initialize([FromBody] InitializeRequest? request)
    {
        var sessionId = _sessionService.CreateSession();
        var tools = _toolService.GetAvailableTools();
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var sseStreamUrl = $"{baseUrl}/api/mcp/stream/{sessionId}";

        var response = new InitializeResponse(
            SessionId: sessionId,
            Protocol: new McpProtocolInfo(),
            Capabilities: new McpServerCapabilities(),
            Tools: tools,
            SseStreamUrl: sseStreamUrl
        );

        _logger.LogInformation("Initialized new MCP session: {SessionId}", sessionId);
        return Ok(response);
    }

    /// <summary>
    /// Establishes a handshake and opens an SSE (Server-Sent Events) stream for the specified session.
    /// This endpoint validates the session ID and opens a persistent SSE connection.
    /// </summary>
    /// <param name="sessionId">The session ID obtained from the Initialize endpoint.</param>
    /// <returns>
    /// An SSE stream with Content-Type: text/event-stream.
    /// Emits a "handshake" event immediately upon connection, followed by tool_result and tool_error events.
    /// </returns>
    /// <response code="200">SSE stream opened successfully.</response>
    /// <response code="404">Session not found or expired.</response>
    [HttpGet("handshake/{sessionId}")]
    [Produces("text/event-stream")]
    public async Task Handshake(string sessionId)
    {
        if (!_sessionService.ValidateSession(sessionId))
        {
            Response.StatusCode = 404;
            await Response.WriteAsync("Session not found or expired");
            return;
        }

        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        var channel = _sessionService.GetOrCreateSseChannel(sessionId);

        var handshakeEvent = new
        {
            type = "handshake",
            status = "ready",
            sessionId = sessionId
        };
        var handshakeMessage = $"event: handshake\ndata: {JsonSerializer.Serialize(handshakeEvent)}\n\n";
        await Response.WriteAsync(handshakeMessage, Encoding.UTF8);
        await Response.Body.FlushAsync();

        _logger.LogInformation("Handshake completed for session: {SessionId}", sessionId);

        try
        {
            await foreach (var message in channel.Reader.ReadAllAsync(HttpContext.RequestAborted))
            {
                await Response.WriteAsync(message, Encoding.UTF8);
                await Response.Body.FlushAsync();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("SSE connection closed for session: {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SSE stream for session: {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Alternative endpoint for SSE stream (alias for handshake).
    /// GET /api/mcp/stream/{sessionId}
    /// </summary>
    /// <param name="sessionId">The session ID.</param>
    /// <returns>SSE stream (same as handshake endpoint).</returns>
    [HttpGet("stream/{sessionId}")]
    [Produces("text/event-stream")]
    public Task Stream(string sessionId) => Handshake(sessionId);

    /// <summary>
    /// Invokes an MCP tool with the provided input.
    /// The tool execution result is also sent via SSE to the session's stream.
    /// </summary>
    /// <param name="request">Tool invocation request containing tool name and input parameters.</param>
    /// <returns>
    /// A <see cref="ToolInvocationResponse"/> containing:
    /// - Tool name that was executed
    /// - Execution result (for check_api_status: HealthCheckResult with status, latency, etc.)
    /// - Error message (if execution failed)
    /// </returns>
    /// <remarks>
    /// Requires X-Session-Id header with a valid session ID.
    /// For check_api_status tool, input should contain: { "url": "https://example.com/health" }
    /// </remarks>
    /// <response code="200">Tool executed successfully.</response>
    /// <response code="400">Invalid tool name or input parameters.</response>
    /// <response code="401">Invalid or missing session ID.</response>
    /// <response code="500">Internal server error during tool execution.</response>
    [HttpPost("tools/invoke")]
    [ProducesResponseType(typeof(ToolInvocationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ToolInvocationResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ToolInvocationResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> InvokeTool([FromBody] ToolInvocationRequest? request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Invalid request: tool name is required" });
        }

        var sessionId = Request.Headers["X-Session-Id"].FirstOrDefault();
        
        if (string.IsNullOrWhiteSpace(sessionId) || !_sessionService.ValidateSession(sessionId))
        {
            return Unauthorized(new { error = "Invalid or missing session ID" });
        }

        try
        {
            var result = await _toolService.ExecuteToolAsync(request.Name, request.Input ?? new Dictionary<string, object>());
            
            await _sessionService.WriteToSessionAsync(sessionId, "tool_result", new
            {
                toolName = request.Name,
                result = result
            });

            var response = new ToolInvocationResponse(
                ToolName: request.Name,
                Result: result,
                Error: null
            );

            _logger.LogInformation("Tool {ToolName} executed successfully for session: {SessionId}", request.Name, sessionId);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid tool invocation: {ToolName}", request.Name);
            
            await _sessionService.WriteToSessionAsync(sessionId, "tool_error", new
            {
                toolName = request.Name,
                error = ex.Message
            });

            return BadRequest(new ToolInvocationResponse(
                ToolName: request.Name,
                Result: null,
                Error: ex.Message
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool {ToolName} for session: {SessionId}", request.Name, sessionId);
            
            await _sessionService.WriteToSessionAsync(sessionId, "tool_error", new
            {
                toolName = request.Name,
                error = "Internal server error"
            });

            return StatusCode(500, new ToolInvocationResponse(
                ToolName: request.Name,
                Result: null,
                Error: "Internal server error"
            ));
        }
    }
}

