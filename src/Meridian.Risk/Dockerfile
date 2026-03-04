FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/Meridian.Common/Meridian.Common.csproj Meridian.Common/
COPY src/Meridian.MarketData/Meridian.MarketData.csproj Meridian.MarketData/
COPY src/Meridian.Pricing/Meridian.Pricing.csproj Meridian.Pricing/
COPY src/Meridian.Risk/Meridian.Risk.csproj Meridian.Risk/
RUN dotnet restore Meridian.Risk/Meridian.Risk.csproj

COPY src/Meridian.Common/ Meridian.Common/
COPY src/Meridian.MarketData/ Meridian.MarketData/
COPY src/Meridian.Pricing/ Meridian.Pricing/
COPY src/Meridian.Risk/ Meridian.Risk/
RUN dotnet publish Meridian.Risk/Meridian.Risk.csproj -c Release -o /app -p:ErrorOnDuplicatePublishOutputFiles=false

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Meridian.Risk.dll"]
