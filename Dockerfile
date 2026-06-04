FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build

# Increment to bust Docker cache when upstream repos change
ARG CACHE_BUST=0
RUN git clone --depth=1 https://github.com/Coflnet/HypixelSkyblock.git dev && \
    git clone --depth=1 https://github.com/Coflnet/SkyUpdater.git && \
    git clone --depth=1 https://github.com/Ekwav/Hypixel.NET.git && \
    : "cache bust: ${CACHE_BUST}"
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
