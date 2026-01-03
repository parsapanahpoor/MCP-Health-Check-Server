# MCP Health Check Server - Solution by parsapanahpoor

Standalone MCP Server implementation for health checking cloud services using HTTP + SSE protocol.

## Quick Start

### Prerequisites

- .NET 8.0 SDK or later
- Any IDE (Visual Studio, VS Code, Rider)

### Build and Run

```bash
cd src/McpHealthServer
dotnet restore
dotnet build
dotnet run
```

Or from the solution root:

```bash
dotnet restore
dotnet build
dotnet run --project src/McpHealthServer
```

The server will start on `https://localhost:5001` (or `http://localhost:5000`).

### Run Tests

```bash
dotnet test
```

### Run with Docker

```bash
cd docker
docker-compose up -d
```

The server will be available at `http://localhost:5000`

For more details, see [docker/README.md](./docker/README.md)

## Architecture

### Project Structure

```
McpHealthServer/
├── Controllers/
│   └── McpController.cs          # MCP API endpoints
├── Models/
│   └── McpModels.cs              # DTOs and data models
├── Services/
│   ├── ISessionService.cs        # Session management interface
│   ├── SessionService.cs         # Thread-safe session management
│   ├── IToolService.cs           # Tool execution interface
│   ├── ToolService.cs            # Health check tool implementation
│   ├── ISsrfProtectionService.cs # SSRF protection interface
│   └── SsrfProtectionService.cs  # SSRF protection implementation
└── Program.cs                    # Application entry point
```

### Key Components

1. **Session Management** (`SessionService`)
   - Thread-safe session storage using `ConcurrentDictionary`
   - Automatic cleanup of expired sessions (30-minute TTL)
   - SSE channel management per session
   - Isolated sessions with no cross-talk

2. **Tool Execution** (`ToolService`)
   - Implements `check_api_status` tool
   - HTTP health checking with configurable timeout (default: 3000ms)
   - Error handling for timeouts, DNS failures, TLS errors

3. **SSRF Protection** (`SsrfProtectionService`)
   - Blocks private IP ranges (127.0.0.0/8, 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16)
   - Blocks loopback addresses (IPv4 and IPv6)
   - Optional domain allowlist (configurable via `appsettings.json`)
   - Blocks link-local addresses

## API Endpoints

### 1. Initialize Session

```http
POST /api/mcp/initialize
Content-Type: application/json

{}
```

**Response:**
```json
{
  "sessionId": "guid-here",
  "protocol": {
    "version": "2024-11-05",
    "protocolVersion": "1.0"
  },
  "capabilities": {
    "tools": true
  },
  "tools": [
    {
      "name": "check_api_status",
      "description": "Checks the health status of an API endpoint...",
      "inputSchema": { ... }
    }
  ],
  "sseStreamUrl": "http://localhost:5000/api/mcp/stream/{sessionId}"
}
```

### 2. Handshake / SSE Stream

```http
GET /api/mcp/handshake/{sessionId}
Accept: text/event-stream
```

**Response:** SSE stream with `text/event-stream` content type

**Events:**
- `handshake` - Initial handshake confirmation
- `tool_result` - Tool execution results
- `tool_error` - Tool execution errors

### 3. Invoke Tool

```http
POST /api/mcp/tools/invoke
Content-Type: application/json
X-Session-Id: {sessionId}

{
  "name": "check_api_status",
  "input": {
    "url": "https://example.com/health"
  }
}
```

**Response:**
```json
{
  "toolName": "check_api_status",
  "result": {
    "url": "https://example.com/health",
    "status": "UP",
    "httpStatus": 200,
    "latencyMs": 87,
    "checkedAt": "2025-12-30T12:00:00Z"
  },
  "error": null
}
```

## Configuration

### appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Security": {
    "AllowedDomains": []
  }
}
```

### Security Configuration

- **AllowedDomains**: Array of allowed domains (empty = allow all public domains)
  - Example: `["example.com", "api.example.com"]`
  - If empty, all public domains are allowed (private IPs are still blocked)

### Environment Variables & User Secrets

This implementation uses standard .NET configuration. For any future secrets (API keys, tokens, etc.), use:

**User Secrets (Development):**
```bash
dotnet user-secrets set "Security:ApiKey" "your-secret-key"
```

**Environment Variables (Production):**
```bash
export Security__ApiKey="your-secret-key"
```

**Note:** Currently, no secrets are required. The configuration uses only non-sensitive settings (timeout, allowed domains).

## Features

### ✅ MCP Lifecycle

- **Initialize**: Creates session and returns tools list
- **Handshake**: Establishes SSE connection with session validation
- **Tool Invocation**: Executes tools with proper error handling

### ✅ Multi-Client Support

- Thread-safe session management
- Isolated SSE streams per session
- No cross-talk between sessions
- Automatic session cleanup (30-minute TTL)

### ✅ Security

- **SSRF Protection**: Blocks private IPs and loopback addresses
- **Domain Allowlist**: Optional domain filtering
- **Request Timeouts**: Configurable timeout (default: 3000ms)
- **Input Validation**: Validates tool inputs before execution

### ✅ Error Handling

- Timeout handling (returns DOWN with timeout message)
- DNS failure handling
- TLS error handling
- Invalid URL handling
- Network error handling

## Testing

### Test Coverage

**19 comprehensive integration tests** covering:

1. ✅ Initialize returns session ID and tools
2. ✅ Initialize returns unique session IDs
3. ✅ Initialize returns correct protocol info
4. ✅ Initialize returns correct capabilities
5. ✅ Initialize returns check_api_status tool with correct schema
6. ✅ Handshake establishes SSE connection
7. ✅ Handshake emits handshake event
8. ✅ Handshake rejects invalid session IDs
9. ✅ Tool invocation with valid URL (UP status)
10. ✅ Tool invocation with HTTP error status (DOWN)
11. ✅ Tool invocation with HTTP success status (UP)
12. ✅ Tool invocation without session ID (401)
13. ✅ SSRF protection blocks private IPs
14. ✅ Multiple sessions don't mix results
15. ✅ Invalid tool name returns 400
16. ✅ Missing URL parameter handled gracefully
17. ✅ Invalid URL format handled gracefully
18. ✅ Tool invocation with timeout handled gracefully
19. ✅ Tool invocation with SSE channel works correctly

### Running Tests

```bash
dotnet test
```

## Design Decisions

1. **Session Storage**: Using `ConcurrentDictionary` for thread-safe access
2. **SSE Channels**: Using `System.Threading.Channels` for efficient message passing
3. **SSRF Protection**: Default deny for private ranges, configurable allowlist
4. **Error Handling**: Global exception handler middleware for consistent error responses
5. **Timeout**: Default 3000ms (configurable)
6. **Session TTL**: 30 minutes with automatic cleanup every 5 minutes

## Security Notes

- ✅ No secrets in configuration files
- ✅ SSRF protection enabled by default
- ✅ Private IP ranges blocked
- ✅ Configurable domain allowlist
- ✅ Request timeout limits
- ✅ Input validation

## Deployment

### Docker

The project includes Docker support for easy deployment:

```bash
cd docker
docker-compose up -d
```

See [docker/README.md](./docker/README.md) for detailed deployment instructions.

### Dockerfile

Multi-stage Dockerfile optimized for production:
- Base image: `mcr.microsoft.com/dotnet/aspnet:8.0`
- Build stage: `mcr.microsoft.com/dotnet/sdk:8.0`
- Health check included
- Minimal image size

## Limitations / Future Improvements

- Session TTL is fixed (could be configurable)
- Tool timeout is fixed (could be per-request configurable)
- No authentication/authorization (could be added for production)
- SSE connection handling could be improved for better error recovery

