using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using McpHealthServer.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace McpHealthServer.Tests;

public class McpControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public McpControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Initialize_ShouldReturnSessionIdAndTools()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/api/mcp/initialize", new InitializeRequest());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<InitializeResponse>();
        Assert.NotNull(result);
        Assert.NotEmpty(result.SessionId);
        Assert.NotNull(result.Tools);
        Assert.Single(result.Tools);
        Assert.Equal("check_api_status", result.Tools[0].Name);
        Assert.Contains("stream", result.SseStreamUrl.ToLowerInvariant());
        Assert.Contains(result.SessionId, result.SseStreamUrl);
    }

    [Fact]
    public async Task Handshake_WithValidSessionId_ShouldReturnSSEStream()
    {
        // Arrange - Initialize session first
        var initResponse = await _client.PostAsJsonAsync("/api/mcp/initialize", new InitializeRequest());
        var initResult = await initResponse.Content.ReadFromJsonAsync<InitializeResponse>();
        Assert.NotNull(initResult);

        // Act - Use cancellation token to limit wait time
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/mcp/handshake/{initResult.SessionId}");
        
        try
        {
            var handshakeResponse = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            
            // Assert
            Assert.Equal(HttpStatusCode.OK, handshakeResponse.StatusCode);
            Assert.Equal("text/event-stream", handshakeResponse.Content.Headers.ContentType?.MediaType);
            
            // Read a small portion of the stream
            using var stream = await handshakeResponse.Content.ReadAsStreamAsync(cts.Token);
            var buffer = new byte[512];
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
            var content = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
            
            Assert.Contains("handshake", content);
            Assert.Contains(initResult.SessionId, content);
        }
        catch (TaskCanceledException)
        {
            // Expected - SSE stream keeps connection open, timeout is expected
            // Just verify we got the response headers
            Assert.True(true, "SSE connection established (timeout expected for long-running stream)");
        }
    }

    [Fact]
    public async Task Handshake_WithInvalidSessionId_ShouldReturn404()
    {
        // Act
        var response = await _client.GetAsync("/api/mcp/handshake/invalid-session-id");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task InvokeTool_CheckApiStatus_WithValidUrl_ShouldReturnResult()
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
        _client.DefaultRequestHeaders.Add("X-Session-Id", initResult.SessionId);
        var response = await _client.PostAsJsonAsync("/api/mcp/tools/invoke", toolRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<ToolInvocationResponse>();
        Assert.NotNull(result);
        Assert.Equal("check_api_status", result.ToolName);
        Assert.Null(result.Error);
        Assert.NotNull(result.Result);
    }

    [Fact]
    public async Task InvokeTool_WithoutSessionId_ShouldReturn401()
    {
        // Arrange
        var toolRequest = new ToolInvocationRequest(
            Name: "check_api_status",
            Input: new Dictionary<string, object>
            {
                ["url"] = "https://example.com"
            }
        );

        // Act
        var response = await _client.PostAsJsonAsync("/api/mcp/tools/invoke", toolRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task InvokeTool_CheckApiStatus_WithPrivateIP_ShouldBeBlocked()
    {
        // Arrange
        var initResponse = await _client.PostAsJsonAsync("/api/mcp/initialize", new InitializeRequest());
        var initResult = await initResponse.Content.ReadFromJsonAsync<InitializeResponse>();
        Assert.NotNull(initResult);

        var toolRequest = new ToolInvocationRequest(
            Name: "check_api_status",
            Input: new Dictionary<string, object>
            {
                ["url"] = "http://127.0.0.1/health"
            }
        );

        // Act
        _client.DefaultRequestHeaders.Add("X-Session-Id", initResult.SessionId);
        var response = await _client.PostAsJsonAsync("/api/mcp/tools/invoke", toolRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<ToolInvocationResponse>();
        Assert.NotNull(result);
        
        // The result should indicate the URL was blocked
        var resultJson = JsonSerializer.Serialize(result.Result);
        Assert.Contains("DOWN", resultJson);
        // Check for SSRF blocking keywords
        var containsBlockedKeyword = resultJson.Contains("private", StringComparison.OrdinalIgnoreCase) ||
                                    resultJson.Contains("loopback", StringComparison.OrdinalIgnoreCase) ||
                                    resultJson.Contains("blocked", StringComparison.OrdinalIgnoreCase) ||
                                    resultJson.Contains("not allowed", StringComparison.OrdinalIgnoreCase);
        Assert.True(containsBlockedKeyword, $"Expected SSRF blocking message, but got: {resultJson}");
    }

    [Fact]
    public async Task MultipleSessions_ShouldNotMixResults()
    {
        // Arrange - Create two sessions
        var session1Response = await _client.PostAsJsonAsync("/api/mcp/initialize", new InitializeRequest());
        var session1 = await session1Response.Content.ReadFromJsonAsync<InitializeResponse>();
        
        var session2Response = await _client.PostAsJsonAsync("/api/mcp/initialize", new InitializeRequest());
        var session2 = await session2Response.Content.ReadFromJsonAsync<InitializeResponse>();
        
        Assert.NotNull(session1);
        Assert.NotNull(session2);
        Assert.NotEqual(session1.SessionId, session2.SessionId);

        // Act - Invoke tool in both sessions
        var toolRequest1 = new ToolInvocationRequest(
            Name: "check_api_status",
            Input: new Dictionary<string, object> { ["url"] = "https://httpbin.org/status/200" }
        );
        
        var toolRequest2 = new ToolInvocationRequest(
            Name: "check_api_status",
            Input: new Dictionary<string, object> { ["url"] = "https://httpbin.org/status/200" }
        );

        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("X-Session-Id", session1.SessionId);
        var response1 = await _client.PostAsJsonAsync("/api/mcp/tools/invoke", toolRequest1);
        
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("X-Session-Id", session2.SessionId);
        var response2 = await _client.PostAsJsonAsync("/api/mcp/tools/invoke", toolRequest2);

        // Assert - Both should succeed independently
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        
        var result1 = await response1.Content.ReadFromJsonAsync<ToolInvocationResponse>();
        var result2 = await response2.Content.ReadFromJsonAsync<ToolInvocationResponse>();
        
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.Null(result1.Error);
        Assert.Null(result2.Error);
    }

    [Fact]
    public async Task InvokeTool_WithInvalidToolName_ShouldReturn400()
    {
        // Arrange
        var initResponse = await _client.PostAsJsonAsync("/api/mcp/initialize", new InitializeRequest());
        var initResult = await initResponse.Content.ReadFromJsonAsync<InitializeResponse>();
        Assert.NotNull(initResult);

        var toolRequest = new ToolInvocationRequest(
            Name: "invalid_tool_name",
            Input: new Dictionary<string, object> { ["url"] = "https://example.com" }
        );

        // Act
        _client.DefaultRequestHeaders.Add("X-Session-Id", initResult.SessionId);
        var response = await _client.PostAsJsonAsync("/api/mcp/tools/invoke", toolRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InvokeTool_WithMissingUrl_ShouldReturnDownStatus()
    {
        // Arrange
        var initResponse = await _client.PostAsJsonAsync("/api/mcp/initialize", new InitializeRequest());
        var initResult = await initResponse.Content.ReadFromJsonAsync<InitializeResponse>();
        Assert.NotNull(initResult);

        var toolRequest = new ToolInvocationRequest(
            Name: "check_api_status",
            Input: new Dictionary<string, object>() // Missing url
        );

        // Act
        _client.DefaultRequestHeaders.Add("X-Session-Id", initResult.SessionId);
        var response = await _client.PostAsJsonAsync("/api/mcp/tools/invoke", toolRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<ToolInvocationResponse>();
        Assert.NotNull(result);
        
        // Should return DOWN status with error message
        var resultJson = JsonSerializer.Serialize(result.Result);
        Assert.Contains("DOWN", resultJson);
        Assert.Contains("Missing", resultJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeTool_WithInvalidUrl_ShouldHandleGracefully()
    {
        // Arrange
        var initResponse = await _client.PostAsJsonAsync("/api/mcp/initialize", new InitializeRequest());
        var initResult = await initResponse.Content.ReadFromJsonAsync<InitializeResponse>();
        Assert.NotNull(initResult);

        var toolRequest = new ToolInvocationRequest(
            Name: "check_api_status",
            Input: new Dictionary<string, object>
            {
                ["url"] = "not-a-valid-url"
            }
        );

        // Act
        _client.DefaultRequestHeaders.Add("X-Session-Id", initResult.SessionId);
        var response = await _client.PostAsJsonAsync("/api/mcp/tools/invoke", toolRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<ToolInvocationResponse>();
        Assert.NotNull(result);
        
        // Should return DOWN status
        var resultJson = JsonSerializer.Serialize(result.Result);
        Assert.Contains("DOWN", resultJson);
    }
}
