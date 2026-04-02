using System;
using System.Windows;
using Microsoft.Win32;
using PocketMC.Desktop.Services;
using PocketMC.Desktop.Views;

namespace PocketMC.Desktop
{
    public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
    {
        public MainWindow()
        {
            InitializeComponent();
            Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(this);
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Kill all managed server processes on app close
            ServerProcessManager.KillAll();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var settingsManager = new SettingsManager();
            var settings = settingsManager.Load();

            if (string.IsNullOrEmpty(settings.AppRootPath))
            {
                var dialog = new OpenFolderDialog()
                {
                    Title = "Select First-Run Root Folder for PocketMC",
                    Multiselect = false
                };

                if (dialog.ShowDialog() == true)
                {
                    settings.AppRootPath = dialog.FolderName;
                    settingsManager.Save(settings);
                }
                else
                {
                    Application.Current.Shutdown();
                    return;
                }
            }

            RootFrame.Navigate(new JavaSetupPage(settings.AppRootPath));
        }
    }
}