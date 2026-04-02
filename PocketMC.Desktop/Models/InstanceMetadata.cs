using System;

namespace PocketMC.Desktop.Models
{
    public class InstanceMetadata
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int MinRamMb { get; set; } = 1024;
        public int MaxRamMb { get; set; } = 4096;
    }
}
