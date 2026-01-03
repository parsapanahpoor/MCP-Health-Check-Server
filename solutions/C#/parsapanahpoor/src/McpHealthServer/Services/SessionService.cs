using System.Collections.Concurrent;
using System.Threading.Channels;
using McpHealthServer.Models;

namespace McpHealthServer.Services;

/// <summary>
/// Thread-safe service for managing MCP sessions and SSE channels.
/// Handles session creation, validation, expiration, and SSE message routing.
/// </summary>
public class SessionService : ISessionService
{
    private readonly ConcurrentDictionary<string, SessionInfo> _sessions = new();
    private readonly ConcurrentDictionary<string, Channel<string>> _sseChannels = new();
    private readonly ILogger<SessionService> _logger;
    private readonly TimeSpan _sessionTimeout = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionService"/> class.
    /// Starts a background task for periodic cleanup of expired sessions.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public SessionService(ILogger<SessionService> logger)
    {
        _logger = logger;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromMinutes(5));
                CleanupExpiredSessions();
            }
        });
    }

    /// <summary>
    /// Creates a new MCP session and returns its unique session ID.
    /// </summary>
    /// <returns>A unique session ID (GUID string).</returns>
    public string CreateSession()
    {
        var sessionId = Guid.NewGuid().ToString();
        var sessionInfo = new SessionInfo
        {
            SessionId = sessionId,
            CreatedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow
        };
        
        _sessions.TryAdd(sessionId, sessionInfo);
        
        _logger.LogInformation("Created new session: {SessionId}", sessionId);
        return sessionId;
    }

    /// <summary>
    /// Validates a session ID and checks if it's still active (not expired).
    /// Updates the last accessed time if session is valid.
    /// </summary>
    /// <param name="sessionId">The session ID to validate.</param>
    /// <returns>True if session exists and is not expired; otherwise, false.</returns>
    public bool ValidateSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return false;

        if (!_sessions.TryGetValue(sessionId, out var session))
            return false;

        // Update last accessed time
        session.LastAccessedAt = DateTime.UtcNow;
        
        // Check if session has expired
        if (DateTime.UtcNow - session.CreatedAt > _sessionTimeout)
        {
            RemoveSession(sessionId);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Writes a message to a session's SSE channel.
    /// The message will be sent to all clients connected to this session's SSE stream.
    /// </summary>
    /// <param name="sessionId">The session ID.</param>
    /// <param name="eventType">SSE event type (e.g., "tool_result", "tool_error").</param>
    /// <param name="data">Data object to serialize and send.</param>
    public async Task WriteToSessionAsync(string sessionId, string eventType, object data)
    {
        if (!ValidateSession(sessionId))
        {
            _logger.LogWarning("Attempted to write to invalid session: {SessionId}", sessionId);
            return;
        }

        if (!_sseChannels.TryGetValue(sessionId, out var channel))
        {
            _logger.LogWarning("No SSE channel found for session: {SessionId}", sessionId);
            return;
        }

        var message = $"event: {eventType}\ndata: {System.Text.Json.JsonSerializer.Serialize(data)}\n\n";
        
        try
        {
            await channel.Writer.WriteAsync(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing to SSE channel for session: {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Removes a session and cleans up its associated SSE channel.
    /// </summary>
    /// <param name="sessionId">The session ID to remove.</param>
    public void RemoveSession(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
        
        if (_sseChannels.TryRemove(sessionId, out var channel))
        {
            channel.Writer.Complete();
        }
        
        _logger.LogInformation("Removed session: {SessionId}", sessionId);
    }

    /// <summary>
    /// Removes all expired sessions (sessions older than the timeout period).
    /// Called periodically by a background task.
    /// </summary>
    public void CleanupExpiredSessions()
    {
        var now = DateTime.UtcNow;
        var expiredSessions = _sessions
            .Where(kvp => now - kvp.Value.CreatedAt > _sessionTimeout)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var sessionId in expiredSessions)
        {
            RemoveSession(sessionId);
        }

        if (expiredSessions.Count > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired sessions", expiredSessions.Count);
        }
    }

    /// <summary>
    /// Gets or creates an SSE channel for a session.
    /// Each session has its own isolated channel for SSE message routing.
    /// </summary>
    /// <param name="sessionId">The session ID.</param>
    /// <returns>A Channel&lt;string&gt; for sending SSE messages to this session.</returns>
    public Channel<string> GetOrCreateSseChannel(string sessionId)
    {
        return _sseChannels.GetOrAdd(sessionId, _ =>
        {
            var channel = Channel.CreateUnbounded<string>();
            _logger.LogInformation("Created SSE channel for session: {SessionId}", sessionId);
            return channel;
        });
    }
}

/// <summary>
/// Internal class representing session metadata.
/// </summary>
internal class SessionInfo
{
    /// <summary>
    /// Gets or sets the unique session identifier.
    /// </summary>
    public string SessionId { get; set; } = string.Empty;
    
    /// <summary>
    /// Gets or sets the UTC timestamp when the session was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// Gets or sets the UTC timestamp of the last access to this session.
    /// </summary>
    public DateTime LastAccessedAt { get; set; }
}

