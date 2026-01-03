using McpHealthServer.Services;

namespace McpHealthServer;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

// Add User Secrets for development (if needed in future)
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register HTTP client factory (best practice for HttpClient usage)
builder.Services.AddHttpClient();

// Register MCP services
builder.Services.AddSingleton<ISessionService, SessionService>();
builder.Services.AddSingleton<IToolService, ToolService>();
builder.Services.AddSingleton<ISsrfProtectionService, SsrfProtectionService>();

// Add logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<McpHealthServer.Middleware.GlobalExceptionHandlerMiddleware>();

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();

        app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
            .WithName("HealthCheck")
            .WithTags("Health");

        app.Run();
    }
}
