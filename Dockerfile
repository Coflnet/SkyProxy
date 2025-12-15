FROM mcr.microsoft.com/dotnet/sdk:10.0 as build
WORKDIR /build
RUN git clone --depth=1 https://github.com/Coflnet/HypixelSkyblock.git dev
RUN git clone --depth=1 https://github.com/Coflnet/SkyUpdater.git
RUN git clone --depth=1 https://github.com/Ekwav/Hypixel.NET.git
WORKDIR /build/sky
COPY SkyProxy.csproj SkyProxy.csproj
RUN dotnet restore
COPY . .
RUN dotnet publish -c debug -o /app

FROM mcr.microsoft.com/dotnet/sdk:10.0
WORKDIR /app

COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8000

RUN useradd --uid $(shuf -i 2000-65000 -n 1) app-user
USER app-user
RUN export PATH="$PATH:$HOME/.dotnet/tools"

ENTRYPOINT ["dotnet", "SkyProxy.dll", "--hostBuilder:reloadConfigOnChange=false"]
