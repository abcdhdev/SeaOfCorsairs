FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src

COPY SeaWars.Backend.sln ./
COPY src/SeaWars.Backend.Common/SeaWars.Backend.Common.csproj src/SeaWars.Backend.Common/
COPY src/SeaWars.AuthService/SeaWars.AuthService.csproj src/SeaWars.AuthService/
RUN dotnet restore src/SeaWars.AuthService/SeaWars.AuthService.csproj

COPY src/ src/
WORKDIR /src/src/SeaWars.AuthService
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:6.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SeaWars.AuthService.dll"]

