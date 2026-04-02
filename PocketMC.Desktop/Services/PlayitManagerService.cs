using System;
using System.IO;
using System.Threading.Tasks;

namespace PocketMC.Desktop.Services
{
    /// <summary>
    /// Manages the Playit.gg CLI binary lifecycle — ensures the binary is downloaded
    /// and available at <APP_ROOT>/runtime/playit/playit.exe.
    /// </summary>
    public class PlayitManagerService
    {
        /// <summary>
        /// Pinned version of the Playit CLI. Update this when a new release is validated.
        /// </summary>
        private const string PlayitVersion = "0.15.26";

        /// <summary>
        /// Download URL for the pinned Windows x64 Playit CLI binary.
        /// </summary>
        private static readonly string DownloadUrl =
            $"https://github.com/playit-cloud/playit-agent/releases/download/v1.0.0-rc9/playit-windows-x86_64.exe";

        private readonly string _appRootPath;
        private readonly DownloaderService _downloader;

        public PlayitManagerService(string appRootPath)
        {
            _appRootPath = appRootPath;
            _downloader = new DownloaderService();
        }

        /// <summary>
        /// Returns the expected path to the playit.exe binary.
        /// </summary>
        public string PlayitExePath => Path.Combine(_appRootPath, "runtime", "playit", "playit.exe");

        /// <summary>
        /// Returns true if the playit binary is already present on disk.
        /// </summary>
        public bool IsInstalled => File.Exists(PlayitExePath);

        /// <summary>
        /// The pinned version string for display and version-mismatch warnings.
        /// </summary>
        public string PinnedVersion => PlayitVersion;

        /// <summary>
        /// Ensures playit.exe is available. Downloads it if missing.
        /// Called on application startup alongside Java runtime checks.
        /// </summary>
        public async Task EnsureInstalledAsync(IProgress<DownloadProgress>? progress = null)
        {
            if (IsInstalled)
                return;

            var dir = Path.GetDirectoryName(PlayitExePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            await _downloader.DownloadFileAsync(DownloadUrl, PlayitExePath, progress);
        }
    }
}
