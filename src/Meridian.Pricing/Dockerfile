FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/Meridian.Common/Meridian.Common.csproj Meridian.Common/
COPY src/Meridian.MarketData/Meridian.MarketData.csproj Meridian.MarketData/
COPY src/Meridian.Pricing/Meridian.Pricing.csproj Meridian.Pricing/
RUN dotnet restore Meridian.Pricing/Meridian.Pricing.csproj

COPY src/Meridian.Common/ Meridian.Common/
COPY src/Meridian.MarketData/ Meridian.MarketData/
COPY src/Meridian.Pricing/ Meridian.Pricing/
RUN dotnet publish Meridian.Pricing/Meridian.Pricing.csproj -c Release -o /app -p:ErrorOnDuplicatePublishOutputFiles=false

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Meridian.Pricing.dll"]
