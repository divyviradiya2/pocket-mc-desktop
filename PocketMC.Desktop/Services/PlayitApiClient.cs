using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace PocketMC.Desktop.Services
{
    /// <summary>
    /// Client for the Playit.gg REST API.
    /// Uses the agent's secret_key from playit.toml as authentication.
    ///
    /// Key endpoint: POST /agents/rundata
    ///   - Returns all tunnels with their public addresses, ports, and local mappings
    ///   - Auth: "Authorization: agent-key {secret_key}"
    /// </summary>
    public class PlayitApiClient
    {
        private const string ApiBaseUrl = "https://api.playit.gg";
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

        private static readonly string GlobalPlayitToml = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "playit_gg", "playit.toml");

        /// <summary>
        /// Reads the secret_key from the global playit.toml file.
        /// </summary>
        public static string? GetSecretKey()
        {
            try
            {
                if (!File.Exists(GlobalPlayitToml)) return null;
                var content = File.ReadAllText(GlobalPlayitToml);
                var match = System.Text.RegularExpressions.Regex.Match(content, @"secret_key\s*=\s*""([^""]+)""");
                return match.Success ? match.Groups[1].Value : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Fetches all tunnel information from the Playit API.
        /// Returns a list of tunnels with their public addresses and local port mappings.
        /// </summary>
        public static async Task<List<PlayitTunnelInfo>> GetTunnelsAsync()
        {
            var tunnels = new List<PlayitTunnelInfo>();

            var secretKey = GetSecretKey();
            if (string.IsNullOrEmpty(secretKey)) return tunnels;

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/agents/rundata");
                request.Headers.Add("Authorization", $"agent-key {secretKey}");
                request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

                var response = await _http.SendAsync(request);
                if (!response.IsSuccessStatusCode) return tunnels;

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("status", out var status) && status.GetString() != "success")
                    return tunnels;

                if (!root.TryGetProperty("data", out var data)) return tunnels;
                if (!data.TryGetProperty("tunnels", out var tunnelsArr)) return tunnels;

                foreach (var t in tunnelsArr.EnumerateArray())
                {
                    var info = new PlayitTunnelInfo();

                    if (t.TryGetProperty("id", out var id))
                        info.TunnelId = id.GetString() ?? "";

                    if (t.TryGetProperty("name", out var name))
                        info.Name = name.GetString() ?? "";

                    if (t.TryGetProperty("assigned_domain", out var domain))
                        info.AssignedDomain = domain.GetString() ?? "";

                    if (t.TryGetProperty("local_port", out var localPort))
                        info.LocalPort = localPort.GetInt32();

                    if (t.TryGetProperty("local_ip", out var localIp))
                        info.LocalIp = localIp.GetString() ?? "127.0.0.1";

                    if (t.TryGetProperty("proto", out var proto))
                        info.Protocol = proto.GetString() ?? "tcp";

                    if (t.TryGetProperty("tunnel_type", out var tunnelType))
                        info.TunnelType = tunnelType.GetString() ?? "";

                    if (t.TryGetProperty("port", out var port))
                    {
                        if (port.TryGetProperty("from", out var portFrom))
                            info.PublicPort = portFrom.GetInt32();
                    }

                    // Build the full public address: domain:port
                    if (!string.IsNullOrEmpty(info.AssignedDomain) && info.PublicPort > 0)
                    {
                        info.PublicAddress = $"{info.AssignedDomain}:{info.PublicPort}";
                    }

                    tunnels.Add(info);
                }
            }
            catch (Exception)
            {
                // API call failed — return empty list, caller handles gracefully
            }

            return tunnels;
        }

        /// <summary>
        /// Finds the tunnel that maps to a specific local port.
        /// Returns null if no tunnel exists for that port.
        /// </summary>
        public static async Task<PlayitTunnelInfo?> FindTunnelByLocalPortAsync(int localPort)
        {
            var tunnels = await GetTunnelsAsync();
            foreach (var t in tunnels)
            {
                if (t.LocalPort == localPort) return t;
            }
            return null;
        }

        /// <summary>
        /// Creates a new tunnel for the specified local port via the Playit API.
        /// </summary>
        public static async Task<PlayitTunnelInfo?> CreateTunnelAsync(string serverName, int localPort)
        {
            var secretKey = GetSecretKey();
            if (string.IsNullOrEmpty(secretKey)) throw new Exception("No secret key found. Playit agent not logged in.");

            var bodyJson = $$"""
            {
              "name": "{{serverName}}",
              "tunnel_type": "minecraft-java",
              "port_type": "tcp",
              "port_count": 1,
              "origin": {
                "type": "default",
                "data": {
                  "local_ip": "127.0.0.1",
                  "local_port": {{localPort}}
                }
              },
              "enabled": true
            }
            """;

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/tunnels/create");
                request.Headers.Add("Authorization", $"agent-key {secretKey}");
                request.Content = new StringContent(bodyJson, System.Text.Encoding.UTF8, "application/json");

                var response = await _http.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    // Wait briefly for tunnel to propagate on Playit's end
                    await Task.Delay(2000); 
                    return await FindTunnelByLocalPortAsync(localPort);
                }
                else
                {
                    var errorCtx = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Failed to create tunnel (HTTP {response.StatusCode}): {errorCtx}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"API request to Playit.gg failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Deletes a tunnel via the Playit API by its ID.
        /// </summary>
        public static async Task<bool> DeleteTunnelAsync(string tunnelId)
        {
            var secretKey = GetSecretKey();
            if (string.IsNullOrEmpty(secretKey)) throw new Exception("No secret key found. Playit agent not logged in.");

            var bodyJson = $$"""
            {
              "tunnel_id": "{{tunnelId}}"
            }
            """;

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/tunnels/delete");
                request.Headers.Add("Authorization", $"agent-key {secretKey}");
                request.Content = new StringContent(bodyJson, System.Text.Encoding.UTF8, "application/json");

                var response = await _http.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
                else
                {
                    var errorCtx = await response.Content.ReadAsStringAsync();
                    throw new Exception($"HTTP {response.StatusCode}: {errorCtx}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to delete tunnel: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Represents a single tunnel from the Playit.gg API.
    /// </summary>
    public class PlayitTunnelInfo
    {
        public string TunnelId { get; set; } = "";
        public string Name { get; set; } = "";
        public string AssignedDomain { get; set; } = "";
        public int PublicPort { get; set; }
        public int LocalPort { get; set; }
        public string LocalIp { get; set; } = "127.0.0.1";
        public string Protocol { get; set; } = "tcp";
        public string TunnelType { get; set; } = "";

        /// <summary>
        /// The full public address players use to connect (e.g., "free-raw.gl.joinmc.link:52293")
        /// </summary>
        public string? PublicAddress { get; set; }
    }
}
