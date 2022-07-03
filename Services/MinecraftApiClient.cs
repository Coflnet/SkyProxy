using System.Threading.Tasks;
using Coflnet.Sky.Proxy.Models;
using System.Linq;
using RestSharp;
using System.Collections.Generic;

namespace Coflnet.Sky.Proxy.Services;

public class MinecraftApiClient : IMinecraftApiClient
{
    private RestClient client;
    public MinecraftApiClient(string url)
    {
        client = new RestClient(url);
    }
    public async Task<List<NameResponse>> LoadByNameBatch(List<PlayerNameUpdate> nameList)
    {
        var request = new RestRequest("profiles/minecraft", Method.POST);
        request.AddJsonBody(nameList.Where(n => n.Name != null).Select(x => x.Name));
        var response = await client.ExecuteAsync(request);
        var responseJson = Newtonsoft.Json.JsonConvert.DeserializeObject<List<NameResponse>>(response.Content);
        return responseJson;
    }

    public async Task<NameResponse> GetNameByUuid(string uuid)
    {
        var request = new RestRequest($"user/profile/{uuid}", Method.GET);
        var response = await client.ExecuteAsync(request);
        var responseJson = Newtonsoft.Json.JsonConvert.DeserializeObject<NameResponse>(response.Content);
        return responseJson;
    }
}
