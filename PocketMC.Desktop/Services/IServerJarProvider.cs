using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Services
{
    public interface IServerJarProvider
    {
        string DisplayName { get; }
        
        Task<List<MinecraftVersion>> GetAvailableVersionsAsync();
        
        Task DownloadJarAsync(string mcVersion, string destinationPath, IProgress<DownloadProgress>? progress = null);
    }
}
