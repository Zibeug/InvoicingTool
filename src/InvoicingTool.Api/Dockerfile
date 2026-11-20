# API de synchronisation — image construite depuis la racine du dépôt :
#   docker build -f src/InvoicingTool.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restauration des paquets d'abord (mise en cache tant que les .csproj ne changent pas).
COPY src/InvoicingTool.Sync.Contracts/InvoicingTool.Sync.Contracts.csproj src/InvoicingTool.Sync.Contracts/
COPY src/InvoicingTool.Api/InvoicingTool.Api.csproj src/InvoicingTool.Api/
RUN dotnet restore src/InvoicingTool.Api/InvoicingTool.Api.csproj

COPY src/InvoicingTool.Sync.Contracts/ src/InvoicingTool.Sync.Contracts/
COPY src/InvoicingTool.Api/ src/InvoicingTool.Api/
RUN dotnet publish src/InvoicingTool.Api/InvoicingTool.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# Utilisateur non privilégié fourni par l'image .NET
USER $APP_UID
ENTRYPOINT ["dotnet", "InvoicingTool.Api.dll"]
