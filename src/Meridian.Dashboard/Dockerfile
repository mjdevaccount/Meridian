FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Meridian.Dashboard/Meridian.Dashboard.csproj Meridian.Dashboard/
RUN dotnet restore Meridian.Dashboard/Meridian.Dashboard.csproj
COPY src/Meridian.Dashboard/ Meridian.Dashboard/
RUN dotnet publish Meridian.Dashboard/Meridian.Dashboard.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Meridian.Dashboard.dll"]
