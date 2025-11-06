FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY FraudBank.sln ./
COPY src/TransactionService/TransactionService.csproj src/TransactionService/
COPY src/BuildingBlocks/Contracts/Contracts.csproj src/BuildingBlocks/Contracts/
COPY src/BuildingBlocks/Messaging/Messaging.csproj src/BuildingBlocks/Messaging/
RUN dotnet restore src/TransactionService/TransactionService.csproj

COPY . .
RUN dotnet publish src/TransactionService/TransactionService.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5032
EXPOSE 5032

ENTRYPOINT ["dotnet", "TransactionService.dll"]
