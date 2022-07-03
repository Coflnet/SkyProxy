VERSION=0.0.1

docker run --rm -v "${PWD}:/local" --network host -u $(id -u ${USER}):$(id -g ${USER})  openapitools/openapi-generator-cli generate \
-i http://localhost:5000/swagger/v1/swagger.json \
-g csharp-netcore \
-o /local/out --additional-properties=packageName=Coflnet.Sky.Proxy.Client,packageVersion=$VERSION,licenseId=MIT

cd out
sed -i 's/GIT_USER_ID/Coflnet/g' src/Coflnet.Sky.Proxy.Client/Coflnet.Sky.Proxy.Client.csproj
sed -i 's/GIT_REPO_ID/SkyProxy/g' src/Coflnet.Sky.Proxy.Client/Coflnet.Sky.Proxy.Client.csproj
sed -i 's/>OpenAPI/>Coflnet/g' src/Coflnet.Sky.Proxy.Client/Coflnet.Sky.Proxy.Client.csproj

dotnet pack
cp src/Coflnet.Sky.Proxy.Client/bin/Debug/Coflnet.Sky.Proxy.Client.*.nupkg ..
