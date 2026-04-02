using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Services
{
    /// <summary>
    /// Manages the Playit.gg agent as a singleton background process.
    ///
    /// ARCHITECTURE:
    /// The playit CLI supports a --stdout flag that forces log output to stdout
    /// instead of the TUI. Combined with UseShellExecute=false and stream
    /// redirection, we can capture the agent's output in real-time.
    ///
    /// Key CLI flags used:
    ///   --stdout       Forces log output to stdout (disables TUI)
    ///   --secret_path  Points to the toml file containing the secret_key
    ///
    /// The agent automatically:
    ///   1. Creates one default tunnel on first claim
    ///   2. Opens browser for claiming if not yet claimed
    ///   3. Logs tunnel connection status to stdout
    /// </summary>
    public class PlayitCoordinator : IDisposable
    {
        // --- Events ---
        public event Action<string>? OnTunnelReady;
        public event Action<string>? OnLogLine;
        public event Action<PlayitTunnelState>? OnStateChanged;
        public event Action<string>? OnError;

        // --- State ---
        private Process? _playitProcess;
        private readonly JobObject _jobObject;
        private readonly string _playitExePath;
        private InstanceMetadata? _lastMeta;
        private string? _lastAppRootPath;
        private Thread? _stdoutThread;
        private CancellationTokenSource? _restartCts;
        private bool _intentionalStop;
        private bool _disposed;
        private int _consecutiveRestarts;
        private const int MaxAutoRestarts = 5;

        /// <summary>Default playit.toml location used by the agent.</summary>
        private static readonly string GlobalPlayitToml = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "playit_gg", "playit.toml");

        /// <summary>Number of tunnels detected from stdout parsing.</summary>
        public int DetectedTunnelCount { get; private set; }

        public PlayitTunnelState State { get; private set; } = PlayitTunnelState.Stopped;
        public string? PublicAddress { get; private set; }
        public ConcurrentQueue<string> LogBuffer { get; } = new();

        public PlayitCoordinator(string playitExePath, JobObject jobObject)
        {
            _playitExePath = playitExePath ?? throw new ArgumentNullException(nameof(playitExePath));
            _jobObject = jobObject ?? throw new ArgumentNullException(nameof(jobObject));
        }

        /// <summary>
        /// Checks whether the agent has been claimed (has a secret_key in playit.toml).
        /// </summary>
        public static bool IsAgentClaimed()
        {
            try
            {
                if (!File.Exists(GlobalPlayitToml)) return false;
                var content = File.ReadAllText(GlobalPlayitToml);
                return content.Contains("secret_key") && !content.Contains("secret_key = \"\"");
            }
            catch { return false; }
        }

        public bool Start(InstanceMetadata meta, string appRootPath)
        {
            if (_playitProcess != null && !_playitProcess.HasExited)
                return true;

            if (!File.Exists(_playitExePath))
            {
                var msg = $"Playit binary not found at: {_playitExePath}";
                EmitLog($"[PocketMC] {msg}");
                OnError?.Invoke(msg);
                SetState(PlayitTunnelState.Error);
                return false;
            }

            _intentionalStop = false;
            _lastMeta = meta;
            _lastAppRootPath = appRootPath;
            PublicAddress = null;
            DetectedTunnelCount = 0;

            SetState(PlayitTunnelState.Starting);

            try
            {
                // ═══════════════════════════════════════════════════════════
                //  Run playit.exe with --stdout to get real log output.
                //  UseShellExecute=false + RedirectStandardOutput=true
                //  lets us capture the agent's log lines in real-time.
                // ═══════════════════════════════════════════════════════════
                var startInfo = new ProcessStartInfo
                {
                    FileName = _playitExePath,
                    Arguments = "--stdout",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = false,
                    CreateNoWindow = true,
                };

                // Force clean output (no ANSI, no color)
                startInfo.EnvironmentVariables["NO_COLOR"] = "1";
                startInfo.EnvironmentVariables["TERM"] = "dumb";

                _playitProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                _playitProcess.Exited += OnProcessExited;

                _playitProcess.Start();

                // Register with Job Object for cleanup
                try { _jobObject.AddProcess(_playitProcess.Handle); }
                catch (Exception ex) { EmitLog($"[PocketMC] Warning: Job Object: {ex.Message}"); }

                EmitLog($"[PocketMC] Playit agent started (PID: {_playitProcess.Id})");

                // Start dedicated stdout reading thread
                _stdoutThread = new Thread(ReadStdout)
                {
                    Name = "PlayitStdoutReader",
                    IsBackground = true
                };
                _stdoutThread.Start(_playitProcess);

                return true;
            }
            catch (Exception ex)
            {
                var msg = $"Failed to start Playit process: {ex.Message}";
                EmitLog($"[PocketMC] {msg}");
                OnError?.Invoke(msg);
                SetState(PlayitTunnelState.Error);
                _playitProcess?.Dispose();
                _playitProcess = null;
                return false;
            }
        }

        /// <summary>
        /// Dedicated thread that reads stdout from the playit process.
        /// Uses blocking StreamReader.ReadLine() which is safe on a background thread.
        /// </summary>
        private void ReadStdout(object? state)
        {
            var proc = state as Process;
            if (proc == null) return;

            try
            {
                using var reader = proc.StandardOutput;
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (_disposed || _intentionalStop) break;
                    ProcessLogLine(line);
                }
            }
            catch (Exception ex)
            {
                if (!_disposed && !_intentionalStop)
                    EmitLog($"[PocketMC] Stdout read error: {ex.Message}");
            }
        }

        /// <summary>
        /// Parses a log line from the playit agent for meaningful state changes.
        ///
        /// Example lines:
        ///   "secret key valid, agent has 1 tunnels"
        ///   "tunnel running"
        ///   "checking if secret key is valid"
        ///   "starting up tunnel connection"
        /// </summary>
        private void ProcessLogLine(string line)
        {
            EmitLog(line);

            // Detect tunnel count: "agent has N tunnels"
            var tunnelCountMatch = Regex.Match(line, @"agent has (\d+) tunnel");
            if (tunnelCountMatch.Success && int.TryParse(tunnelCountMatch.Groups[1].Value, out var count))
            {
                DetectedTunnelCount = count;
                EmitLog($"[PocketMC] Detected {count} tunnel(s) on this agent.");
            }

            // Detect tunnel running = connected
            if (line.Contains("tunnel running"))
            {
                SetState(PlayitTunnelState.Connected);
                OnTunnelReady?.Invoke("connected");
            }

            // Detect errors
            if (line.Contains("ERROR") && (line.Contains("ApiError") || line.Contains("failed")))
            {
                // Log but don't mark as Error state for transient API errors
                // (e.g. TooManyRequests is a rate limit, agent will retry)
            }
        }

        private void EmitLog(string text)
        {
            LogBuffer.Enqueue(text);
            OnLogLine?.Invoke(text);
        }

        public void Stop()
        {
            _intentionalStop = true;
            _restartCts?.Cancel();

            if (_playitProcess != null && !_playitProcess.HasExited)
            {
                try { _playitProcess.Kill(entireProcessTree: true); }
                catch { }
            }

            EmitLog("[PocketMC] Playit agent stopped.");
            PublicAddress = null;
            SetState(PlayitTunnelState.Stopped);
        }

        private void OnProcessExited(object? sender, EventArgs e)
        {
            if (_intentionalStop || _disposed) return;

            SetState(PlayitTunnelState.Error);
            _consecutiveRestarts++;

            if (_consecutiveRestarts < MaxAutoRestarts && _lastMeta != null && _lastAppRootPath != null)
            {
                _ = AutoRestartAsync();
            }
            else
            {
                var msg = $"Playit agent crashed {_consecutiveRestarts} times — auto-restart disabled.";
                EmitLog($"[PocketMC] {msg}");
                OnError?.Invoke(msg);
            }
        }

        private async Task AutoRestartAsync()
        {
            _restartCts = new CancellationTokenSource();
            try
            {
                EmitLog("[PocketMC] Playit agent crashed. Restarting in 5 seconds...");
                await Task.Delay(5000, _restartCts.Token);

                if (!_restartCts.IsCancellationRequested && !_disposed && _lastMeta != null && _lastAppRootPath != null)
                {
                    _playitProcess?.Dispose();
                    _playitProcess = null;
                    Start(_lastMeta, _lastAppRootPath);
                }
            }
            catch (TaskCanceledException) { }
        }

        private void SetState(PlayitTunnelState newState)
        {
            State = newState;
            OnStateChanged?.Invoke(newState);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _playitProcess?.Dispose();
        }
    }

    public enum PlayitTunnelState
    {
        Stopped,
        Starting,
        Connected,
        Error
    }
}
