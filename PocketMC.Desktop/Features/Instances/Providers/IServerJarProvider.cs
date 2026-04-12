using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Services;

namespace PocketMC.Desktop.Features.Instances.Providers;

public interface IServerJarProvider
{
    string DisplayName { get; }
    
    Task<List<MinecraftVersion>> GetAvailableVersionsAsync();
    
    Task DownloadJarAsync(string mcVersion, string destinationPath, IProgress<DownloadProgress>? progress = null);
}
