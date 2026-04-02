using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using PocketMC.Desktop.Models;
using PocketMC.Desktop.Utils;

namespace PocketMC.Desktop.Services
{
    public class InstanceManager
    {
        private readonly string _serversDirectory;

        public InstanceManager(string appRootPath)
        {
            if (string.IsNullOrEmpty(appRootPath))
            {
                throw new ArgumentNullException(nameof(appRootPath));
            }
            _serversDirectory = Path.Combine(appRootPath, "servers");
        }

        private void EnsureDirectory()
        {
            if (!Directory.Exists(_serversDirectory))
            {
                Directory.CreateDirectory(_serversDirectory);
            }
        }

        public List<InstanceMetadata> GetAllInstances()
        {
            EnsureDirectory();
            var instances = new List<InstanceMetadata>();

            foreach (var dir in Directory.GetDirectories(_serversDirectory))
            {
                var metadataFile = Path.Combine(dir, ".pocket-mc.json");
                if (File.Exists(metadataFile))
                {
                    try
                    {
                        var content = File.ReadAllText(metadataFile);
                        var metadata = JsonSerializer.Deserialize<InstanceMetadata>(content);
                        if (metadata != null)
                        {
                            instances.Add(metadata);
                        }
                    }
                    catch
                    {
                        // Ignore malformed files
                    }
                }
            }

            return instances;
        }

        public InstanceMetadata CreateInstance(string name, string description)
        {
            EnsureDirectory();

            string baseSlug = SlugHelper.GenerateSlug(name);
            string slug = baseSlug;
            int counter = 2;

            while (Directory.Exists(Path.Combine(_serversDirectory, slug)))
            {
                slug = $"{baseSlug}-{counter}";
                counter++;
            }

            var newInstancePath = Path.Combine(_serversDirectory, slug);
            Directory.CreateDirectory(newInstancePath);

            var metadata = new InstanceMetadata
            {
                Id = Guid.NewGuid(),
                Name = name,
                Description = description,
                CreatedAt = DateTime.UtcNow
            };

            var metadataFile = Path.Combine(newInstancePath, ".pocket-mc.json");
            File.WriteAllText(metadataFile, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));

            return metadata;
        }

        public void UpdateMetadata(string folderName, string newName, string newDescription)
        {
            var oldFolderPath = Path.Combine(_serversDirectory, folderName);
            if (!Directory.Exists(oldFolderPath)) return;

            // 1. Calculate new slug
            string baseSlug = SlugHelper.GenerateSlug(newName);
            string newSlug = baseSlug;
            int counter = 2;
            
            while (Directory.Exists(Path.Combine(_serversDirectory, newSlug)) && newSlug != folderName)
            {
                newSlug = $"{baseSlug}-{counter}";
                counter++;
            }

            var currentFolderPath = oldFolderPath;

            // 2. Safely rename folder first (avoids antivirus file lock race conditions on the directory)
            if (newSlug != folderName)
            {
                var newFolderPath = Path.Combine(_serversDirectory, newSlug);
                try
                {
                    Directory.Move(oldFolderPath, newFolderPath);
                    currentFolderPath = newFolderPath;
                }
                catch (IOException)
                {
                    // Fallback to original path if locked
                    currentFolderPath = oldFolderPath;
                }
            }

            // 3. Write updated JSON inside the definitive folder
            var metadataFile = Path.Combine(currentFolderPath, ".pocket-mc.json");
            if (File.Exists(metadataFile))
            {
                var content = File.ReadAllText(metadataFile);
                var metadata = JsonSerializer.Deserialize<InstanceMetadata>(content) ?? new InstanceMetadata();
                metadata.Name = newName;
                metadata.Description = newDescription;

                File.WriteAllText(metadataFile, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
            }
        }

        public void DeleteInstance(string folderName)
        {
            var folderPath = Path.Combine(_serversDirectory, folderName);
            if (Directory.Exists(folderPath))
            {
                // Simple retry logic since files might be temporarily locked
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        Directory.Delete(folderPath, true);
                        break;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(500); // Wait 500ms and retry
                    }
                }
            }
        }

        public void OpenInExplorer(string folderName)
        {
            var folderPath = Path.Combine(_serversDirectory, folderName);
            if (Directory.Exists(folderPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{folderPath}\"",
                    UseShellExecute = true
                });
            }
        }

        public void AcceptEula(string folderName)
        {
            var folderPath = Path.Combine(_serversDirectory, folderName);
            if (Directory.Exists(folderPath))
            {
                File.WriteAllText(Path.Combine(folderPath, "eula.txt"), 
                    "# By changing the setting below to TRUE you are indicating your agreement to our EULA (https://aka.ms/MinecraftEULA).\n" +
                    "eula=true\n");
            }
        }
    }
}
