using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Services
{
    public class VanillaProvider : IServerJarProvider
    {
        private readonly HttpClient _httpClient;
        private readonly string _cachePath;
        private readonly DownloaderService _downloader;

        public string DisplayName => "Vanilla (Mojang)";

        public VanillaProvider(string appRootPath)
        {
            _httpClient = new HttpClient();
            _cachePath = Path.Combine(appRootPath, "manifest-cache.json");
            _downloader = new DownloaderService();
        }

        public async Task<List<MinecraftVersion>> GetAvailableVersionsAsync()
        {
            string manifestJson;

            // Check cache (1 hour expiry)
            if (File.Exists(_cachePath) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(_cachePath)).TotalHours < 1)
            {
                manifestJson = await File.ReadAllTextAsync(_cachePath);
            }
            else
            {
                manifestJson = await _httpClient.GetStringAsync("https://launchermeta.mojang.com/mc/game/version_manifest_v2.json");
                // Save to cache
                Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
                await File.WriteAllTextAsync(_cachePath, manifestJson);
            }

            var root = JsonNode.Parse(manifestJson);
            var versionsArray = root?["versions"]?.AsArray();

            var versions = new List<MinecraftVersion>();
            if (versionsArray != null)
            {
                foreach (var v in versionsArray)
                {
                    if (v == null) continue;
                    
                    versions.Add(new MinecraftVersion
                    {
                        Id = v["id"]?.ToString() ?? "",
                        Type = v["type"]?.ToString() ?? "",
                        ReleaseTime = DateTime.TryParse(v["releaseTime"]?.ToString(), out var dt) ? dt : DateTime.MinValue
                    });
                }
            }

            return versions;
        }

        public async Task DownloadJarAsync(string mcVersion, string destinationPath, IProgress<DownloadProgress>? progress = null)
        {
            string manifestJson;
            if (File.Exists(_cachePath))
                manifestJson = await File.ReadAllTextAsync(_cachePath);
            else
                manifestJson = await _httpClient.GetStringAsync("https://launchermeta.mojang.com/mc/game/version_manifest_v2.json");

            var root = JsonNode.Parse(manifestJson);
            var versionsArray = root?["versions"]?.AsArray();
            var targetVersionNode = versionsArray?.FirstOrDefault(v => v?["id"]?.ToString() == mcVersion);

            if (targetVersionNode == null)
                throw new Exception($"Version {mcVersion} not found in Mojang manifest.");

            string versionUrl = targetVersionNode["url"]?.ToString() ?? "";
            string versionJson = await _httpClient.GetStringAsync(versionUrl);
            
            var versionRoot = JsonNode.Parse(versionJson);
            string serverUrl = versionRoot?["downloads"]?["server"]?["url"]?.ToString() ?? "";

            if (string.IsNullOrEmpty(serverUrl))
                throw new Exception($"Vanilla server jar not available for {mcVersion}.");

            await _downloader.DownloadFileAsync(serverUrl, destinationPath, progress);
        }
    }
}
