using System.Threading.Tasks;
using System.Linq;
using Coflnet.Sky.Proxy.Models;
using Microsoft.EntityFrameworkCore;
using System;

namespace Coflnet.Sky.Proxy.Services;

public class KeyManager
{
    private ProxyDbContext db;
    private IIpRetriever ipRetriever;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="db"></param>
    /// <param name="ipRetriever"></param>
    public KeyManager(ProxyDbContext db, IIpRetriever ipRetriever)
    {
        this.db = db;
        this.ipRetriever = ipRetriever;
    }

    /// <summary>
    /// Returns an api key
    /// </summary>
    /// <param name="provider"></param>
    /// <param name="addUses">How many usages to add</param>
    /// <returns></returns>
    /// <exception cref="Coflnet.Sky.Core.CoflnetException"></exception>
    public async Task<string> GetKey(string provider, int addUses = 0)
    {
        var myIp = await ipRetriever.GetIp();
        var key = await db.ApiKeys.Where(a => a.Party == provider && a.IsValid && a.LastServerIp == myIp).FirstOrDefaultAsync();
        if (key != null)
            return key.Key;

        // get key not in use
        var maxTime = System.DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(20));
        key = await db.ApiKeys.Where(a => a.Party == provider && a.IsValid && (a.LastServerIp == null || a.LastServerIp != null && a.LastUsed < maxTime)).FirstOrDefaultAsync();
        if (key == null)
            throw new Coflnet.Sky.Core.CoflnetException("no_key", $"No key for {provider} is available for this server ({myIp})");
        if(addUses > 0)
            await StoreUse(addUses, key);
        return key?.Key;
    }

    /// <summary>
    /// Mark key as invalid
    /// </summary>
    /// <param name="provider"></param>
    /// <param name="key"></param>
    /// <returns></returns>
    public async Task InvalidateKey(string provider, string key)
    {
        var ApiKeys = await db.ApiKeys.Where(a => a.Party == provider && a.Key == key).ToListAsync();
        foreach (var apiKey in ApiKeys)
        {
            apiKey.IsValid = false;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="provider"></param>
    /// <param name="key"></param>
    /// <param name="times"></param>
    /// <returns></returns>
    public async Task UsedKey(string provider, string key, int times = 1)
    {
        var apiKey = await db.ApiKeys.Where(a => a.Party == provider && a.Key == key).FirstOrDefaultAsync();
        await StoreUse(times, apiKey);
    }

    private async Task StoreUse(int times, ApiKey apiKey)
    {
        if (apiKey != null)
        {
            apiKey.LastUsed = System.DateTime.UtcNow;
            apiKey.UseCount += times;
            apiKey.LastServerIp = await ipRetriever.GetIp();
            await db.SaveChangesAsync();
        }
    }
}
