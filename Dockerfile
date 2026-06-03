FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build
RUN git clone --depth=1 https://github.com/Coflnet/HypixelSkyblock.git dev
RUN git clone --depth=1 https://github.com/Coflnet/SkyUpdater.git
RUN git clone --depth=1 https://github.com/Ekwav/Hypixel.NET.git
WORKDIR /build/sky
COPY SkyProxy.csproj SkyProxy.csproj
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app \
    -p:PublishTrimmed=true \
    -p:TrimMode=partial \
    -p:ReadyToRun=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8000

# Chiseled image includes a pre-configured non-root 'app' user (uid 1654)
USER app

ENTRYPOINT ["dotnet", "SkyProxy.dll", "--hostBuilder:reloadConfigOnChange=false"]
