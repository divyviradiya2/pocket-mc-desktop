using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace PocketMC.Desktop.Services
{
    /// <summary>
    /// Service responsible for ensuring the correct Java runtimes are present and functional.
    /// Handles automated downloads, extraction, validation, retry logic, and shared progress state.
    /// </summary>
    public class JavaProvisioningService
    {
        private const string DownloadClientName = "PocketMC.Downloads";
        private readonly DownloaderService _downloader;
        private readonly ApplicationState _applicationState;
        private readonly ILogger<JavaProvisioningService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ConcurrentDictionary<int, Task> _inflightProvisioning = new();
        private readonly ConcurrentDictionary<int, JavaProvisioningStatus> _statuses = new();
        private readonly ConcurrentDictionary<int, DateTimeOffset> _automaticRetryBlockedUntil = new();
        private readonly object _backgroundProvisioningLock = new();
        private Task? _backgroundProvisioningTask;

        public event Action<JavaProvisioningStatus>? OnProvisioningStatusChanged;

        public JavaProvisioningService(
            DownloaderService downloader,
            ApplicationState applicationState,
            ILogger<JavaProvisioningService> logger,
            IHttpClientFactory httpClientFactory)
        {
            _downloader = downloader;
            _applicationState = applicationState;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// Checks if a specific Java version is already provisioned and functional enough to be used.
        /// </summary>
        public bool IsJavaVersionPresent(int version)
        {
            string appRoot = _applicationState.GetRequiredAppRootPath();
            string exePath = Path.Combine(appRoot, "runtime", $"java{version}", "bin", "java.exe");
            return File.Exists(exePath) && new FileInfo(exePath).Length > 1024 * 10;
        }

        public IReadOnlyList<JavaProvisioningStatus> GetStatuses()
        {
            return JavaRuntimeResolver.GetBundledJavaVersions()
                .Select(GetStatus)
                .OrderBy(status => status.Version)
                .ToList();
        }

        public JavaProvisioningStatus GetStatus(int version)
        {
            if (_statuses.TryGetValue(version, out var status))
            {
                return status;
            }

            return CreateDefaultStatus(version);
        }

        public Task EnsureBundledRuntimesAsync(CancellationToken cancellationToken = default)
        {
            return EnsureVersionsAsync(JavaRuntimeResolver.GetBundledJavaVersions(), ignoreAutomaticRetryCooldown: true, cancellationToken);
        }

        public void StartBackgroundProvisioning()
        {
            if (!_applicationState.IsConfigured)
            {
                return;
            }

            lock (_backgroundProvisioningLock)
            {
                if (_backgroundProvisioningTask is { IsCompleted: false })
                {
                    return;
                }

                if (JavaRuntimeResolver.GetBundledJavaVersions().All(IsJavaVersionPresent))
                {
                    return;
                }

                _backgroundProvisioningTask = Task.Run(async () =>
                {
                    try
                    {
                        await EnsureVersionsAsync(JavaRuntimeResolver.GetBundledJavaVersions(), ignoreAutomaticRetryCooldown: false, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Background Java runtime provisioning did not complete successfully.");
                    }
                });
            }
        }

        /// <summary>
        /// Ensures a specific Java version is present, downloading it if necessary.
        /// Concurrent callers for the same version will share the same provisioning task.
        /// </summary>
        public async Task EnsureJavaAsync(int version, CancellationToken cancellationToken = default)
        {
            if (IsJavaVersionPresent(version))
            {
                PublishStatus(version, JavaProvisioningStage.Ready, "Runtime is installed and ready.", 100, isInstalled: true);
                return;
            }

            Task provisioningTask = _inflightProvisioning.GetOrAdd(
                version,
                static (runtimeVersion, state) => state.self.ProvisionRuntimeCoreAsync(runtimeVersion, state.cancellationToken),
                (self: this, cancellationToken));

            try
            {
                await provisioningTask;
            }
            finally
            {
                if (provisioningTask.IsCompleted)
                {
                    _inflightProvisioning.TryRemove(new KeyValuePair<int, Task>(version, provisioningTask));
                }
            }
        }

        private async Task EnsureVersionsAsync(IEnumerable<int> versions, bool ignoreAutomaticRetryCooldown, CancellationToken cancellationToken)
        {
            foreach (int version in versions.Distinct().OrderBy(v => v))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!ignoreAutomaticRetryCooldown && ShouldSkipAutomaticRetry(version, out var blockedUntil))
                {
                    PublishAutomaticRetryDeferredStatus(version, blockedUntil);
                    continue;
                }

                await EnsureJavaAsync(version, cancellationToken);
            }
        }

        private async Task ProvisionRuntimeCoreAsync(int version, CancellationToken cancellationToken)
        {
            if (IsJavaVersionPresent(version))
            {
                PublishStatus(version, JavaProvisioningStage.Ready, "Runtime is already installed.", 100, isInstalled: true);
                return;
            }

            string appRootPath = _applicationState.GetRequiredAppRootPath();
            string runtimeDir = Path.Combine(appRootPath, "runtime");
            string tempZipPath = Path.Combine(runtimeDir, $"temp_java{version}.zip");
            string extractPath = Path.Combine(runtimeDir, $"java{version}_ext");
            string finalPath = Path.Combine(runtimeDir, $"java{version}");

            Directory.CreateDirectory(runtimeDir);

            const int maxAttempts = 4;
            Exception? lastException = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PublishStatus(version, JavaProvisioningStage.Queued, $"Preparing Java {version} download (attempt {attempt}/{maxAttempts})...", 0, attempt: attempt, maxAttempts: maxAttempts);

                try
                {
                    CleanupProvisioningPaths(tempZipPath, extractPath, finalPath, keepFinalIfValid: false);

                    PublishStatus(version, JavaProvisioningStage.ResolvingPackage, "Resolving package metadata...", 0, attempt: attempt, maxAttempts: maxAttempts);
                    string packageUrl = await ResolveRuntimePackageUrlAsync(version, cancellationToken);

                    var progress = new Progress<DownloadProgress>(download =>
                    {
                        double percentage = download.TotalBytes > 0 ? download.Percentage : 0;
                        string message = download.TotalBytes > 0
                            ? $"Downloading... {FormatSize(download.BytesRead)} / {FormatSize(download.TotalBytes)}"
                            : $"Downloading... {FormatSize(download.BytesRead)}";

                        PublishStatus(version, JavaProvisioningStage.Downloading, message, percentage, attempt: attempt, maxAttempts: maxAttempts);
                    });

                    await _downloader.DownloadFileAsync(packageUrl, tempZipPath, progress, cancellationToken);

                    PublishStatus(version, JavaProvisioningStage.Extracting, "Extracting runtime archive...", 100, attempt: attempt, maxAttempts: maxAttempts);
                    await _downloader.ExtractZipAsync(tempZipPath, extractPath, new Progress<DownloadProgress>(extract =>
                    {
                        double percentage = extract.TotalBytes > 0 ? extract.Percentage : 100;
                        PublishStatus(version, JavaProvisioningStage.Extracting, "Extracting runtime archive...", percentage, attempt: attempt, maxAttempts: maxAttempts);
                    }));

                    string extractedRoot = ResolveExtractedRuntimeRoot(version, extractPath);

                    PublishStatus(version, JavaProvisioningStage.Verifying, "Validating runtime installation...", 100, attempt: attempt, maxAttempts: maxAttempts);
                    TryDeleteDirectory(finalPath);
                    Directory.Move(extractedRoot, finalPath);
                    CleanupProvisioningPaths(tempZipPath, extractPath, finalPath, keepFinalIfValid: true);

                    await ValidateProvisionedRuntimeAsync(finalPath, cancellationToken);

                    _automaticRetryBlockedUntil.TryRemove(version, out _);
                    PublishStatus(version, JavaProvisioningStage.Ready, $"Java {version} installed successfully.", 100, isInstalled: true, attempt: attempt, maxAttempts: maxAttempts);
                    return;
                }
                catch (Exception ex) when (attempt < maxAttempts && IsRetryable(ex))
                {
                    lastException = ex;
                    _logger.LogWarning(ex, "Provisioning Java {Version} failed on attempt {Attempt}/{MaxAttempts}. Retrying...", version, attempt, maxAttempts);
                    PublishStatus(version, JavaProvisioningStage.Failed, $"Retrying after network/install issue: {ex.Message}", 0, attempt: attempt, maxAttempts: maxAttempts);
                    CleanupProvisioningPaths(tempZipPath, extractPath, finalPath, keepFinalIfValid: false);
                    await Task.Delay(GetRetryDelay(attempt), cancellationToken);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    CleanupProvisioningPaths(tempZipPath, extractPath, finalPath, keepFinalIfValid: false);
                    break;
                }
            }

            string userMessage = $"Failed to download Java {version}. PocketMC retried automatically, but the runtime could not be provisioned.";
            _automaticRetryBlockedUntil[version] = DateTimeOffset.UtcNow.AddMinutes(10);
            PublishStatus(version, JavaProvisioningStage.Failed, userMessage, 0, maxAttempts: maxAttempts);
            _logger.LogError(lastException, "Failed to provision Java {Version}.", version);
            throw new InvalidOperationException(userMessage, lastException);
        }

        private bool ShouldSkipAutomaticRetry(int version, out DateTimeOffset blockedUntil)
        {
            if (_automaticRetryBlockedUntil.TryGetValue(version, out blockedUntil) && blockedUntil > DateTimeOffset.UtcNow && !IsJavaVersionPresent(version))
            {
                return true;
            }

            blockedUntil = default;
            return false;
        }

        private void PublishAutomaticRetryDeferredStatus(int version, DateTimeOffset blockedUntil)
        {
            TimeSpan remaining = blockedUntil - DateTimeOffset.UtcNow;
            int minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
            PublishStatus(
                version,
                JavaProvisioningStage.Failed,
                $"PocketMC paused automatic retries for Java {version} after repeated failures. Automatic retry resumes in about {minutes} minute(s), or click Download Missing to retry now.",
                0,
                isInstalled: false);
        }

        private async Task<string> ResolveRuntimePackageUrlAsync(int version, CancellationToken cancellationToken)
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
                    _logger.LogWarning(ex, "Failed to resolve Java {Version} package metadata on attempt {Attempt}/{MaxAttempts}. Retrying...", version, attempt, maxAttempts);
                    await Task.Delay(GetRetryDelay(attempt), cancellationToken);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    break;
                }
            }

            throw new InvalidOperationException($"Could not resolve package metadata for Java {version}.", lastException);
        }

        private static string ResolveExtractedRuntimeRoot(int version, string extractPath)
        {
            string[] subDirs = Directory.GetDirectories(extractPath);
            if (subDirs.Length != 1)
            {
                throw new InvalidOperationException($"Unexpected ZIP structure for Java {version}. Expected exactly one root directory.");
            }

            return subDirs[0];
        }

        private static async Task ValidateProvisionedRuntimeAsync(string finalPath, CancellationToken cancellationToken)
        {
            string javaExePath = Path.Combine(finalPath, "bin", "java.exe");
            if (!File.Exists(javaExePath))
            {
                throw new FileNotFoundException("java.exe was not found after extraction.", javaExePath);
            }

            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = javaExePath,
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            if (!process.Start())
            {
                throw new InvalidOperationException("java.exe could not be started after installation.");
            }

            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await process.WaitForExitAsync(timeout.Token);

            if (process.ExitCode != 0)
            {
                string stdErr = await process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"java.exe validation failed with exit code {process.ExitCode}: {stdErr}");
            }
        }

        private JavaProvisioningStatus CreateDefaultStatus(int version)
        {
            bool installed = IsJavaVersionPresent(version);
            return new JavaProvisioningStatus
            {
                Version = version,
                Stage = installed ? JavaProvisioningStage.Ready : JavaProvisioningStage.Idle,
                Message = installed ? "Runtime is installed and ready." : "Runtime is missing and will be downloaded automatically.",
                ProgressPercentage = installed ? 100 : 0,
                IsInstalled = installed
            };
        }

        private void PublishStatus(int version, JavaProvisioningStage stage, string message, double percentage, bool? isInstalled = null, int attempt = 0, int maxAttempts = 0)
        {
            JavaProvisioningStatus status = new()
            {
                Version = version,
                Stage = stage,
                Message = message,
                ProgressPercentage = Math.Clamp(percentage, 0, 100),
                IsInstalled = isInstalled ?? IsJavaVersionPresent(version),
                Attempt = attempt,
                MaxAttempts = maxAttempts,
                UpdatedAtUtc = DateTime.UtcNow
            };

            _statuses[version] = status;
            OnProvisioningStatusChanged?.Invoke(status);
        }

        private static void CleanupProvisioningPaths(string tempZipPath, string extractPath, string finalPath, bool keepFinalIfValid)
        {
            TryDeleteFile(tempZipPath);
            TryDeleteDirectory(extractPath);

            if (!keepFinalIfValid)
            {
                TryDeleteDirectory(finalPath);
            }
        }

        private static bool IsRetryable(Exception ex) =>
            ex is HttpRequestException
            or IOException
            or TaskCanceledException
            or InvalidDataException;

        private static TimeSpan GetRetryDelay(int attempt) => attempt switch
        {
            1 => TimeSpan.FromSeconds(2),
            2 => TimeSpan.FromSeconds(5),
            3 => TimeSpan.FromSeconds(10),
            _ => TimeSpan.FromSeconds(15)
        };

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best effort cleanup only.
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup only.
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024 * 1024)
            {
                return $"{bytes / 1024.0:F1} KB";
            }

            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }
    }
}
