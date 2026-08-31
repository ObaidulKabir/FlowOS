# Use ASP.NET Core Runtime — supports both stdio and Streamable HTTP transports
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

# Use SDK for building
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy CSPROJ files first to optimize cache
COPY ["src/FlowOS.MCP/FlowOS.MCP.csproj", "src/FlowOS.MCP/"]
COPY ["src/FlowOS.Application/FlowOS.Application.csproj", "src/FlowOS.Application/"]
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

ENV MCP_TRANSPORT=http
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "FlowOS.MCP.dll"]
