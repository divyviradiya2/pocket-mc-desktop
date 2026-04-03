using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Services
{
    public class PlayitService
    {
        private readonly HttpClient _httpClient;
        private readonly SettingsManager _settingsManager;

        public PlayitService(SettingsManager settingsManager)
        {
            _settingsManager = settingsManager;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PocketMC-Desktop");
        }

        public async Task<string> ClaimPlayitAccountAsync(string appRootPath)
        {
            string playitExePath = Path.Combine(appRootPath, "runtime", "playit", "playit.exe");
            if (!File.Exists(playitExePath)) return "https://playit.gg/login";
            
            var psi = new ProcessStartInfo
            {
                FileName = playitExePath,
                Arguments = "claim",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            try
            {
                var process = Process.Start(psi);
                if (process == null) return "https://playit.gg/login";

                // Playit claim runs and usually prints a URL to stdout/stderr. 
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                
                await Task.WhenAny(Task.Delay(5000), Task.WhenAll(outputTask, errorTask));
                
                string content = (outputTask.IsCompleted ? outputTask.Result : "") + 
                                 (errorTask.IsCompleted ? errorTask.Result : "");

                var match = Regex.Match(content, @"https:/\/playit\.gg\/claim\/[a-zA-Z0-9\-]+");
                if (match.Success)
                {
                    return match.Value;
                }
            }
            catch {}

            return "https://playit.gg/login";
        }

        public async Task<string> GetPublicAddressForPortAsync(int localPort)
        {
            var settings = _settingsManager.Load();
            if (string.IsNullOrEmpty(settings.PlayitSecretKey))
                return string.Empty;

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://api.playit.cloud/agent/tunnels");
                // The API format might differ; handling gracefully if it fails
                request.Headers.Add("Authorization", $"agent {settings.PlayitSecretKey}");

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var tunnels = JsonSerializer.Deserialize<PlayitTunnelsResponse>(content);
                    if (tunnels != null)
                    {
                        foreach (var t in tunnels.Tunnels)
                        {
                            if (t.LocalPort == localPort || (t.PortMapping != null && t.PortMapping.To == localPort))
                            {
                                return string.IsNullOrEmpty(t.CustomDomain) ? t.AssignedDomain : t.CustomDomain;
                            }
                        }
                    }
                }
            }
            catch {}

            return string.Empty;
        }
    }
}
