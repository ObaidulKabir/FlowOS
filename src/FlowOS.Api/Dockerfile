# Use ASP.NET Core Runtime for the final image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Use SDK for building
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy CSPROJ files first to optimize cache
COPY ["src/FlowOS.Api/FlowOS.Api.csproj", "src/FlowOS.Api/"]
COPY ["src/FlowOS.Application/FlowOS.Application.csproj", "src/FlowOS.Application/"]
COPY ["src/FlowOS.Domain/FlowOS.Domain.csproj", "src/FlowOS.Domain/"]
COPY ["src/FlowOS.Infrastructure/FlowOS.Infrastructure.csproj", "src/FlowOS.Infrastructure/"]
COPY ["src/FlowOS.Core/FlowOS.Core.csproj", "src/FlowOS.Core/"]
COPY ["src/FlowOS.Events/FlowOS.Events.csproj", "src/FlowOS.Events/"]
COPY ["src/FlowOS.Security/FlowOS.Security.csproj", "src/FlowOS.Security/"]
COPY ["src/FlowOS.Agents/FlowOS.Agents.csproj", "src/FlowOS.Agents/"]
COPY ["src/FlowOS.StateMachines/FlowOS.StateMachines.csproj", "src/FlowOS.StateMachines/"]
COPY ["src/FlowOS.Workflows/FlowOS.Workflows.csproj", "src/FlowOS.Workflows/"]

# Restore dependencies
RUN dotnet restore "src/FlowOS.Api/FlowOS.Api.csproj"

# Copy the rest of the source code
COPY . .
WORKDIR "/src/src/FlowOS.Api"
RUN dotnet build "FlowOS.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "FlowOS.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Copy Configuration Files
# We assume the config is at the root of the solution, so we copy it to the container
# In Docker Compose, this should ideally be a volume mount, but we copy for standalone image too.
# The relative path here depends on context. If context is solution root:
# COPY --from=build /src/flowos-config /app/flowos-config 
# But usually config should be external. We will rely on Volume Mount in Compose.

ENTRYPOINT ["dotnet", "FlowOS.Api.dll"]
