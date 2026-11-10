FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY src/*.csproj ./src/
RUN dotnet restore ./src/LmsPlatform.csproj

COPY src/ ./src/
WORKDIR /app/src
RUN dotnet publish LmsPlatform.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "LmsPlatform.dll"]
