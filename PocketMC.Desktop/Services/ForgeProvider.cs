using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Services
{
    public class ForgeProvider : IServerJarProvider
    {
        private readonly HttpClient _httpClient;
        private readonly DownloaderService _downloader;

        public string DisplayName => "Forge";

        public ForgeProvider(HttpClient httpClient, DownloaderService downloader)
        {
            _httpClient = httpClient;
            _downloader = downloader;
        }

        public async Task<List<MinecraftVersion>> GetAvailableVersionsAsync()
        {
            // Fetch the official Forge versions JSON (slim promotions)
            var response = await _httpClient.GetFromJsonAsync<JsonObject>("https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json");
            var versions = new List<MinecraftVersion>();
            
            if (response != null && response.TryGetPropertyValue("promos", out var promosNode) && promosNode is JsonObject promos)
            {
                // We extract unique MC versions from the keys
                var mcVersions = promos
                    .Select(p => p.Key.Split('-')[0])
                    .Distinct()
                    .Where(v => v.StartsWith("1.")); // Filter out junk like "26.1"
                
                foreach (var mcVersion in mcVersions)
                {
                    versions.Add(new MinecraftVersion
                    {
                        Id = mcVersion,
                        Type = "release",
                        ReleaseTime = DateTime.MinValue
                    });
                }
            }
            
            // Numerical sort by segments
            return versions
                .OrderByDescending(v => {
                    var parts = v.Id.Split('.');
                    long total = 0;
                    for(int i = 0; i < Math.Min(parts.Length, 3); i++) {
                        if (long.TryParse(parts[i], out var p))
                            total += p * (long)Math.Pow(1000, 2 - i);
                    }
                    return total;
                })
                .ToList();
        }

        public async Task DownloadJarAsync(string mcVersion, string destinationPath, IProgress<DownloadProgress>? progress = null)
        {
            // 1. Get the latest recommended/latest Forge version for this MC version
            string forgeVersion = await GetLatestForgeVersionAsync(mcVersion);
            
            // 2. Build the download URL for the installer
            // Official: https://maven.minecraftforge.net/net/minecraftforge/forge/1.20.1-47.2.20/forge-1.20.1-47.2.20-installer.jar
            string url = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{mcVersion}-{forgeVersion}/forge-{mcVersion}-{forgeVersion}-installer.jar";
            
            // NOTE: Forge installers need to be RUN to generate the server. 
            // For now, we download the installer. The instance launch logic will need to handle the "installation" step.
            await _downloader.DownloadFileAsync(url, destinationPath, progress);
        }

        private async Task<string> GetLatestForgeVersionAsync(string mcVersion)
        {
            var response = await _httpClient.GetFromJsonAsync<JsonObject>("https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json");
            if (response != null && response.TryGetPropertyValue("promos", out var promosNode) && promosNode is JsonObject promos)
            {
                // Format: "1.20.1-recommended": "47.2.0"
                if (promos.TryGetPropertyValue($"{mcVersion}-recommended", out var rec))
                    return rec?.ToString() ?? "0";
                
                if (promos.TryGetPropertyValue($"{mcVersion}-latest", out var lat))
                    return lat?.ToString() ?? "0";
            }
            
            return "latest";
        }
    }
}
