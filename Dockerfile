# LifeLink - ASP.NET Core 10 Dockerfile for Render.com
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY ["LifeLink.csproj", "."]
RUN dotnet restore

# Copy all source files
COPY . .

# Publish in Release mode
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 2: Runtime image (smaller)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Copy published output from build stage
COPY --from=build /app/publish .

# Render uses PORT environment variable - default 10000
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 10000

ENTRYPOINT ["dotnet", "LifeLink.dll"]
