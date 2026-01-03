using System.Threading.Channels;
using McpHealthServer.Models;

namespace McpHealthServer.Services;

public interface ISessionService
{
    /// <summary>
    /// Creates a new session and returns its ID
    /// </summary>
    string CreateSession();
    
    /// <summary>
    /// Validates if a session exists and is still active
    /// </summary>
    bool ValidateSession(string sessionId);
    
    /// <summary>
    /// Gets or creates an SSE channel for a session
    /// </summary>
    Channel<string> GetOrCreateSseChannel(string sessionId);
    
    /// <summary>
    /// Writes data to a session's SSE channel
    /// </summary>
    Task WriteToSessionAsync(string sessionId, string eventType, object data);
    
    /// <summary>
    /// Removes a session and cleans up resources
    /// </summary>
    void RemoveSession(string sessionId);
    
    /// <summary>
    /// Cleans up expired sessions
    /// </summary>
    void CleanupExpiredSessions();
}

