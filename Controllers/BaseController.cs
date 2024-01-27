using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.Proxy.Models;
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Collections;
using System.Collections.Generic;
using Coflnet.Sky.Proxy.Services;
using System.Net.Http;

namespace Coflnet.Sky.Proxy.Controllers
{
    /// <summary>
    /// Main Controller handling tracking
    /// </summary>
    [ApiController]
    [Route("[controller]")]
    public class BaseController : ControllerBase
    {
        private readonly BaseService service;

        /// <summary>
        /// Creates a new instance of <see cref="BaseController"/>
        /// </summary>
        /// <param name="service"></param>
        public BaseController(BaseService service)
        {
            this.service = service;
        }

        /// <summary>
        /// Request ah update for a player
        /// </summary>
        /// <param name="playerId"></param>
        /// <param name="hintSource"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("ah/{playerId}")]
        public async Task RequestAhUpdate(string playerId, string hintSource = "#cofl")
        {
            await service.UpdateAh(playerId, hintSource);
        }
        [HttpPost]
        [Route("ah/loadtest")]
        public async Task LoadTest(int iterations)
        {
            for (int i = 0; i < iterations; i++)
            {
                var guidNoDash = Guid.NewGuid().ToString().Replace("-", "");
                await service.UpdateAh(guidNoDash, "test");
            }
        }

        [HttpPost]
        [Route("key")]
        public async Task AddKey([FromBody] KeyCreate model)
        {
            await service.AddKey(model.Key, model.Party, model.Owner, model.ServerCount);
        }

        [HttpGet]
        [Route("keys/{party}/count")]
        public async Task<int> GetActiveKeyCount(string party)
        {
            return await service.GetActiveKeyCount(party);
        }

        [HttpGet]
        [Route("keys/{party}/invalid")]
        public async Task<IEnumerable<ApiKey>> GetInactiveKeys(string party, int count = 10)
        {
            return await service.GetInactiveKeys(party, count);
        }
    }
}
