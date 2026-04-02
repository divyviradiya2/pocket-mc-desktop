using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;
using PocketMC.Desktop.Services;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Utils;
using System.Linq;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace PocketMC.Desktop.Views
{
    /// <summary>
    /// ViewModel wrapper for InstanceMetadata that adds live state tracking.
    /// </summary>
    public class InstanceCardViewModel : INotifyPropertyChanged
    {
        private readonly InstanceMetadata _metadata;
        private ServerState _state = ServerState.Stopped;

        public InstanceCardViewModel(InstanceMetadata metadata)
        {
            _metadata = metadata;

            // Sync initial state from ServerProcessManager
            if (ServerProcessManager.IsRunning(metadata.Id))
            {
                var proc = ServerProcessManager.GetProcess(metadata.Id);
                _state = proc?.State ?? ServerState.Stopped;
            }
        }

        public InstanceMetadata Metadata => _metadata;
        public Guid Id => _metadata.Id;
        public string Name => _metadata.Name;
        public string Description => _metadata.Description;

        public bool IsRunning => _state == ServerState.Starting || _state == ServerState.Online || _state == ServerState.Stopping;
        public bool IsWaitingToRestart => ServerProcessManager.IsWaitingToRestart(Id);
        public bool ShowRunningControls => IsRunning || IsWaitingToRestart;
        public string StopButtonText => IsWaitingToRestart ? "Abort" : "Stop";

        private string? _countdownText;
        public string StatusText => _countdownText ?? _state switch
        {
            ServerState.Stopped => "● Stopped",
            ServerState.Starting => "◉ Starting...",
            ServerState.Online => "● Online",
            ServerState.Stopping => "◉ Stopping...",
            ServerState.Crashed => "✖ Crashed",
            _ => "Unknown"
        };

        public Brush StatusColor => _state switch
        {
            ServerState.Online => Brushes.LimeGreen,
            ServerState.Starting or ServerState.Stopping => Brushes.Orange,
            ServerState.Crashed => Brushes.Red,
            _ => Brushes.Gray
        };

        // LiveCharts properties
        public ObservableCollection<double> CpuHistory { get; } = new();
        public ObservableCollection<double> RamHistory { get; } = new();
        public LiveChartsCore.ISeries[] CpuSeries { get; set; }
        public LiveChartsCore.ISeries[] RamSeries { get; set; }
        
        public LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint InvisiblePaint { get; set; } = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SkiaSharp.SKColors.Transparent);
        
        public LiveChartsCore.SkiaSharpView.Axis[] InvisibleXAxes { get; set; } = new[] 
        { 
            new LiveChartsCore.SkiaSharpView.Axis { IsVisible = false, ShowSeparatorLines = false } 
        };
        public LiveChartsCore.SkiaSharpView.Axis[] InvisibleYAxes { get; set; } = new[] 
        { 
            new LiveChartsCore.SkiaSharpView.Axis { IsVisible = false, MinLimit = 0, ShowSeparatorLines = false } 
        };

        private string _playerStatus = "0 Players Online";
        public string PlayerStatus { get => _playerStatus; set { _playerStatus = value; OnPropertyChanged(nameof(PlayerStatus)); } }

        private string _cpuText = "CPU 0%";
        public string CpuText { get => _cpuText; set { _cpuText = value; OnPropertyChanged(nameof(CpuText)); } }

        private string _ramText = "RAM 0 MB";
        public string RamText { get => _ramText; set { _ramText = value; OnPropertyChanged(nameof(RamText)); } }

        // --- Tunnel / Networking Properties ---
        private PlayitTunnelState _tunnelState = PlayitTunnelState.Stopped;

        /// <summary>True if this instance has a stored public IP from a previous session.</summary>
        public bool HasStoredAddress => !string.IsNullOrEmpty(_metadata.PlayitPublicAddress);

        public string TunnelStatusText
        {
            get
            {
                if (_tunnelState == PlayitTunnelState.Starting)
                    return "⏳ Starting Playit agent...";
                if (_tunnelState == PlayitTunnelState.Error)
                    return "⚠ Tunnel error";
                if (HasStoredAddress)
                    return $"🌐 {_metadata.PlayitPublicAddress}";
                return "";
            }
        }

        public string TunnelButtonText
        {
            get
            {
                if (_tunnelState == PlayitTunnelState.Starting)
                    return "Starting...";
                if (_tunnelState == PlayitTunnelState.Connected)
                    return "Stop Tunnel";
                if (HasStoredAddress)
                    return "📋 Copy IP";
                return "Get Public IP";
            }
        }

        public bool IsTunnelActive => _tunnelState == PlayitTunnelState.Connected || _tunnelState == PlayitTunnelState.Starting;

        public void UpdateTunnelState(PlayitTunnelState state)
        {
            _tunnelState = state;
            RefreshTunnelUI();
        }

        public void RefreshTunnelUI()
        {
            OnPropertyChanged(nameof(TunnelStatusText));
            OnPropertyChanged(nameof(TunnelButtonText));
            OnPropertyChanged(nameof(IsTunnelActive));
            OnPropertyChanged(nameof(HasStoredAddress));
        }

        public void EnsureChartsReady()
        {
            if (CpuSeries != null) return; // Only init once
            CpuSeries = new LiveChartsCore.ISeries[]
            {
                new LiveChartsCore.SkiaSharpView.LineSeries<double>
                {
                    Values = CpuHistory,
                    Fill = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SkiaSharp.SKColor.Parse("#204CAF50")),
                    Stroke = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SkiaSharp.SKColor.Parse("#4CAF50")) { StrokeThickness = 2 },
                    GeometryFill = null,
                    GeometryStroke = null,
                    LineSmoothness = 0.5
                }
            };

            RamSeries = new LiveChartsCore.ISeries[]
            {
                new LiveChartsCore.SkiaSharpView.LineSeries<double>
                {
                    Values = RamHistory,
                    Fill = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SkiaSharp.SKColor.Parse("#202196F3")),
                    Stroke = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(SkiaSharp.SKColor.Parse("#2196F3")) { StrokeThickness = 2 },
                    GeometryFill = null,
                    GeometryStroke = null,
                    LineSmoothness = 0.5
                }
            };
            OnPropertyChanged(nameof(CpuSeries));
            OnPropertyChanged(nameof(RamSeries));
        }

        public void UpdateState(ServerState newState)
        {
            _countdownText = null;
            _state = newState;
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(ShowRunningControls));
            OnPropertyChanged(nameof(IsWaitingToRestart));
            OnPropertyChanged(nameof(StopButtonText));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
        }

        public void UpdateCountdown(int seconds)
        {
            _countdownText = $"Restarting in {seconds}s...";
            OnPropertyChanged(nameof(IsWaitingToRestart));
            OnPropertyChanged(nameof(ShowRunningControls));
            OnPropertyChanged(nameof(StopButtonText));
            _state = ServerState.Crashed;
            OnPropertyChanged(nameof(StatusText));
        }

        public void RefreshNameDescription()
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Description));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string prop) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public partial class DashboardPage : Page
    {
        private readonly InstanceManager _instanceManager;
        private readonly string _appRootPath;
        private ObservableCollection<InstanceCardViewModel> _viewModels = new();

        public DashboardPage(string appRootPath)
        {
            InitializeComponent();
            _appRootPath = appRootPath;
            _instanceManager = new InstanceManager(appRootPath);
            LoadInstances();

            // Subscribe to global state changes
            ServerProcessManager.OnInstanceStateChanged += OnServerStateChanged;
            ServerProcessManager.OnRestartCountdownTick += OnRestartCountdownTick;
            MainWindow.GlobalMonitor.OnGlobalMetricsUpdated += UpdateMetrics;
        }

        private void UpdateMetrics()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var dictionary = MainWindow.GlobalMonitor.Metrics;
                foreach (var vm in _viewModels)
                {
                    if (dictionary.TryGetValue(vm.Id, out var metric))
                    {
                        vm.EnsureChartsReady();
                        
                        vm.CpuHistory.Add(metric.CpuUsage);
                        if (vm.CpuHistory.Count > 30) vm.CpuHistory.RemoveAt(0);
                        vm.CpuText = $"CPU {Math.Round(metric.CpuUsage)}%";

                        vm.RamHistory.Add(metric.RamUsageMb);
                        if (vm.RamHistory.Count > 30) vm.RamHistory.RemoveAt(0);
                        vm.RamText = $"RAM {Math.Round(metric.RamUsageMb)} MB";

                        vm.PlayerStatus = $"{metric.PlayerCount} Players Online";
                        
                        // High RAM warning badge logic
                        if (vm.Metadata.MaxRamMb > 0 && metric.RamUsageMb > vm.Metadata.MaxRamMb * 1.1)
                        {
                            vm.RamText += " ⚠ (High)";
                        }
                    }
                    else
                    {
                        // Default out
                        vm.PlayerStatus = "0 Players Online";
                        vm.CpuText = "CPU 0%";
                        vm.RamText = "RAM 0 MB";
                    }
                }
            });
        }

        private void LoadInstances()
        {
            var instances = _instanceManager.GetAllInstances();
            _viewModels = new ObservableCollection<InstanceCardViewModel>(
                instances.Select(m => new InstanceCardViewModel(m)));
            InstanceGrid.ItemsSource = _viewModels;
        }

        private void OnServerStateChanged(Guid instanceId, ServerState newState)
        {
            Dispatcher.Invoke(() =>
            {
                var vm = _viewModels.FirstOrDefault(v => v.Id == instanceId);
                vm?.UpdateState(newState);
            });
        }

        private void OnRestartCountdownTick(Guid instanceId, int seconds)
        {
            Dispatcher.Invoke(() =>
            {
                var vm = _viewModels.FirstOrDefault(v => v.Id == instanceId);
                if (vm != null)
                {
                    vm.UpdateCountdown(seconds);
                }
            });
        }

        // --- Helper to get ViewModel from sender ---
        private InstanceCardViewModel? GetViewModel(object sender)
        {
            if (sender is FrameworkElement element && element.DataContext is InstanceCardViewModel vm)
                return vm;
            return null;
        }

        private void BtnMoreOptions_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.DataContext = btn.DataContext;
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void BtnManage_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetViewModel(sender);
            if (vm != null)
                NavigationService.Navigate(new ServerSettingsPage(vm.Metadata, _appRootPath));
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetViewModel(sender);
            if (vm != null)
            {
                string? folderName = FindFolderById(vm.Id);
                if (folderName != null)
                    _instanceManager.OpenInExplorer(folderName);
            }
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetViewModel(sender);
            if (vm == null) return;

            try
            {
                ServerProcessManager.StartProcess(vm.Metadata, _appRootPath);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    ex.Message,
                    "Cannot Start Server",
                    MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }

        private async void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetViewModel(sender);
            if (vm == null) return;

            try
            {
                if (vm.IsWaitingToRestart)
                {
                    ServerProcessManager.AbortRestartDelay(vm.Id);
                }
                else
                {
                    await ServerProcessManager.StopProcessAsync(vm.Id);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    ex.Message,
                    "Stop Error",
                    MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        private void BtnConsole_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetViewModel(sender);
            if (vm == null) return;

            var process = ServerProcessManager.GetProcess(vm.Id);
            if (process != null)
            {
                var consolePage = new ServerConsolePage(vm.Metadata, process);

                // Wire up the Playit Logs tab if the shared agent is running
                if (_sharedCoordinator != null)
                {
                    consolePage.AttachPlayitCoordinator(_sharedCoordinator);
                }

                NavigationService.Navigate(consolePage);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  SHARED PLAYIT AGENT
        //  Only ONE playit.exe process runs for the entire app.
        //  Each server just needs its own tunnel address from the dashboard.
        // ═══════════════════════════════════════════════════════════════
        private static PlayitCoordinator? _sharedCoordinator;
        private static readonly object _coordinatorLock = new();

        private async void BtnTunnel_Click(object sender, RoutedEventArgs e)
        {
            var vm = GetViewModel(sender);
            if (vm == null) return;

            try
            {
                // ═══ STATE 1: Has stored IP → Copy to clipboard ═══
                if (vm.HasStoredAddress && vm.Metadata.PlayitPublicAddress != null)
                {
                    System.Windows.Clipboard.SetText(vm.Metadata.PlayitPublicAddress);
                    System.Windows.MessageBox.Show(
                        $"Copied to clipboard:\n{vm.Metadata.PlayitPublicAddress}",
                        "IP Copied",
                        MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                    return;
                }

                // ═══ STATE 2: No stored IP → Start agent + auto-detect via API ═══
                var playitManager = new PlayitManagerService(_appRootPath);
                if (!playitManager.IsInstalled)
                {
                    System.Windows.MessageBox.Show(
                        "Playit CLI is not yet installed. It will be downloaded on next app startup.",
                        "Playit Not Ready",
                        MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                    return;
                }

                vm.UpdateTunnelState(PlayitTunnelState.Starting);
                EnsureSharedAgentRunning(playitManager);

                // Give agent a moment to connect if it just started
                if (_sharedCoordinator?.State != PlayitTunnelState.Connected)
                    await System.Threading.Tasks.Task.Delay(3000);

                // Auto-detect the tunnel address via Playit API
                await AutoDetectTunnelAddress(vm);
            }
            catch (Exception ex)
            {
                vm.UpdateTunnelState(PlayitTunnelState.Stopped);
                System.Windows.MessageBox.Show(
                    $"Failed to start tunnel: {ex.Message}",
                    "Tunnel Error", MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Ensures a single shared playit.exe process is running.
        /// If it's already running, this is a no-op.
        /// </summary>
        private void EnsureSharedAgentRunning(PlayitManagerService playitManager)
        {
            lock (_coordinatorLock)
            {
                // Already running and healthy
                if (_sharedCoordinator != null &&
                    (_sharedCoordinator.State == PlayitTunnelState.Connected || _sharedCoordinator.State == PlayitTunnelState.Starting))
                {
                    return;
                }

                // Clean up dead coordinator
                if (_sharedCoordinator != null)
                {
                    _sharedCoordinator.Dispose();
                    _sharedCoordinator = null;
                }

                var jobObject = ServerProcessManager.SharedJobObject;
                _sharedCoordinator = new PlayitCoordinator(playitManager.PlayitExePath, jobObject);

                // We don't wire up OnTunnelReady here — instead we use the API to detect tunnels
                _sharedCoordinator.Start(null!, _appRootPath);
            }
        }

        /// <summary>
        /// Reads the server port from server.properties for a given instance.
        /// </summary>
        private int GetServerPort(Guid instanceId)
        {
            try
            {
                var folder = FindFolderById(instanceId);
                if (folder != null)
                {
                    var settings = new SettingsManager().Load();
                    if (!string.IsNullOrEmpty(settings.AppRootPath))
                    {
                        var propsPath = System.IO.Path.Combine(settings.AppRootPath, "servers", folder, "server.properties");
                        if (System.IO.File.Exists(propsPath))
                        {
                            var props = ServerPropertiesParser.Read(propsPath);
                            if (props.TryGetValue("server-port", out var portStr) && int.TryParse(portStr, out var parsed))
                                return parsed;
                        }
                    }
                }
            }
            catch { }
            return 25565;
        }

        /// <summary>
        /// Calls the Playit API to find a tunnel matching this server's local port.
        /// If found → auto-saves. If not → guides user to create one, then retries.
        /// </summary>
        private async System.Threading.Tasks.Task AutoDetectTunnelAddress(InstanceCardViewModel vm)
        {
            int serverPort = GetServerPort(vm.Id);

            // Try to find a tunnel matching this server's port
            var tunnel = await PlayitApiClient.FindTunnelByLocalPortAsync(serverPort);

            if (tunnel != null && !string.IsNullOrEmpty(tunnel.PublicAddress))
            {
                // ✅ Tunnel found — auto-save the address
                vm.Metadata.PlayitPublicAddress = tunnel.PublicAddress;
                SaveInstanceMetadata(vm.Metadata);
                vm.RefreshTunnelUI();
                vm.UpdateTunnelState(PlayitTunnelState.Stopped);

                System.Windows.Clipboard.SetText(tunnel.PublicAddress);
                System.Windows.MessageBox.Show(
                    $"Public address found and saved!\n\n" +
                    $"🌐 {tunnel.PublicAddress}\n\n" +
                    $"(Copied to clipboard)\n" +
                    $"Tunnel: {tunnel.Name} → localhost:{tunnel.LocalPort}",
                    "Tunnel Configured",
                    MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            // ❌ No tunnel found for this port — Auto-create it!
            vm.UpdateTunnelState(PlayitTunnelState.Starting);
            try
            {
                var createdTunnel = await PlayitApiClient.CreateTunnelAsync(vm.Name, serverPort);

                if (createdTunnel != null && !string.IsNullOrEmpty(createdTunnel.PublicAddress))
                {
                    vm.Metadata.PlayitPublicAddress = createdTunnel.PublicAddress;
                    SaveInstanceMetadata(vm.Metadata);
                    vm.RefreshTunnelUI();
                    vm.UpdateTunnelState(PlayitTunnelState.Stopped);

                    System.Windows.Clipboard.SetText(createdTunnel.PublicAddress);
                    System.Windows.MessageBox.Show(
                        $"Tunnel created successfully!\n\n" +
                        $"🌐 {createdTunnel.PublicAddress}\n\n" +
                        $"(Copied to clipboard)",
                        "Tunnel Created",
                        MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    throw new Exception("Returned tunnel was null.");
                }
            }
            catch (Exception ex)
            {
                // If Playit forbids creation using the local agent key, seamlessly fallback to dashboard
                if (ex.Message.Contains("NotAllowedWithReadOnly") || ex.Message.Contains("Unauthorized"))
                {
                    vm.UpdateTunnelState(PlayitTunnelState.Stopped);
                    
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://playit.gg/account/tunnels") { UseShellExecute = true }); } catch { }

                    var result = System.Windows.MessageBox.Show(
                        $"Playit.gg requires you to manually create your first tunnel on the dashboard.\n\n" +
                        $"The dashboard has opened in your browser.\n" +
                        $"Please create a new tunnel by filling out these fields:\n\n" +
                        $"  1. Click 'Add Tunnel'\n" +
                        $"  2. Name your tunnel: (e.g. {vm.Name})\n" +
                        $"  3. Tunnel Type: Minecraft Java\n" +
                        $"  4. Assign to Agent: (Select your PC name)\n" +
                        $"  5. Local Port: {serverPort}\n" +
                        $"  6. Click 'Next' and save the tunnel\n\n" +
                        $"After creating the tunnel, click OK to auto-detect it.",
                        $"Create Tunnel Manually — {vm.Name}",
                        MessageBoxButton.OKCancel,
                        System.Windows.MessageBoxImage.Information);

                    if (result == MessageBoxResult.OK)
                    {
                        vm.UpdateTunnelState(PlayitTunnelState.Starting);
                        await System.Threading.Tasks.Task.Delay(2000); 

                        var retryTunnel = await PlayitApiClient.FindTunnelByLocalPortAsync(serverPort);
                        if (retryTunnel != null && !string.IsNullOrEmpty(retryTunnel.PublicAddress))
                        {
                            vm.Metadata.PlayitPublicAddress = retryTunnel.PublicAddress;
                            SaveInstanceMetadata(vm.Metadata);
                            vm.RefreshTunnelUI();
                            vm.UpdateTunnelState(PlayitTunnelState.Stopped);

                            System.Windows.Clipboard.SetText(retryTunnel.PublicAddress);
                            System.Windows.MessageBox.Show(
                                $"Public address found and saved!\n\n" +
                                $"🌐 {retryTunnel.PublicAddress}\n\n" +
                                $"(Copied to clipboard)",
                                "Tunnel Configured",
                                MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Information);
                        }
                        else
                        {
                            vm.UpdateTunnelState(PlayitTunnelState.Stopped);
                            System.Windows.MessageBox.Show(
                                $"Could not detect a tunnel for port {serverPort}.\n\n" +
                                $"Make sure you created the tunnel on the Playit dashboard " +
                                $"with local port set to {serverPort}, then try again.",
                                "Tunnel Not Found",
                                MessageBoxButton.OK,
                                System.Windows.MessageBoxImage.Warning);
                        }
                    }
                }
                else
                {
                    vm.UpdateTunnelState(PlayitTunnelState.Stopped);
                    System.Windows.MessageBox.Show(
                        $"Failed to automatically create a tunnel.\n\n" +
                        $"Error: {ex.Message}\n\n" +
                        $"Please check your connection, or check the Playit.gg dashboard manually.",
                        "Tunnel Creation Failed",
                        MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
            }
        }

        /// <summary>
        /// Persists instance metadata to the .pocket-mc.json file.
        /// </summary>
        private void SaveInstanceMetadata(InstanceMetadata metadata)
        {
            var folder = FindFolderById(metadata.Id);
            if (folder == null) return;

            var settings = new SettingsManager().Load();
            if (string.IsNullOrEmpty(settings.AppRootPath)) return;

            var metaFile = System.IO.Path.Combine(settings.AppRootPath, "servers", folder, ".pocket-mc.json");
            System.IO.File.WriteAllText(metaFile,
                System.Text.Json.JsonSerializer.Serialize(metadata,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }

        private string? FindFolderById(Guid id)
        {
            var settings = new SettingsManager().Load();
            if (string.IsNullOrEmpty(settings.AppRootPath)) return null;

            var dirPath = System.IO.Path.Combine(settings.AppRootPath, "servers");
            if (!System.IO.Directory.Exists(dirPath)) return null;

            foreach (var dir in System.IO.Directory.GetDirectories(dirPath))
            {
                var metaFile = System.IO.Path.Combine(dir, ".pocket-mc.json");
                if (System.IO.File.Exists(metaFile))
                {
                    var content = System.IO.File.ReadAllText(metaFile);
                    if (content.Contains(id.ToString()))
                        return new System.IO.DirectoryInfo(dir).Name;
                }
            }
            return null;
        }

        private void BtnNewInstance_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new NewInstanceDialog(_instanceManager, _appRootPath);
            dialog.Owner = Window.GetWindow(this);
            if (dialog.ShowDialog() == true)
            {
                LoadInstances();
            }
        }

        private void DeleteInstance_Click(object sender, RoutedEventArgs e)
        {
            InstanceCardViewModel? vm = null;
            if (sender is MenuItem menuItem && menuItem.DataContext is InstanceCardViewModel mvm)
                vm = mvm;
            
            if (vm == null) return;

            if (ServerProcessManager.IsRunning(vm.Id))
            {
                System.Windows.MessageBox.Show(
                    "Cannot delete a running server. Stop it first.",
                    "Server Running",
                    MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            var prompt = System.Windows.MessageBox.Show(
                "Are you sure you want to completely erase the " + vm.Name + " server? All worlds and files will be permanently deleted.",
                "Delete Server",
                MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (prompt == MessageBoxResult.Yes)
            {
                var folder = FindFolderById(vm.Id);
                if (folder != null)
                {
                    _instanceManager.DeleteInstance(folder);
                    LoadInstances();
                }
            }
        }

        private async void RenameInstance_Click(object sender, RoutedEventArgs e)
        {
            InstanceCardViewModel? vm = null;
            if (sender is MenuItem menuItem && menuItem.DataContext is InstanceCardViewModel mvm)
                vm = mvm;
            
            if (vm == null) return;

            var dialog = new Wpf.Ui.Controls.MessageBox
            {
                Title = "Rename Server",
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel"
            };

            var stackPanel = new StackPanel();
            var txtName = new Wpf.Ui.Controls.TextBox { Text = vm.Name };
            var txtDesc = new Wpf.Ui.Controls.TextBox { Text = vm.Description, Margin = new Thickness(0, 10, 0, 0) };
            stackPanel.Children.Add(txtName);
            stackPanel.Children.Add(txtDesc);
            dialog.Content = stackPanel;

            var result = await dialog.ShowDialogAsync();
            if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
            {
                if (!string.IsNullOrWhiteSpace(txtName.Text))
                {
                    var folder = FindFolderById(vm.Id);
                    if (folder != null)
                    {
                        _instanceManager.UpdateMetadata(folder, txtName.Text, txtDesc.Text);
                        LoadInstances();
                    }
                }
            }
        }
    }
}
