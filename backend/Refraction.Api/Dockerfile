FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY backend/Refraction.Api/Refraction.Api.csproj backend/Refraction.Api/
RUN dotnet restore backend/Refraction.Api/Refraction.Api.csproj

COPY backend/Refraction.Api/. backend/Refraction.Api/
RUN dotnet publish backend/Refraction.Api/Refraction.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Refraction.Api.dll"]
