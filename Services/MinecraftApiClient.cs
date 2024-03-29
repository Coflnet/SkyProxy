using System.Threading.Tasks;
using Coflnet.Sky.Proxy.Models;
using System.Linq;
using RestSharp;
using System.Collections.Generic;

namespace Coflnet.Sky.Proxy.Services;

public class MinecraftApiClient : IMinecraftApiClient
{
    private RestClient client;
    public MinecraftApiClient()
    {
        client = new RestClient("https://api.mojang.com");
    }
    public async Task<List<NameResponse>> LoadByNameBatch(List<PlayerNameUpdate> nameList)
    {
        var request = new RestRequest("profiles/minecraft", Method.Post);
        request.AddJsonBody(nameList.Where(n => n.Name != null).Select(x => x.Name));
        var response = await client.ExecuteAsync(request);
        var responseJson = Newtonsoft.Json.JsonConvert.DeserializeObject<List<NameResponse>>(response.Content);
        return responseJson;
    }

    public async Task<NameResponse> GetNameByUuid(string uuid)
    {
        var request = new RestRequest($"user/profile/{uuid}", Method.Get);
        var response = await client.ExecuteAsync(request);
        var responseJson = Newtonsoft.Json.JsonConvert.DeserializeObject<NameResponse>(response.Content);
        return responseJson;
    }
}
