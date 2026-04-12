using System;
using System.Windows.Input;
using System.IO;
using System.Windows.Media.Imaging;
using PocketMC.Desktop.Core.Interfaces;
using PocketMC.Desktop.Core.Mvvm;

namespace PocketMC.Desktop.Features.Settings
{
    public class SettingsGeneralVM : ViewModelBase
    {
        private readonly string _serverDir;
        private readonly IDialogService _dialogService;
        private readonly Action _markDirty;

        private string? _motd;
        public string? Motd { get => _motd; set { if (SetProperty(ref _motd, value)) _markDirty(); } }

        private string _serverPort = "25565";
        public string ServerPort { get => _serverPort; set { if (SetProperty(ref _serverPort, value)) _markDirty(); } }

        private string? _serverIp;
        public string? ServerIp { get => _serverIp; set { if (SetProperty(ref _serverIp, value)) _markDirty(); } }

        private BitmapImage? _serverIcon;
        public BitmapImage? ServerIcon { get => _serverIcon; set => SetProperty(ref _serverIcon, value); }

        public ICommand BrowseIconCommand { get; }

        public SettingsGeneralVM(string serverDir, IDialogService dialogService, Action markDirty)
        {
            _serverDir = serverDir;
            _dialogService = dialogService;
            _markDirty = markDirty;
            BrowseIconCommand = new RelayCommand(async _ => await BrowseIconAsync());
        }

        public void LoadIcon()
        {
            var iconPath = Path.Combine(_serverDir, "server-icon.png");
            if (File.Exists(iconPath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(iconPath);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    ServerIcon = bmp;
                }
                catch { ServerIcon = null; }
            }
            else ServerIcon = null;
        }

        public async System.Threading.Tasks.Task BrowseIconAsync()
        {
            var file = await _dialogService.OpenFileDialogAsync("Select Server Icon (64x64 PNG)", "PNG Files (*.png)|*.png");
            if (file != null)
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit(); bmp.UriSource = new Uri(file); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.EndInit();
                    if (bmp.PixelWidth != 64 || bmp.PixelHeight != 64) { _dialogService.ShowMessage("Invalid Size", "Icon must be exactly 64x64 pixels.", DialogType.Warning); return; }
                    File.Copy(file, Path.Combine(_serverDir, "server-icon.png"), true);
                    ServerIcon = bmp;
                }
                catch (Exception ex) { _dialogService.ShowMessage("Error", ex.Message, DialogType.Error); }
            }
        }
    }
}
