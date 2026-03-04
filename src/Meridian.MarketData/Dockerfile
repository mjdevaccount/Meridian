FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/Meridian.Common/Meridian.Common.csproj Meridian.Common/
COPY src/Meridian.MarketData/Meridian.MarketData.csproj Meridian.MarketData/
RUN dotnet restore Meridian.MarketData/Meridian.MarketData.csproj

COPY src/Meridian.Common/ Meridian.Common/
COPY src/Meridian.MarketData/ Meridian.MarketData/
RUN dotnet publish Meridian.MarketData/Meridian.MarketData.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Meridian.MarketData.dll"]
