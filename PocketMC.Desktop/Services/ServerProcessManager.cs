using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Services
{
    /// <summary>
    /// Global singleton managing all active Minecraft server processes.
    /// Thread-safe via ConcurrentDictionary.
    /// </summary>
    public static class ServerProcessManager
    {
        private static readonly JobObject _jobObject = new();
        private static readonly ConcurrentDictionary<Guid, ServerProcess> _activeProcesses = new();

        /// <summary>
        /// Fires when any instance changes state (started, stopped, crashed).
        /// </summary>
        public static event Action<Guid, ServerState>? OnInstanceStateChanged;

        /// <summary>
        /// Gets the collection of currently active server processes.
        /// </summary>
        public static ConcurrentDictionary<Guid, ServerProcess> ActiveProcesses => _activeProcesses;

        /// <summary>
        /// Starts a server process for the given instance.
        /// Throws if already running, or if java/server.jar missing.
        /// </summary>
        public static ServerProcess StartProcess(InstanceMetadata meta, string appRootPath)
        {
            if (_activeProcesses.ContainsKey(meta.Id))
                throw new InvalidOperationException($"Server '{meta.Name}' is already running.");

            var serverProcess = new ServerProcess(meta.Id, _jobObject);

            serverProcess.OnStateChanged += (state) =>
            {
                OnInstanceStateChanged?.Invoke(meta.Id, state);

                // Remove from dictionary when stopped or crashed
                if (state == ServerState.Stopped || state == ServerState.Crashed)
                {
                    _activeProcesses.TryRemove(meta.Id, out _);
                }
            };

            serverProcess.Start(meta, appRootPath);
            _activeProcesses[meta.Id] = serverProcess;

            return serverProcess;
        }

        /// <summary>
        /// Gracefully stops a running server by sending /stop and waiting.
        /// </summary>
        public static async Task StopProcessAsync(Guid instanceId)
        {
            if (_activeProcesses.TryGetValue(instanceId, out var process))
            {
                await process.StopAsync();
            }
        }

        /// <summary>
        /// Force kills a running server immediately.
        /// </summary>
        public static void KillProcess(Guid instanceId)
        {
            if (_activeProcesses.TryGetValue(instanceId, out var process))
            {
                process.Kill();
            }
        }

        /// <summary>
        /// Returns whether a specific instance is currently running.
        /// </summary>
        public static bool IsRunning(Guid instanceId)
        {
            return _activeProcesses.ContainsKey(instanceId) &&
                   _activeProcesses[instanceId].State != ServerState.Stopped &&
                   _activeProcesses[instanceId].State != ServerState.Crashed;
        }

        /// <summary>
        /// Gets the ServerProcess for a running instance, or null. 
        /// </summary>
        public static ServerProcess? GetProcess(Guid instanceId)
        {
            _activeProcesses.TryGetValue(instanceId, out var process);
            return process;
        }

        /// <summary>
        /// Kills all running processes. Called on application shutdown.
        /// </summary>
        public static void KillAll()
        {
            foreach (var kvp in _activeProcesses)
            {
                try { kvp.Value.Kill(); } catch { }
            }
            _activeProcesses.Clear();
        }
    }
}
