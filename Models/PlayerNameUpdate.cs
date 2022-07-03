namespace Coflnet.Sky.Proxy.Models
{
    /// <summary>
    /// TransferObject to inform about a name change
    /// </summary>
    public class PlayerNameUpdate
    {
        /// <summary>
        /// The visible name of the player
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// The unique id of the player
        /// </summary>
        public string Uuid { get; set; }
    }
}