FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY src/Hippo.Server/Hippo.Server.csproj src/Hippo.Server/
RUN dotnet restore src/Hippo.Server/Hippo.Server.csproj

COPY . .
RUN dotnet publish src/Hippo.Server/Hippo.Server.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish ./

USER 1654
EXPOSE 8080

ENTRYPOINT ["dotnet", "Hippo.Server.dll"]
