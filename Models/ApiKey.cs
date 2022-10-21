
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace Coflnet.Sky.Proxy.Models
{
    [DataContract]
    public class ApiKey
    {
        [IgnoreDataMember]
        [JsonIgnore]
        public int Id { get; set; }
        [MaxLength(20)]
        public string Party { get; set; }
        [MaxLength(40)]
        public string Key { get; set; }
        [MaxLength(40)]
        public string Owner { get; set; }
        [MaxLength(40)]
        public string LastServerIp { get; set; }
        public int UseCount { get; set; }
        public bool IsValid { get; set; } = true;
        public DateTime LastUsed { get; set; }  = DateTime.UtcNow;
        public DateTime Created { get; set; } = DateTime.UtcNow;
    }
}