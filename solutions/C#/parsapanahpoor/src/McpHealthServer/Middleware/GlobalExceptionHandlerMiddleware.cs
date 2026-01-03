using System.Net;
using System.Text.Json;

namespace McpHealthServer.Middleware;

/// <summary>
/// Global exception handler middleware for consistent error responses.
/// This middleware catches unhandled exceptions and returns appropriate HTTP status codes.
/// </summary>
public class GlobalExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

    public GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var code = HttpStatusCode.InternalServerError;
        var message = "An error occurred while processing your request.";

        // Handle specific exception types
        if (exception is ArgumentException || exception is ArgumentNullException)
        {
            code = HttpStatusCode.BadRequest;
            message = exception.Message;
        }
        else if (exception is UnauthorizedAccessException)
        {
            code = HttpStatusCode.Unauthorized;
            message = "Unauthorized access.";
        }
        else if (exception is KeyNotFoundException)
        {
            code = HttpStatusCode.NotFound;
            message = "The requested resource was not found.";
        }

        var result = JsonSerializer.Serialize(new
        {
            error = new
            {
                message = message,
                statusCode = (int)code
            }
        });

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)code;

        return context.Response.WriteAsync(result);
    }
}
