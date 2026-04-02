using System.Windows;
using System.Windows.Controls;
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
    public partial class DashboardPage : Page
    {
        private readonly InstanceManager _instanceManager;
        private readonly string _appRootPath;

        public DashboardPage(string appRootPath)
        {
            InitializeComponent();
            _appRootPath = appRootPath;
            _instanceManager = new InstanceManager(appRootPath);
            LoadInstances();
        }

        private void LoadInstances()
        {
            var instances = _instanceManager.GetAllInstances();
            InstanceGrid.ItemsSource = instances;
        }

        private void BtnMoreOptions_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void BtnManage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.CommandParameter is InstanceMetadata metadata)
            {
                NavigateToSettings(metadata);
            }
        }

        private void InstanceCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is InstanceMetadata metadata)
            {
                NavigateToSettings(metadata);
            }
        }

        private void NavigateToSettings(InstanceMetadata metadata)
        {
            NavigationService.Navigate(new ServerSettingsPage(metadata, _appRootPath));
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.CommandParameter is InstanceMetadata metadata)
            {
                // We know InstanceManager relies on folder names (slugs) to find them, derived from Name and id.
                // Wait, InstanceManager.OpenInExplorer takes the slugified folder name.
                // We can recalculate it or scan, but for now we look up the folder by generating slug? No.
                // Wait! To open the exact folder, we need the slug. But we only have `InstanceMetadata`.
                // Let's modify InstanceManager to find it or we can pass the slug.
                // The requirements say instance files live under `<APP_ROOT>/servers/<slug>/`.
                // But `InstanceMetadata` doesn't currently store the `<slug>`. 
                // Let's let the `InstanceManager` do a folder lookup by `.pocket-mc.json` UUID.
                
                // For the scope of this file: let's quickly regenerate slug or we assume the folder name.
                // Actually, let's just make InstanceManager do the job based on Id for safety.
                // For now, I will use a simple workaround to find folder by Id.
                var allInstances = _instanceManager.GetAllInstances(); // we probably should store FolderName but ok
                // In a proper implementation, InstanceMetadata returned would have a FolderName not serialized,
                // but we can just tell InstanceManager about it.
                // To keep it simple, I'll pass the Name, assuming it didn't change enough to lose the slug,
                // BUT renaming preserves slug.
                
                // Let's implement this method directly:
                // We need the folder name. I'll add `FolderName` implicitly if I adjust InstanceManager later.
                // Right now, I'll look through folders ourselves.
                string folderName = FindFolderById(metadata.Id);
                if (folderName != null)
                {
                    _instanceManager.OpenInExplorer(folderName);
                }
            }
        }

        private string? FindFolderById(System.Guid id)
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
                    {
                        return new System.IO.DirectoryInfo(dir).Name;
                    }
                }
            }
            return null;
        }

        private async void BtnNewInstance_Click(object sender, RoutedEventArgs e)
        {
            // Wpf Dialog
            var dialog = new Wpf.Ui.Controls.MessageBox
            {
                Title = "New Server Instance",
                Content = "Name for the new instance:",
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel"
            };

            // In WPF-UI 3.0, we use Content Dialogs or custom windows for text input.
            // For simplicity, we just use a basic text prompt logic or native input box if available.
            // I'll create a TextBox dynamically.
            var stackPanel = new StackPanel();
            var txtName = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Server Name" };
            var txtDesc = new Wpf.Ui.Controls.TextBox { PlaceholderText = "Description", Margin = new Thickness(0, 10, 0, 0) };
            stackPanel.Children.Add(txtName);
            stackPanel.Children.Add(txtDesc);
            dialog.Content = stackPanel;

            var result = await dialog.ShowDialogAsync();
            if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
            {
                if (!string.IsNullOrWhiteSpace(txtName.Text))
                {
                    _instanceManager.CreateInstance(txtName.Text, txtDesc.Text);
                    LoadInstances();
                }
            }
        }

        private void DeleteInstance_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is InstanceMetadata metadata)
            {
                var prompt = System.Windows.MessageBox.Show(
                    "Are you sure you want to completely erase the " + metadata.Name + " server? All worlds and files will be permanently deleted.",
                    "Delete Server",
                    MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);
                
                if (prompt == MessageBoxResult.Yes)
                {
                    var folder = FindFolderById(metadata.Id);
                    if (folder != null)
                    {
                        _instanceManager.DeleteInstance(folder);
                        LoadInstances();
                    }
                }
            }
        }

        private async void RenameInstance_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is InstanceMetadata metadata)
            {
                var dialog = new Wpf.Ui.Controls.MessageBox
                {
                    Title = "Rename Server",
                    PrimaryButtonText = "Save",
                    CloseButtonText = "Cancel"
                };

                var stackPanel = new StackPanel();
                var txtName = new Wpf.Ui.Controls.TextBox { Text = metadata.Name };
                var txtDesc = new Wpf.Ui.Controls.TextBox { Text = metadata.Description, Margin = new Thickness(0, 10, 0, 0) };
                stackPanel.Children.Add(txtName);
                stackPanel.Children.Add(txtDesc);
                dialog.Content = stackPanel;

                var result = await dialog.ShowDialogAsync();
                if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                {
                    if (!string.IsNullOrWhiteSpace(txtName.Text))
                    {
                        var folder = FindFolderById(metadata.Id);
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
}
