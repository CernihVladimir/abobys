# Use Microsoft's official .NET SDK image to build the project
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy csproj and restore as distinct layers
COPY src/*.csproj ./src/
RUN dotnet restore ./src/LmsPlatform.csproj

# Copy everything else and build app
COPY src/ ./src/
WORKDIR /app/src
RUN dotnet publish LmsPlatform.csproj -c Release -o /app/publish /p:UseAppHost=false

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "LmsPlatform.dll"]
