using System;
using System.IO;
using System.Text.Json;
using PocketMC.Desktop.Models;

namespace PocketMC.Desktop.Services
{
    public class SettingsManager
    {
        private readonly string _settingsFilePath;

        public SettingsManager()
        {
            _settingsFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PocketMC",
                "settings.json");
        }

        public AppSettings Load()
        {
            if (!File.Exists(_settingsFilePath))
            {
                return new AppSettings { AppRootPath = null };
            }

            try
            {
                var content = File.ReadAllText(_settingsFilePath);
                return JsonSerializer.Deserialize<AppSettings>(content) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings { AppRootPath = null };
            }
        }

        public void Save(AppSettings settings)
        {
            var directory = Path.GetDirectoryName(_settingsFilePath);
            if (!Directory.Exists(directory) && directory != null)
            {
                Directory.CreateDirectory(directory);
            }

            var content = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, content);
        }
    }
}
