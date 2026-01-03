# Docker Deployment Guide

This directory contains Docker-related files for the MCP Health Check Server.

## Quick Start

### Build and Run with Docker Compose

```bash
cd docker
docker-compose up -d
```

The server will be available at:
- HTTP: http://localhost:5000
- Health Check: http://localhost:5000/health
- Swagger: http://localhost:5000/swagger (if enabled)

### Build Docker Image Manually

```bash
docker build -f docker/Dockerfile -t mcp-health-server ..
```

### Run Container

```bash
docker run -d \
  --name mcp-health-server \
  -p 5000:80 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  mcp-health-server
```

## Docker Compose Services

- **mcp-server**: The MCP Health Check Server API
  - Ports: 80 (container) → 5000 (host)
  - Health check: `/health` endpoint
  - Auto-restart: unless-stopped

## Environment Variables

You can customize the server using environment variables:

```yaml
environment:
  - ASPNETCORE_ENVIRONMENT=Production
  - ASPNETCORE_URLS=http://+:80
  - Logging__LogLevel__Default=Information
  - Security__AllowedDomains__0=example.com
  - Security__AllowedDomains__1=api.example.com
```

## Health Checks

The container includes a health check that monitors the `/health` endpoint:
- Interval: 30 seconds
- Timeout: 3 seconds
- Retries: 3
- Start period: 10 seconds

Check container health:
```bash
docker ps
docker inspect mcp-health-server | grep -A 10 Health
```

## Logs

View logs:
```bash
docker-compose logs -f mcp-server
```

Logs are also mounted to `../logs` directory.

## Production Deployment

For production deployment, consider:

1. **Environment Variables**: Use secrets management (Docker secrets, Kubernetes secrets, etc.)
2. **Reverse Proxy**: Use nginx or traefik in front of the container
3. **SSL/TLS**: Configure HTTPS with certificates
4. **Resource Limits**: Set memory and CPU limits
5. **Monitoring**: Add Prometheus/Grafana for metrics

### Example with Resource Limits

```yaml
services:
  mcp-server:
    deploy:
      resources:
        limits:
          cpus: '1'
          memory: 512M
        reservations:
          cpus: '0.5'
          memory: 256M
```

## Kubernetes Deployment

See `DEPLOYMENT.md` for Kubernetes deployment examples.

