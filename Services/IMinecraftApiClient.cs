using System.Threading.Tasks;
using Coflnet.Sky.Proxy.Models;
using System.Collections.Generic;

namespace Coflnet.Sky.Proxy.Services;

public interface IMinecraftApiClient
{
    Task<NameResponse> GetNameByUuid(string uuid);
    Task<List<NameResponse>> LoadByNameBatch(List<PlayerNameUpdate> nameList);
}
