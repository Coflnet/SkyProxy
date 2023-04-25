using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Coflnet.Sky.Proxy.Services;
using RestSharp;
using Coflnet.Sky.Core;
using System.Collections.Generic;
using Coflnet.Sky.Updater;
using System.Linq;
using Microsoft.Extensions.Logging;
using System;

namespace Coflnet.Sky.Proxy.Controllers
{
    /// <summary>
    /// Proxy requests to hypixel
    /// </summary>
    [ApiController]
    [Route("[controller]")]
    public class ProxyController : ControllerBase
    {
        private static Prometheus.Counter playerUuidAhRequests = Prometheus.Metrics.CreateCounter("sky_proxy_player_auctions_load", "The count of player which auctions were loaded");
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
            return response.Content.Replace(key, "<redacted key>");
        }

        /// <summary>
        /// Load the auctions of a player
        /// </summary>
        /// <param name="playerUuid"></param>
        /// <param name="missingChecker"></param>
        /// <param name="backgroundService"></param>
        /// <param name="maxAgeSeconds">prefilter auctions to end in the future or less than x seconds ago, 0 is no limit (default)</param>
        /// <param name="hintOwner">Identifer for whoever provided the hint</param>
        /// <returns></returns>
        [HttpGet]
        [Route("hypixel/ah/player/{playerUuid}")]
        public async Task<IEnumerable<SaveAuction>> ProxyPlayerAh(string playerUuid, [FromServices] MissingChecker missingChecker, [FromServices] HypixelBackgroundService backgroundService, int maxAgeSeconds = 0, string hintOwner = "xReborn")
        {
            var key = await keyManager.GetKey("hypixel", 1);
            using var lease = await backgroundService.GetLeaseFor(playerUuid);
            if(!lease.IsAcquired)
                return new List<SaveAuction>();
            var auctions = await missingChecker.GetAuctionOfPlayer(playerUuid, key);
            var minEnd = System.DateTime.UtcNow - System.TimeSpan.FromSeconds(maxAgeSeconds);
            if (maxAgeSeconds == 0)
                minEnd = new System.DateTime(2019, 1, 1);
            playerUuidAhRequests.Inc();

            missingChecker.ProduceAuctions(backgroundService.AuctionProducer, new("pre-api", hintOwner), auctions);
            await Task.Delay(5000);

            return auctions.Where(a => a.End > minEnd);
        }

        /// <summary>
        /// Can proxy anything
        /// </summary>
        [HttpGet]
        [Route("hypixel/status")]
        public async Task<ActionResult> CanyProxy()
        {
            try
            {
                await keyManager.GetKey("hypixel");
            }
            catch (System.Exception)
            {
                return StatusCode(503);
            }
            return Ok();
        }
    }
}
