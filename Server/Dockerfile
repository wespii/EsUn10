FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Server/EsUn10Pero.Server.csproj Server/
RUN dotnet restore Server/EsUn10Pero.Server.csproj
COPY Server/ Server/
RUN dotnet publish Server/EsUn10Pero.Server.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 10000
ENTRYPOINT ["dotnet", "EsUn10Pero.Server.dll"]
