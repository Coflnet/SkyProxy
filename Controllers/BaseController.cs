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
        /// Tracks a flip
        /// </summary>
        /// <param name="flip"></param>
        /// <param name="AuctionId"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("flip/{AuctionId}")]
        public async Task<ApiKey> TrackFlip([FromBody] ApiKey flip, string AuctionId)
        {
            await service.AddFlip(flip);
            return flip;
        }
    }
}
