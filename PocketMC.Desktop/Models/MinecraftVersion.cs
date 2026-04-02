using System;

namespace PocketMC.Desktop.Models
{
    public class MinecraftVersion
    {
        public string Id { get; set; } = string.Empty;
        
        /// <summary>
        /// e.g. "release" or "snapshot"
        /// </summary>
        public string Type { get; set; } = string.Empty;
        
        public DateTime ReleaseTime { get; set; }

        public override string ToString() => $"{Id} ({Type})";
    }
}
