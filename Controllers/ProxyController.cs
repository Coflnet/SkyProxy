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

        /// <summary>
        /// Proxy the path to hypixel using the assigned key (no key required)
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        [HttpGet]
        [Route("hypixel")]
        public async Task<string> Proxy(string path)
        {
            var request = new RestRequest(path, Method.Get);
            var key = await keyManager.GetKey("hypixel");
            request.AddQueryParameter("key", key);
            var response = await restClient.ExecuteAsync(request);
            await keyManager.UsedKey("hypixel", key);
            return response.Content.Replace(key,"<redacted key>");
        }
    }
}
