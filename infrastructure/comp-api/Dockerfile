# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /source

# Copy csproj files and restore dependencies
COPY src/COMP/COMP.csproj ./COMP/
RUN dotnet restore COMP/COMP.csproj

# Copy source code
COPY src/COMP/ ./COMP/

# Copy production config as appsettings.json
COPY infrastructure/comp-api/production-config.json ./COMP/appsettings.json

# Build and publish
RUN dotnet publish COMP/COMP.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

# Copy published app
COPY --from=build /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Expose port
EXPOSE 8080

# Run the application
ENTRYPOINT ["dotnet", "COMP.dll"]