using System;
using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Utils;

namespace PocketMC.Desktop.Services
{
    /// <summary>
    /// Service responsible for ensuring the correct Java runtimes are present and functional.
    /// Handles automated downloads, extraction, and integrity checks.
    /// </summary>
    public class JavaProvisioningService
    {
        private readonly DownloaderService _downloader;
        private readonly ApplicationState _applicationState;
        private readonly ILogger<JavaProvisioningService> _logger;
        private readonly HttpClient _httpClient;

        public JavaProvisioningService(
            DownloaderService downloader,
            ApplicationState applicationState,
            ILogger<JavaProvisioningService> logger)
        {
            _downloader = downloader;
            _applicationState = applicationState;
            _logger = logger;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PocketMC-Desktop");
        }

        /// <summary>
        /// Checks if a specific Java version is already provisioned and functional.
        /// </summary>
        public bool IsJavaVersionPresent(int version)
        {
            string appRoot = _applicationState.GetRequiredAppRootPath();
            string exePath = Path.Combine(appRoot, "runtime", $"java{version}", "bin", "java.exe");
            
            // Perform basic integrity check (file exists and is not empty/corrupt)
            return File.Exists(exePath) && new FileInfo(exePath).Length > 1024 * 10;
        }

        /// <summary>
        /// Ensures a specific Java version is present, downloading it if necessary.
        /// </summary>
        public async Task EnsureJavaAsync(int version, IProgress<DownloadProgress>? progress = null)
        {
            if (IsJavaVersionPresent(version))
            {
                _logger.LogInformation("Java {Version} is already present and valid.", version);
                return;
            }

            _logger.LogInformation("Provisioning Java {Version}...", version);
            
            try
            {
                string apiUrl = $"https://api.adoptium.net/v3/assets/latest/{version}/hotspot?os=windows&architecture=x64&image_type=jre";
                string jsonResponse = await _httpClient.GetStringAsync(apiUrl);
                
                var array = JsonNode.Parse(jsonResponse)?.AsArray();
                var link = array?[0]?["binary"]?["package"]?["link"]?.ToString();

                if (string.IsNullOrEmpty(link))
                {
                    throw new Exception($"Could not find a valid download link for Java {version} from Adoptium API.");
                }

                string appRootPath = _applicationState.GetRequiredAppRootPath();
                string runtimeDir = Path.Combine(appRootPath, "runtime");
                string tempZipPath = Path.Combine(runtimeDir, $"temp_java{version}.zip");
                string extractPath = Path.Combine(runtimeDir, $"java{version}_ext");
                string finalPath = Path.Combine(runtimeDir, $"java{version}");

                Directory.CreateDirectory(runtimeDir);

                // 1. Download
                await _downloader.DownloadFileAsync(link, tempZipPath, progress);

                // 2. Extract to temp folder
                if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
                await _downloader.ExtractZipAsync(tempZipPath, extractPath, progress);

                // 3. Move contents from Adoptium root folder to final destination
                var subDirs = Directory.GetDirectories(extractPath);
                if (subDirs.Length != 1)
                {
                    throw new Exception($"Unexpected ZIP structure for Java {version}. Expected exactly one internal directory.");
                }

                if (Directory.Exists(finalPath)) Directory.Delete(finalPath, true);
                Directory.Move(subDirs[0], finalPath);

                // 4. Cleanup
                if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
                if (File.Exists(tempZipPath)) File.Delete(tempZipPath);

                _logger.LogInformation("Successfully provisioned Java {Version} to {FinalPath}", version, finalPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to provision Java {Version}.", version);
                throw new Exception($"Failed to download or install Java {version}. Please check your internet connection.", ex);
            }
        }
    }
}
