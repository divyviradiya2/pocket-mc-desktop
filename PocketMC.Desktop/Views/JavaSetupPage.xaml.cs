using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PocketMC.Desktop.Services;

namespace PocketMC.Desktop.Views
{
    public class JreDownloadTask : INotifyPropertyChanged
    {
        public int Version { get; set; }
        public string TaskName => $"Java {Version} Runtime";
        
        private string _statusText = "Pending";
        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        private Brush _statusColor = Brushes.Gray;
        public Brush StatusColor
        {
            get => _statusColor;
            set { _statusColor = value; OnPropertyChanged(nameof(StatusColor)); }
        }

        private double _progressValue;
        public double ProgressValue
        {
            get => _progressValue;
            set { _progressValue = value; OnPropertyChanged(nameof(ProgressValue)); }
        }

        private string _progressText = "";
        public string ProgressText
        {
            get => _progressText;
            set { _progressText = value; OnPropertyChanged(nameof(ProgressText)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    public partial class JavaSetupPage : Page
    {
        private readonly ApplicationState _applicationState;
        private readonly IServiceProvider _serviceProvider;
        private readonly JavaProvisioningService _javaProvisioning;
        private readonly ILogger<JavaSetupPage> _logger;
        public ObservableCollection<JreDownloadTask> DownloadTasks { get; } = new();

        public JavaSetupPage(
            ApplicationState applicationState,
            IServiceProvider serviceProvider,
            JavaProvisioningService javaProvisioning,
            ILogger<JavaSetupPage> logger)
        {
            InitializeComponent();
            _applicationState = applicationState;
            _serviceProvider = serviceProvider;
            _javaProvisioning = javaProvisioning;
            _logger = logger;
            TasksList.ItemsSource = DownloadTasks;
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            string appRootPath = _applicationState.GetRequiredAppRootPath();
            var requiredVersions = JavaRuntimeResolver.GetBundledJavaVersions();
            bool anyMissing = false;

            foreach (var v in requiredVersions)
            {
                if (!_javaProvisioning.IsJavaVersionPresent(v))
                {
                    DownloadTasks.Add(new JreDownloadTask { Version = v });
                    anyMissing = true;
                }
            }

            if (!anyMissing)
            {
                // Still ensure playit.exe is downloaded even when JREs are present (NET-01)
                await EnsurePlayitReadyAsync();
                NavigationService.Navigate(_serviceProvider.GetRequiredService<DashboardPage>());
                return;
            }

            TxtGlobalStatus.Text = "Downloading runtimes...";
            
            try
            {
                foreach (var task in DownloadTasks)
                {
                    await AcquireJreAsync(task);
                }
                
                // Download playit.exe alongside JREs (NET-01)
                TxtGlobalStatus.Text = "Downloading Playit.gg agent...";
                await EnsurePlayitReadyAsync();

                TxtGlobalStatus.Text = "All runtimes configured successfully!";
                await Task.Delay(1000);
                NavigationService.Navigate(_serviceProvider.GetRequiredService<DashboardPage>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Java runtime acquisition failed.");
                TxtGlobalStatus.Text = $"Error: {ex.Message}";
                TxtGlobalStatus.Foreground = Brushes.Red;
            }
        }

        private async Task AcquireJreAsync(JreDownloadTask task)
        {
            task.StatusText = "Downloading...";
            task.StatusColor = Brushes.LightBlue;

            var progress = new Progress<DownloadProgress>(p =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (task.StatusText == "Downloading...")
                    {
                        task.ProgressValue = p.Percentage;
                        task.ProgressText = $"{p.BytesRead / 1024 / 1024} MB / {p.TotalBytes / 1024 / 1024} MB";
                        
                        if (task.ProgressValue >= 100)
                        {
                            task.StatusText = "Extracting...";
                            task.StatusColor = Brushes.Orange;
                            task.ProgressValue = 0;
                            task.ProgressText = "Preparing extraction...";
                        }
                    }
                    else
                    {
                        task.ProgressValue = p.Percentage;
                        task.ProgressText = $"{p.BytesRead} / {p.TotalBytes} files";
                    }
                });
            });

            await _javaProvisioning.EnsureJavaAsync(task.Version, progress);

            task.StatusText = "Done";
            task.StatusColor = Brushes.LimeGreen;
            task.ProgressValue = 100;
            task.ProgressText = "Installed";
        }

        /// <summary>
        /// Downloads playit.exe if not already present (NET-01).
        /// Non-fatal: if download fails, the app continues (tunneling is optional).
        /// </summary>
        private async Task EnsurePlayitReadyAsync()
        {
            try
            {
                var downloader = new DownloaderService();
                await downloader.EnsurePlayitDownloadedAsync(_applicationState.GetRequiredAppRootPath());
            }
            catch (Exception ex)
            {
                // Playit download failure is non-fatal — server management still works
                _logger.LogWarning(ex, "Playit download failed but the rest of the app can continue.");
            }
        }
    }
}
