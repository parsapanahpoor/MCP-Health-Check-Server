using McpHealthServer.Models;

namespace McpHealthServer.Services;

public interface IToolService
{
    /// <summary>
    /// Gets all available tools
    /// </summary>
    McpTool[] GetAvailableTools();
    
    /// <summary>
    /// Executes a tool by name with the given input
    /// </summary>
    Task<object> ExecuteToolAsync(string toolName, Dictionary<string, object> input);
}

