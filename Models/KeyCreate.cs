namespace Coflnet.Sky.Proxy.Models
{
    public class KeyCreate
    {
        public string Party { get; set; }
        public string Key { get; set; }
        public string Owner { get; set; }
        /// <summary>
        /// By how many servers can this key be used (if higher rate limit)
        /// </summary>
        public int ServerCount { get; set; }
    }
}