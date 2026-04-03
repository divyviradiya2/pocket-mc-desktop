using System;

namespace PocketMC.Desktop.Models
{
    public class AppSettings
    {
        public string? AppRootPath { get; set; }
        public string PlayitSecretKey { get; set; } = string.Empty;
    }
}
