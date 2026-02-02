# Use ASP.NET Core Runtime (or just Runtime) for the final image
# Using aspnet:9.0 to be safe with any potential web dependencies or if we add health checks later
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app

# Use SDK for building
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy CSPROJ files first to optimize cache
COPY ["src/FlowOS.MCP/FlowOS.MCP.csproj", "src/FlowOS.MCP/"]
COPY ["src/FlowOS.Domain/FlowOS.Domain.csproj", "src/FlowOS.Domain/"]
COPY ["src/FlowOS.Infrastructure/FlowOS.Infrastructure.csproj", "src/FlowOS.Infrastructure/"]
COPY ["src/FlowOS.Core/FlowOS.Core.csproj", "src/FlowOS.Core/"]
COPY ["src/FlowOS.Events/FlowOS.Events.csproj", "src/FlowOS.Events/"]
COPY ["src/FlowOS.Security/FlowOS.Security.csproj", "src/FlowOS.Security/"]
COPY ["src/FlowOS.Agents/FlowOS.Agents.csproj", "src/FlowOS.Agents/"]
COPY ["src/FlowOS.StateMachines/FlowOS.StateMachines.csproj", "src/FlowOS.StateMachines/"]
COPY ["src/FlowOS.Workflows/FlowOS.Workflows.csproj", "src/FlowOS.Workflows/"]
COPY ["src/FlowOS.Notifications/FlowOS.Notifications.csproj", "src/FlowOS.Notifications/"]

# Restore dependencies
RUN dotnet restore "src/FlowOS.MCP/FlowOS.MCP.csproj"

# Copy the rest of the source code
COPY . .
WORKDIR "/src/src/FlowOS.MCP"
RUN dotnet build "FlowOS.MCP.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "FlowOS.MCP.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# MCP uses Stdin/Stdout, so we need to ensure the entrypoint is correct
# and that we keep the container alive if it's meant to be persistent (though MCP usually runs on demand).
# However, for a "Service" in Docker Compose, it usually needs to listen on a port (SSE) or be invoked via exec.
# The current MCP implementation is Stdio based.
# If we want to expose it to other containers, we might need an SSE wrapper or just keep it running.
# But standard MCP over Docker usually means running the container interactively.
# Or, if this is for the "Dashboard" to use, the Dashboard cannot easily invoke Stdio of a remote container.
# The prompt says "enable docker for MCP service".
# If it's a "Service", maybe it should expose a port?
# But the current implementation `Program.cs` reads from Console.In.
# Let's assume Stdio mode for now. To use it, one would `docker run -i flowos-mcp`.
# In docker-compose, `stdin_open: true` and `tty: true` might be needed.

ENTRYPOINT ["dotnet", "FlowOS.MCP.dll"]
