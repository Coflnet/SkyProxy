using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Coflnet.Sky.Proxy.Services;
using RestSharp;

namespace Coflnet.Sky.Proxy.Controllers
{
    /// <summary>
    /// Proxy requests to hypixel
    /// </summary>
    [ApiController]
    [Route("[controller]")]
    public class ProxyController : ControllerBase
    {
        private readonly RestClient restClient = new RestClient("https://api.hypixel.net");
        private readonly KeyManager keyManager;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="keyManager"></param>
        public ProxyController(KeyManager keyManager)
        {
            this.keyManager = keyManager;
        }

        [HttpGet]
        public async Task<string> Proxy(string path)
        {
            var request = new RestRequest(path, Method.GET);
            var key = await keyManager.GetKey("hypixel");
            request.AddQueryParameter("key", key);
            var response = await restClient.ExecuteAsync(request);
            await keyManager.UsedKey("hypixel", key);
            return response.Content;
        }
    }
}
