using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PocketMC.Desktop.Features.Java
{
    /// <summary>
    /// Client for the Adoptium (Eclipse Temurin) API to resolve Minecraft-compatible Java runtimes.
    /// </summary>
    public sealed class JavaAdoptiumClient
    {
        private const string DownloadClientName = "PocketMC.Downloads";
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<JavaAdoptiumClient> _logger;

        public JavaAdoptiumClient(IHttpClientFactory httpClientFactory, ILogger<JavaAdoptiumClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <summary>
        /// Resolves the latest JRE download URL for a specific Java version on Windows x64.
        /// </summary>
        public async Task<string> ResolveRuntimePackageUrlAsync(int version, CancellationToken cancellationToken)
        {
            string apiUrl = $"https://api.adoptium.net/v3/assets/latest/{version}/hotspot?os=windows&architecture=x64&image_type=jre";
            const int maxAttempts = 3;
            Exception? lastException = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using HttpClient client = _httpClientFactory.CreateClient(DownloadClientName);
                    string jsonResponse = await client.GetStringAsync(apiUrl, cancellationToken);

                    JsonArray? array = JsonNode.Parse(jsonResponse)?.AsArray();
                    string? link = array?[0]?["binary"]?["package"]?["link"]?.ToString();

                    if (string.IsNullOrWhiteSpace(link))
                    {
                        throw new InvalidOperationException($"Could not find a valid download link for Java {version}.");
                    }

                    return link;
                }
                catch (Exception ex) when (attempt < maxAttempts && IsRetryable(ex))
                {
                    lastException = ex;
                    _logger.LogWarning(ex, "Failed to resolve Java {Version} package metadata (attempt {Attempt}/{MaxAttempts}).", version, attempt, maxAttempts);
                    await Task.Delay(GetRetryDelay(attempt), cancellationToken);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    break;
                }
            }

            throw new InvalidOperationException($"Could not resolve package metadata for Java {version} from Adoptium API.", lastException);
        }

        public bool IsRetryable(Exception ex) =>
            ex is HttpRequestException or System.IO.IOException or TaskCanceledException;

        public TimeSpan GetRetryDelay(int attempt) => attempt switch
        {
            1 => TimeSpan.FromSeconds(2),
            2 => TimeSpan.FromSeconds(5),
            _ => TimeSpan.FromSeconds(10)
        };
    }
}
