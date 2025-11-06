FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY FraudBank.sln ./
COPY src/ProjectionService/ProjectionService.csproj src/ProjectionService/
COPY src/BuildingBlocks/Contracts/Contracts.csproj src/BuildingBlocks/Contracts/
COPY src/BuildingBlocks/Messaging/Messaging.csproj src/BuildingBlocks/Messaging/
RUN dotnet restore src/ProjectionService/ProjectionService.csproj

COPY . .
RUN dotnet publish src/ProjectionService/ProjectionService.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5272
EXPOSE 5272

ENTRYPOINT ["dotnet", "ProjectionService.dll"]
