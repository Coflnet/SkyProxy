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
        /// <returns></returns>
        [HttpPost]
        [Route("ah/{playerId}")]
        public async Task RequestAhUpdate(string playerId)
        {
            await service.UpdateAh(playerId);
        }
    }
}
