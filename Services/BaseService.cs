using System.Threading.Tasks;
using Coflnet.Sky.Proxy.Models;
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using RestSharp;
using Newtonsoft.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Coflnet.Sky.Core;
using StackExchange.Redis;

namespace Coflnet.Sky.Proxy.Services
{
    public class BaseService
    {
        private ProxyDbContext db;
        public int RequestsSinceStart { get; private set; }
        /// <summary>
        /// Is set to the last time the ip was rate limited by Mojang
        /// </summary>
        /// <returns></returns>
        private static DateTime BlockedSince = new DateTime(0);
        private IConfiguration config;
        private ConnectionMultiplexer redis;

        public BaseService(ProxyDbContext db, IConfiguration config)
        {
            this.db = db;
            this.config = config;
        }

        public async Task UpdateAh(string playerId)
        {
            var db = redis.GetDatabase();
            var mId = db.StreamAdd("ah-update", "uuid", playerId);
        }

        public static string GetUuidFromPlayerName(string playerName)
        {
            //Create the request
            var client = new RestClient("https://api.mojang.com/");
            var request = new RestRequest($"users/profiles/minecraft/{playerName}", Method.GET);

            //Get the response and Deserialize
            var response = client.Execute(request);

            if (response.Content == "")
            {
                return null;
            }

            dynamic responseDeserialized = JsonConvert.DeserializeObject(response.Content);

            //Mojang stores the uuid under id so return that
            return responseDeserialized.id;
        }

        internal async Task AddKey(string key, string party, string owner)
        {
            db.ApiKeys.Add(new ApiKey { Key = key, Party = party, Owner = owner });
            await db.SaveChangesAsync();
        }

        /// <summary>
        /// Downloads username for a given uuid from mojang.
        /// Will return null if rate limit reached.
        /// </summary>
        /// <param name="uuid"></param>
        /// <returns>The name or null if error occurs</returns>
        public async Task<string> GetPlayerNameFromUuid(string uuid)
        {
            if (DateTime.Now.Subtract(new TimeSpan(0, 10, 0)) < BlockedSince && RequestsSinceStart >= 2000)
            {
                //Console.Write("Blocked");
                // blocked
                return null;
            }
            else if (RequestsSinceStart >= 2000)
            {
                Console.Write("\tFreed 2000 ");
                RequestsSinceStart = 0;
            }

            //Create the request
            RestClient client = null;
            RestRequest request;
            int type = 0;

            if (RequestsSinceStart == 600)
            {
                BlockedSince = DateTime.Now;
            }

            if (RequestsSinceStart < 600)
            {
                client = new RestClient("https://api.mojang.com/");
                request = new RestRequest($"user/profiles/{uuid}/names", Method.GET);
            }
            else if (RequestsSinceStart < 1500)
            {
                client = new RestClient("https://mc-heads.net/");
                request = new RestRequest($"/minecraft/profile/{uuid}", Method.GET);
                type = 1;
            }
            else
            {
                client = new RestClient("https://minecraft-api.com/");
                request = new RestRequest($"/api/uuid/pseudo.php?uuid={uuid}", Method.GET);
                type = 2;
            }

            RequestsSinceStart++;

            //Get the response and Deserialize
            var response = await client.ExecuteAsync(request);

            if (response.Content == "")
            {
                return null;
            }

            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                // Shift out to another ip
                RequestsSinceStart += 1000;
                return null;
            }

            if (type == 2)
            {
                return response.Content;
            }

            dynamic responseDeserialized = JsonConvert.DeserializeObject(response.Content);

            if (responseDeserialized == null)
            {
                return null;
            }

            switch (type)
            {
                case 0:
                    return responseDeserialized[responseDeserialized.Count - 1]?.name;
                case 1:
                    return responseDeserialized.name;
            }

            return responseDeserialized.name;
        }
    }
}
