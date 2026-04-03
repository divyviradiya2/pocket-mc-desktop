using System;
using System.Text.Json.Serialization;

namespace PocketMC.Desktop.Models
{
    public class PlayitTunnel
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("local_port")]
        public int? LocalPort { get; set; }

        [JsonPropertyName("custom_domain")]
        public string CustomDomain { get; set; } = string.Empty;

        [JsonPropertyName("assigned_domain")]
        public string AssignedDomain { get; set; } = string.Empty;
        
        [JsonPropertyName("port_type")]
        public string PortType { get; set; } = string.Empty;

        [JsonPropertyName("port_mapping")]
        public PlayitPortMapping? PortMapping { get; set; }
    }

    public class PlayitPortMapping
    {
        [JsonPropertyName("from")]
        public int From { get; set; }

        [JsonPropertyName("to")]
        public int To { get; set; }
    }

    public class PlayitTunnelsResponse
    {
        [JsonPropertyName("tunnels")]
        public PlayitTunnel[] Tunnels { get; set; } = Array.Empty<PlayitTunnel>();
    }
}
