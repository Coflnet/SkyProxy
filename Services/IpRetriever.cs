using System.Threading.Tasks;
using RestSharp;

namespace Coflnet.Sky.Proxy.Services;

public interface IIpRetriever
{
    Task<string> GetIp();
}

public class IpRetriever : IIpRetriever
{
    string ip;
    public async Task<string> GetIp()
    {
        if(this.ip != null)
            return this.ip;
        var c1 = new RestClient("http://checkip.dyndns.org/");
        var r1 = await c1.ExecuteAsync(new RestRequest());
        if (r1.StatusCode == System.Net.HttpStatusCode.OK)
        {
            return this.ip = r1.Content.Split(':')[1].Split('<')[0].Trim();
        }
        string ip = null;
        foreach (var item in new string[] { "https://api.ipify.org", "https://icanhazip.com", "https://ifconfig.io/ip" })
        {
            var c = new RestClient(item);
            var r = await c.ExecuteAsync(new RestRequest());
            if (r.StatusCode == System.Net.HttpStatusCode.OK)
            {
                ip = r.Content;
                break;
            }
        }
        this.ip = ip;
        return ip;

    }
}
