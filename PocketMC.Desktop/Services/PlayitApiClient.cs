using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PocketMC.Desktop.Services
{
    /// <summary>
    /// Represents a single Playit.gg tunnel entry returned by the API.
    /// </summary>
    public class TunnelData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("port")]
        public int Port { get; set; }

        [JsonPropertyName("public_address")]
        public string PublicAddress { get; set; } = string.Empty;

        [JsonPropertyName("proto")]
        public string Protocol { get; set; } = "tcp";
    }

    /// <summary>
    /// Result of a tunnel list API call, including error state.
    /// </summary>
    public class TunnelListResult
    {
        public bool Success { get; set; }
        public List<TunnelData> Tunnels { get; set; } = new();
        public string? ErrorMessage { get; set; }
        public bool IsTokenInvalid { get; set; }
    }

    /// <summary>
    /// HTTP client for the Playit.gg tunnel API.
    /// Reads the agent secret from %APPDATA%/playit/playit.toml.
    /// Implements NET-05, NET-06, NET-09.
    /// </summary>
    public class PlayitApiClient
    {
        private readonly HttpClient _httpClient;
        private static readonly Regex SecretRegex = new(
            @"secret_key\s*=\s*""([^""]+)""",
            RegexOptions.Compiled);

        private const string TunnelApiUrl = "https://api.playit.gg/account/tunnels";

        public PlayitApiClient(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PocketMC-Desktop");
        }

        /// <summary>
        /// Reads the agent secret key from %APPDATA%/playit/playit.toml (NET-05).
        /// Returns null if not found (agent never claimed).
        /// </summary>
        public string? GetSecretKey()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string tomlPath = Path.Combine(appData, "playit", "playit.toml");

            if (!File.Exists(tomlPath))
                return null;

            try
            {
                string content = File.ReadAllText(tomlPath);
                var match = SecretRegex.Match(content);
                return match.Success ? match.Groups[1].Value : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Fetches the list of tunnels from the Playit.gg API (NET-06).
        /// Tunnel addresses are always resolved fresh — never cached (NET-09).
        /// </summary>
        public async Task<TunnelListResult> GetTunnelsAsync()
        {
            string? secretKey = GetSecretKey();
            if (string.IsNullOrEmpty(secretKey))
            {
                return new TunnelListResult
                {
                    Success = false,
                    ErrorMessage = "No agent secret key found. Agent may not be claimed yet.",
                    IsTokenInvalid = true
                };
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, TunnelApiUrl);
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Secret", secretKey);

                using var response = await _httpClient.SendAsync(request);

                // Detect token revocation (D-02 from CONTEXT.md)
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    return new TunnelListResult
                    {
                        Success = false,
                        ErrorMessage = "Playit.gg token is invalid or revoked. Please re-link your account.",
                        IsTokenInvalid = true
                    };
                }

                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync();
                var tunnels = JsonSerializer.Deserialize<List<TunnelData>>(json)
                              ?? new List<TunnelData>();

                return new TunnelListResult
                {
                    Success = true,
                    Tunnels = tunnels
                };
            }
            catch (HttpRequestException ex)
            {
                return new TunnelListResult
                {
                    Success = false,
                    ErrorMessage = $"Could not verify tunnel status. Check your connection. ({ex.Message})"
                };
            }
            catch (Exception ex)
            {
                return new TunnelListResult
                {
                    Success = false,
                    ErrorMessage = $"Unexpected error resolving tunnels: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Finds the tunnel entry whose port matches the given server port.
        /// Returns null if no match found.
        /// </summary>
        public static TunnelData? FindTunnelForPort(List<TunnelData> tunnels, int serverPort)
        {
            return tunnels.Find(t => t.Port == serverPort);
        }
    }
}
