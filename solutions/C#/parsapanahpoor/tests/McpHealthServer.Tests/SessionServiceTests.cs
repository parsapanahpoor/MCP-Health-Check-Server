using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using McpHealthServer.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace McpHealthServer.Tests;

/// <summary>
/// Tests for session management functionality including expiration and cleanup.
/// </summary>
public class SessionServiceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public SessionServiceTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Initialize_ShouldCreateUniqueSessionIds()
    {
        // Act - Create multiple sessions
        var response1 = await _client.PostAsJsonAsync("/api/mcp/initialize", new InitializeRequest());
        var response2 = await _client.PostAsJsonAsync("/api/mcp/initialize", new InitializeRequest());
        var response3 = await _client.PostAsJsonAsync("/api/mcp/initialize", new InitializeRequest());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response3.StatusCode);

        var result1 = await response1.Content.ReadFromJsonAsync<InitializeResponse>();
        var result2 = await response2.Content.ReadFromJsonAsync<InitializeResponse>();
        var result3 = await response3.Content.ReadFromJsonAsync<InitializeResponse>();

        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.NotNull(result3);

        // All session IDs should be unique
        Assert.NotEqual(result1.SessionId, result2.SessionId);
        Assert.NotEqual(result2.SessionId, result3.SessionId);
        Assert.NotEqual(result1.SessionId, result3.SessionId);
    }

    [Fact]
    public async Task Handshake_ShouldEmitHandshakeEvent()
    {
        // Arrange
        var initResponse = await _client.PostAsJsonAsync("/api/mcp/initialize", new InitializeRequest());
        var initResult = await initResponse.Content.ReadFromJsonAsync<InitializeResponse>();
        Assert.NotNull(initResult);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/mcp/handshake/{initResult.SessionId}");
        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        var content = await reader.ReadLineAsync();

        // Should contain handshake event
        Assert.NotNull(content);
        Assert.Contains("event: handshake", content);
    }

    [Fact]
    public async Task ToolInvocation_WithSSEChannel_ShouldWork()
    {
        // Arrange
        var initResponse = await _client.PostAsJsonAsync("/api/mcp/initialize", new InitializeRequest());
        var initResult = await initResponse.Content.ReadFromJsonAsync<InitializeResponse>();
        Assert.NotNull(initResult);

        // Establish SSE connection first to create the channel
        using var sseCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var sseRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/mcp/handshake/{initResult.SessionId}");
        var sseResponse = await _client.SendAsync(sseRequest, HttpCompletionOption.ResponseHeadersRead, sseCts.Token);
        Assert.Equal(HttpStatusCode.OK, sseResponse.StatusCode);
        
        // Wait a bit for channel to be established
        await Task.Delay(300);

        // Act - Invoke tool (should send event to SSE channel)
        var toolRequest = new ToolInvocationRequest(
            Name: "check_api_status",
            Input: new Dictionary<string, object> { ["url"] = "https://httpbin.org/status/200" }
        );

        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("X-Session-Id", initResult.SessionId);
        var toolResponse = await _client.PostAsJsonAsync("/api/mcp/tools/invoke", toolRequest);

        // Assert - Tool should execute successfully
        Assert.Equal(HttpStatusCode.OK, toolResponse.StatusCode);
        
        var result = await toolResponse.Content.ReadFromJsonAsync<ToolInvocationResponse>();
        Assert.NotNull(result);
        Assert.Equal("check_api_status", result.ToolName);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Initialize_ShouldReturnCorrectProtocolInfo()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/api/mcp/initialize", new InitializeRequest());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<InitializeResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.Protocol);
        Assert.Equal("2024-11-05", result.Protocol.Version);
        Assert.Equal("1.0", result.Protocol.ProtocolVersion);
    }

    [Fact]
    public async Task Initialize_ShouldReturnCorrectCapabilities()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/api/mcp/initialize", new InitializeRequest());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<InitializeResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.Capabilities);
        Assert.True(result.Capabilities.Tools);
    }

    [Fact]
    public async Task Initialize_ShouldReturnCheckApiStatusTool()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/api/mcp/initialize", new InitializeRequest());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<InitializeResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result.Tools);
        Assert.Single(result.Tools);
        
        var tool = result.Tools[0];
        Assert.Equal("check_api_status", tool.Name);
        Assert.NotNull(tool.Description);
        Assert.NotNull(tool.InputSchema);
        Assert.Equal("object", tool.InputSchema.Type);
        Assert.Contains("url", tool.InputSchema.Required);
    }

    [Fact]
    public async Task InvokeTool_WithTimeout_ShouldReturnDownStatus()
    {
        // Arrange - Use a URL that will timeout
        var initResponse = await _client.PostAsJsonAsync("/api/mcp/initialize", new InitializeRequest());
        var initResult = await initResponse.Content.ReadFromJsonAsync<InitializeResponse>();
        Assert.NotNull(initResult);

        // Use a non-routable IP that will timeout
        var toolRequest = new ToolInvocationRequest(
            Name: "check_api_status",
            Input: new Dictionary<string, object>
            {
                ["url"] = "http://192.0.2.0" // Test-Net address, should timeout or be blocked
            }
        );

        // Act
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("X-Session-Id", initResult.SessionId);
        var response = await _client.PostAsJsonAsync("/api/mcp/tools/invoke", toolRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ToolInvocationResponse>();
        Assert.NotNull(result);
        Assert.Equal("check_api_status", result.ToolName);

        var resultJson = JsonSerializer.Serialize(result.Result);
        Assert.Contains("DOWN", resultJson);
    }

    [Fact]
    public async Task InvokeTool_WithHttpErrorStatus_ShouldReturnDown()
    {
        // Arrange
        var initResponse = await _client.PostAsJsonAsync("/api/mcp/initialize", new InitializeRequest());
        var initResult = await initResponse.Content.ReadFromJsonAsync<InitializeResponse>();
        Assert.NotNull(initResult);

        var toolRequest = new ToolInvocationRequest(
            Name: "check_api_status",
            Input: new Dictionary<string, object>
            {
                ["url"] = "https://httpbin.org/status/500"
            }
        );

        // Act
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("X-Session-Id", initResult.SessionId);
        var response = await _client.PostAsJsonAsync("/api/mcp/tools/invoke", toolRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ToolInvocationResponse>();
        Assert.NotNull(result);

        var resultJson = JsonSerializer.Serialize(result.Result);
        Assert.Contains("DOWN", resultJson);
        Assert.Contains("500", resultJson);
    }

    [Fact]
    public async Task InvokeTool_WithHttpSuccessStatus_ShouldReturnUp()
    {
        // Arrange
        var initResponse = await _client.PostAsJsonAsync("/api/mcp/initialize", new InitializeRequest());
        var initResult = await initResponse.Content.ReadFromJsonAsync<InitializeResponse>();
        Assert.NotNull(initResult);

        var toolRequest = new ToolInvocationRequest(
            Name: "check_api_status",
            Input: new Dictionary<string, object>
            {
                ["url"] = "https://httpbin.org/status/200"
            }
        );

        // Act
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("X-Session-Id", initResult.SessionId);
        var response = await _client.PostAsJsonAsync("/api/mcp/tools/invoke", toolRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ToolInvocationResponse>();
        Assert.NotNull(result);

        var resultJson = JsonSerializer.Serialize(result.Result);
        Assert.Contains("UP", resultJson);
        Assert.Contains("200", resultJson);
        Assert.Contains("latency_ms", resultJson);
        Assert.Contains("checked_at", resultJson);
    }
}
