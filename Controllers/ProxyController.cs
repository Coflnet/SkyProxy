using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Coflnet.Sky.Proxy.Services;
using RestSharp;
using Coflnet.Sky.Core;
using System.Collections.Generic;
using Coflnet.Sky.Updater;
using System.Linq;

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

        /// <summary>
        /// Load the auctions of a player
        /// </summary>
        /// <param name="playerUuid"></param>
        /// <param name="missingChecker"></param>
        /// <param name="maxAgeSeconds">prefilter auctions to end in the future or less than x seconds ago, 0 is no limit (default)</param>
        /// <returns></returns>
        [HttpGet]
        [Route("hypixel/ah/player/{playerUuid}")]
        public async Task<IEnumerable<SaveAuction>> ProxyPlayerAh(string playerUuid, [FromServices] MissingChecker missingChecker, int maxAgeSeconds = 0)
        {
            var key = await keyManager.GetKey("hypixel", 1);
            var auctions = await missingChecker.GetAuctionOfPlayer(playerUuid,key);
            var minEnd = System.DateTime.UtcNow - System.TimeSpan.FromSeconds(maxAgeSeconds);
            if(maxAgeSeconds == 0)
                minEnd = new System.DateTime(2019, 1, 1);
            return auctions.Where(a=>a.End > minEnd);
        }

        /// <summary>
        /// Can proxy anything
        /// </summary>
        [HttpGet]
        [Route("hypixel/status")]
        public async Task CanyProxy()
        {
            await keyManager.GetKey("hypixel");
        }
    }
}
