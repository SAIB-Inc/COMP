# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files and restore
COPY src/COMP.Data/COMP.Data.csproj src/COMP.Data/
COPY src/COMP.API/COMP.API.csproj src/COMP.API/
COPY src/COMP.Sync/COMP.Sync.csproj src/COMP.Sync/
RUN dotnet restore "src/COMP.Sync/COMP.Sync.csproj"

# Copy the remaining source
COPY . .

# Copy production config as appsettings.json
COPY infrastructure/comp-sync/production-config.json ./src/COMP.Sync/appsettings.json

# Publish
RUN dotnet publish "src/COMP.Sync/COMP.Sync.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install socat for Unix socket proxy
RUN apt-get update && apt-get install -y \
    socat \
    && rm -rf /var/lib/apt/lists/*

# Copy published app
COPY --from=build /app/publish .

# Create directory for IPC socket
RUN mkdir -p /ipc

# Copy entrypoint script
COPY infrastructure/comp-sync/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8081 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

# Expose port
EXPOSE 8081

# Run with entrypoint script
ENTRYPOINT ["/entrypoint.sh"]