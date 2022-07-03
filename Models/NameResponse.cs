namespace Coflnet.Sky.Proxy.Models
{
    /// <summary>
    /// Name response from mojang api
    /// </summary>
    public class NameResponse
    {
        /// <summary>
        /// The account name of the player
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// The uuid of the player
        /// </summary>
        public string Id { get; set; }
    }
}