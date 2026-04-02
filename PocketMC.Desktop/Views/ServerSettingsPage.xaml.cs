using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Services;
using PocketMC.Desktop.Utils;

namespace PocketMC.Desktop.Views
{
    public partial class ServerSettingsPage : Page
    {
        private InstanceMetadata _metadata;
        private string _appRoot;
        private string _serverDir;

        public ServerSettingsPage(InstanceMetadata metadata, string appRoot)
        {
            InitializeComponent();
            _metadata = metadata;
            _appRoot = appRoot;
            
            // Assume the format is servers/<slug/id>
            string potentialSlug = SlugHelper.GenerateSlug(_metadata.Name);
            // Quick implementation relies on getting exact path from the Manager.
            // But we will scan servers dir to find the .pocket-mc.json with same ID
            _serverDir = Path.Combine(_appRoot, "servers");
            foreach (var dir in Directory.GetDirectories(_serverDir))
            {
                var metaFile = Path.Combine(dir, ".pocket-mc.json");
                if (File.Exists(metaFile))
                {
                    try
                    {
                        var content = File.ReadAllText(metaFile);
                        var meta = System.Text.Json.JsonSerializer.Deserialize<InstanceMetadata>(content);
                        if (meta != null && meta.Id == _metadata.Id)
                        {
                            _serverDir = dir;
                            break;
                        }
                    }
                    catch { }
                }
            }

            LoadSettings();
        }

        private void LoadSettings()
        {
            SldMinRam.Value = _metadata.MinRamMb > 0 ? _metadata.MinRamMb : 1024;
            SldMaxRam.Value = _metadata.MaxRamMb > 0 ? _metadata.MaxRamMb : 4096;

            var propsFile = Path.Combine(_serverDir, "server.properties");
            var props = ServerPropertiesParser.Read(propsFile);

            TxtMotd.Text = props.TryGetValue("motd", out var motd) ? motd : "A Minecraft Server";
            TxtSeed.Text = props.TryGetValue("level-seed", out var seed) ? seed : "";
            TxtSpawnProtection.Text = props.TryGetValue("spawn-protection", out var prot) ? prot : "16";
            TxtMaxPlayers.Text = props.TryGetValue("max-players", out var mp) ? mp : "20";
            TxtServerPort.Text = props.TryGetValue("server-port", out var port) ? port : "25565";
            TxtServerIp.Text = props.TryGetValue("server-ip", out var ip) ? ip : "";

            if (props.TryGetValue("level-type", out var lt))
            {
                foreach (ComboBoxItem item in CmbLevelType.Items)
                {
                    if (item.Content.ToString() == lt)
                        CmbLevelType.SelectedItem = item;
                }
            }

            if (props.TryGetValue("online-mode", out var om)) ChkOnlineMode.IsChecked = om == "true";
            if (props.TryGetValue("pvp", out var pvp)) ChkPvp.IsChecked = pvp == "true";
            if (props.TryGetValue("white-list", out var wl)) ChkWhiteList.IsChecked = wl == "true";

            if (props.TryGetValue("gamemode", out var gm))
            {
                foreach (ComboBoxItem item in CmbGamemode.Items)
                {
                    if (item.Content.ToString() == gm)
                        CmbGamemode.SelectedItem = item;
                }
            }

            if (props.TryGetValue("difficulty", out var dif))
            {
                foreach (ComboBoxItem item in CmbDifficulty.Items)
                {
                    if (item.Content.ToString() == dif)
                        CmbDifficulty.SelectedItem = item;
                }
            }

            if (props.TryGetValue("enable-command-block", out var cb)) ChkAllowBlock.IsChecked = cb == "true";
            if (props.TryGetValue("allow-flight", out var af)) ChkAllowFlight.IsChecked = af == "true";
            if (props.TryGetValue("allow-nether", out var an)) ChkAllowNether.IsChecked = an == "true";

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
                    ImgIconPreview.Source = bmp;
                }
                catch { }
            }
        }

        private void RamSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtRamWarning == null) return;
            ulong totalMb = MemoryHelper.GetTotalPhysicalMemoryMb();
            if (totalMb > 0)
            {
                double totalRequested = SldMaxRam.Value;
                if (totalRequested > (totalMb * 0.8))
                {
                    TxtRamWarning.Visibility = Visibility.Visible;
                }
                else
                {
                    TxtRamWarning.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void TxtMotd_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Simple implementation of MOTD parsing for § and &
            TxtMotdPreview.Text = TxtMotd.Text;
        }

        private void BtnBrowseIcon_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "PNG Files (*.png)|*.png",
                Title = "Select Server Icon (Must be 64x64)"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(dlg.FileName);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();

                    if (bmp.PixelWidth != 64 || bmp.PixelHeight != 64)
                    {
                        MessageBox.Show("Icon must be exactly 64x64 pixels.", "Invalid Size", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    ImgIconPreview.Source = bmp;
                    // File is copied on "Save" or we can copy it instantly:
                    var dest = Path.Combine(_serverDir, "server-icon.png");
                    File.Copy(dlg.FileName, dest, true);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error loading image: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService.CanGoBack)
                NavigationService.GoBack();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            _metadata.MinRamMb = (int)SldMinRam.Value;
            _metadata.MaxRamMb = (int)SldMaxRam.Value;

            // Save JSON explicitly
            var metaFile = Path.Combine(_serverDir, ".pocket-mc.json");
            File.WriteAllText(metaFile, System.Text.Json.JsonSerializer.Serialize(_metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            // Save Props
            var propsFile = Path.Combine(_serverDir, "server.properties");
            var props = ServerPropertiesParser.Read(propsFile);

            props["motd"] = TxtMotd.Text;
            if (!string.IsNullOrWhiteSpace(TxtSeed.Text)) props["level-seed"] = TxtSeed.Text;
            props["spawn-protection"] = TxtSpawnProtection.Text;
            props["max-players"] = TxtMaxPlayers.Text;
            props["server-port"] = TxtServerPort.Text;
            if (!string.IsNullOrWhiteSpace(TxtServerIp.Text)) props["server-ip"] = TxtServerIp.Text;

            props["level-type"] = ((ComboBoxItem)CmbLevelType.SelectedItem).Content.ToString();
            props["online-mode"] = ChkOnlineMode.IsChecked == true ? "true" : "false";
            props["pvp"] = ChkPvp.IsChecked == true ? "true" : "false";
            props["white-list"] = ChkWhiteList.IsChecked == true ? "true" : "false";
            props["gamemode"] = ((ComboBoxItem)CmbGamemode.SelectedItem).Content.ToString();
            props["difficulty"] = ((ComboBoxItem)CmbDifficulty.SelectedItem).Content.ToString();
            
            props["enable-command-block"] = ChkAllowBlock.IsChecked == true ? "true" : "false";
            props["allow-flight"] = ChkAllowFlight.IsChecked == true ? "true" : "false";
            props["allow-nether"] = ChkAllowNether.IsChecked == true ? "true" : "false";

            ServerPropertiesParser.Write(propsFile, props);

            MessageBox.Show("Settings configuration saved successfully.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
