# Multi-stage Dockerfile for MCP Health Check Server

# Base stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy solution and project files
COPY ["McpHealthServer.sln", "./"]
COPY ["McpHealthServer/McpHealthServer.csproj", "McpHealthServer/"]
COPY ["McpHealthServer.Tests/McpHealthServer.Tests.csproj", "McpHealthServer.Tests/"]

# Restore dependencies
RUN dotnet restore "McpHealthServer.sln"

# Copy everything else and build
COPY . .
WORKDIR "/src/McpHealthServer"
RUN dotnet build "McpHealthServer.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "McpHealthServer.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final stage
FROM base AS final
WORKDIR /app

# Copy published files
COPY --from=publish /app/publish .

# Create logs directory
RUN mkdir -p /app/logs

# Health check (using wget which is available in aspnet image)
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
  CMD wget --no-verbose --tries=1 --spider http://localhost/health || exit 1

ENTRYPOINT ["dotnet", "McpHealthServer.dll"]

