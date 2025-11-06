FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY FraudBank.sln ./
COPY src/FraudService/FraudService.csproj src/FraudService/
COPY src/BuildingBlocks/Contracts/Contracts.csproj src/BuildingBlocks/Contracts/
COPY src/BuildingBlocks/Messaging/Messaging.csproj src/BuildingBlocks/Messaging/
COPY src/FraudService/models/ /app/models/

RUN dotnet restore src/FraudService/FraudService.csproj

COPY . .
RUN dotnet publish src/FraudService/FraudService.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /app/models ./models

ENV ASPNETCORE_URLS=http://+:5042
ENV Onnx__ModelPath=/app/models/fraud_detection_model.onnx
ENV Onnx__MetadataPath=/app/models/metadata.json
EXPOSE 5042

ENTRYPOINT ["dotnet", "FraudService.dll"]
