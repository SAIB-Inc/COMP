# Multi-stage build for COMP (ASP.NET Core 10)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files and restore as distinct layers for better caching
COPY src/COMP/COMP.csproj src/COMP/
RUN dotnet restore "src/COMP/COMP.csproj"

# Copy the remaining source
COPY . .

# Publish to a self-contained directory (framework-dependent)
RUN dotnet publish "src/COMP/COMP.csproj" -c Release -o /app/out --no-restore


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Copy app
COPY --from=build /app/out .

ENV DOTNET_EnableDiagnostics=0 \
    ASPNETCORE_ENVIRONMENT=Production

# Bind to Railway's dynamic $PORT (default 8080 locally) and run
CMD ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} dotnet COMP.dll"]
