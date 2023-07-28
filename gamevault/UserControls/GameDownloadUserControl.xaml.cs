﻿using gamevault.Helper;
using gamevault.Models;
using gamevault.ViewModels;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace gamevault.UserControls
{
    /// <summary>
    /// Interaction logic for GameInstallUserControl.xaml
    /// </summary>
    public partial class GameDownloadUserControl : UserControl
    {
        private GameDownloadViewModel ViewModel { get; set; }
        private bool IsDownloadActive = false;
        private string m_DownloadPath { get; set; }
        string m_InstallPath { get; set; }
        private HttpClientDownloadWithProgress client { get; set; }
        private DateTime startTime;

        private SevenZipHelper sevenZipHelper { get; set; }
        public GameDownloadUserControl(Game game, bool download)
        {
            InitializeComponent();
            ViewModel = new GameDownloadViewModel();
            this.DataContext = ViewModel;
            ViewModel.Game = game;
            ViewModel.DownloadUIVisibility = System.Windows.Visibility.Hidden;
            m_DownloadPath = $"{SettingsViewModel.Instance.RootPath}\\GameVault\\Downloads\\({ViewModel.Game.ID}){ViewModel.Game.Title}";
            m_DownloadPath = m_DownloadPath.Replace(@"\\", @"\");
            m_InstallPath = $"{SettingsViewModel.Instance.RootPath}\\GameVault\\Installations\\({ViewModel.Game.ID}){ViewModel.Game.Title}";
            sevenZipHelper = new SevenZipHelper();
            if (download)
            {
                Task.Run(async () =>
                {
                    IsDownloadActive = true;
                    ViewModel.State = "Downloading...";
                    ViewModel.DownloadUIVisibility = System.Windows.Visibility.Visible;
                    await DownloadGame();
                    await CacheHelper.CreateOfflineCacheAsync(ViewModel.Game);
                });
            }
            else
            {
                if (File.Exists($"{m_DownloadPath}\\Extract\\gamevault-metadata") && Preferences.Get(AppConfigKey.ExtractionFinished, $"{m_DownloadPath}\\Extract\\gamevault-metadata") == "1")
                {
                    ViewModel.State = "Extracted";
                }
                else
                {
                    ViewModel.State = "Downloaded";
                }
            }
        }
        public bool IsDownloading()
        {
            return IsDownloadActive;
        }
        public bool IsGameIdDownloading(int id)
        {
            if (IsDownloadActive == true && ViewModel.Game.ID == id)
            {
                return true;
            }
            return false;
        }
        public int GetGameId()
        {
            return ViewModel.Game.ID;
        }
        public int GetBoxImageID()
        {
            return ViewModel.Game.BoxImage.ID;
        }
        public void CancelDownload()
        {
            if (client == null)
                return;

            client.Cancel();
            client.Dispose();
            IsDownloadActive = false;
            ViewModel.State = "Cancelled";
            ViewModel.DownloadUIVisibility = System.Windows.Visibility.Hidden;
        }
        private async Task DownloadGame()
        {

            if (!Directory.Exists(m_DownloadPath)) { Directory.CreateDirectory(m_DownloadPath); }
            client = new HttpClientDownloadWithProgress($"{SettingsViewModel.Instance.ServerUrl}/api/v1/games/{ViewModel.Game.ID}/download", $"{m_DownloadPath}\\{Path.GetFileName(ViewModel.Game.FilePath)}");
            client.ProgressChanged += DownloadProgress;
            startTime = DateTime.Now;

            try
            {
                await client.StartDownload();
            }
            catch (Exception ex)
            {
                client.Dispose();
                IsDownloadActive = false;
                ViewModel.State = $"Error: '{ex.Message}'";
                ViewModel.DownloadUIVisibility = System.Windows.Visibility.Hidden;
            }
        }
        private void DownloadProgress(long? totalFileSize, long totalBytesDownloaded, double? progressPercentage)
        {
            App.Current.Dispatcher.Invoke((Action)delegate
            {
                ViewModel.TotalBytesDownloaded = totalBytesDownloaded;
                ViewModel.DownloadRate = $"Download Speed: {CalculateSpeed(totalBytesDownloaded, (DateTime.Now - startTime).TotalSeconds)}";
                ViewModel.TimeLeft = CalculateTimeLeft(totalFileSize, totalBytesDownloaded, (DateTime.Now - startTime).TotalSeconds);
                if (ViewModel.GameDownloadProgress != (int)progressPercentage)
                {
                    ViewModel.GameDownloadProgress = (int)progressPercentage;
                    if (ViewModel.GameDownloadProgress == 100)
                    {
                        DownloadCompleted();
                    }
                }
            });
        }

        private void DownloadCompleted()
        {
            client.Dispose();
            IsDownloadActive = false;
            ViewModel.State = "Downloaded";
            ViewModel.DownloadUIVisibility = System.Windows.Visibility.Hidden;
            if (!Directory.Exists(m_InstallPath))
            {
                Directory.CreateDirectory(m_InstallPath);
            }
            MainWindowViewModel.Instance.Installs.AddSystemFileWatcher(m_InstallPath);
        }

        private void CancelDownload_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (IsDownloadActive == true)
            {
                CancelDownload();
            }
            else
            {
                sevenZipHelper.Cancel();
            }
        }
        private string CalculateSpeed(double size, double tspan)
        {
            string message = string.Empty;
            if (size / tspan > 1024 * 1024) // MB
            {
                return $"{message} {Math.Round(size / (1024 * 1204) / tspan, 2)} MB/s"; //string.Format(message, size / (1024 * 1204) / tspan, "MB/s");
            }
            else if (size / tspan > 1024) // KB
            {
                return string.Format(message, size / (1024) / tspan, "KB/s");
            }
            else
            {
                return string.Format(message, size / tspan, "B/s");
            }
        }

        private string CalculateTimeLeft(long? totalFileSize, long totalBytesDownloaded, double tspan)
        {
            var averagespeed = totalBytesDownloaded / tspan;
            var timeleft = (totalFileSize / averagespeed) - (tspan);
            TimeSpan t = TimeSpan.FromSeconds(0);
            if (!double.IsInfinity(Convert.ToDouble(timeleft)))
            {
                t = TimeSpan.FromSeconds(Convert.ToInt32(timeleft));
            }
            return string.Format("{0:00}:{1:00}:{2:00}", ((int)t.TotalHours), t.Minutes, t.Seconds);
        }

        private async void DeleteFile_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            MessageDialogResult result = await ((MetroWindow)App.Current.MainWindow).ShowMessageAsync($"Are you sure you want to delete '{ViewModel.Game.Title}' ?", "", MessageDialogStyle.AffirmativeAndNegative, new MetroDialogSettings() { AffirmativeButtonText = "Yes", NegativeButtonText = "No", AnimateHide = false });
            if (result == MessageDialogResult.Affirmative)
            {
                if (Directory.Exists(m_DownloadPath))
                    Directory.Delete(m_DownloadPath, true);

                DownloadsViewModel.Instance.DownloadedGames.Remove(this);
            }
        }
        private void OpenDirectory_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (Directory.Exists(m_DownloadPath))
                Process.Start("explorer.exe", m_DownloadPath);
        }

        private void GameImage_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            MainWindowViewModel.Instance.SetActiveControl(new GameViewUserControl(ViewModel.Game, LoginManager.Instance.IsLoggedIn()));
        }

        private async void Extract_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ViewModel.State = "Extracting...";
            ViewModel.DownloadUIVisibility = System.Windows.Visibility.Visible;


            sevenZipHelper.Process += ExtractionProgress;
            startTime = DateTime.Now;
            int result = await sevenZipHelper.ExtractArchive($"{m_DownloadPath}\\{Path.GetFileName(ViewModel.Game.FilePath)}", $"{m_DownloadPath}\\Extract");
            if (result == 0)
            {
                if (!File.Exists($"{m_DownloadPath}\\Extract\\gamevault-metadata"))
                {
                    File.Create($"{m_DownloadPath}\\Extract\\gamevault-metadata").Close();
                }
                Preferences.Set(AppConfigKey.ExtractionFinished, "1", $"{m_DownloadPath}\\Extract\\gamevault-metadata");
                ViewModel.State = "Extracted";
                ViewModel.DownloadUIVisibility = System.Windows.Visibility.Hidden;
            }
            else
            {
                if (Directory.Exists($"{m_DownloadPath}\\Extract"))
                {
                    Directory.Delete($"{m_DownloadPath}\\Extract", true);
                }
                ViewModel.State = "Something went wrong during extraction";
                ViewModel.DownloadUIVisibility = System.Windows.Visibility.Hidden;
            }
        }

        private void ExtractionProgress(object sender, SevenZipProgressEventArgs e)
        {
            long totalBytesDownloaded = (Convert.ToInt64(ViewModel.Game.Size) / 100) * e.PercentageDone;
            ViewModel.DownloadRate = $"Extraction Speed: {CalculateSpeed(totalBytesDownloaded, (DateTime.Now - startTime).TotalSeconds)}";
            ViewModel.TotalBytesDownloaded = totalBytesDownloaded;
            ViewModel.TimeLeft = CalculateTimeLeft(Convert.ToInt64(ViewModel.Game.Size), totalBytesDownloaded, (DateTime.Now - startTime).TotalSeconds);
            ViewModel.GameDownloadProgress = e.PercentageDone;
        }
    }
}
