/*
 * Lone EFT DMA Radar
 * Brought to you by Lone (Lone DMA)
 * 
MIT License

Copyright (c) 2025 Lone DMA

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
 *
*/

global using SDK;
global using SkiaSharp;
global using SkiaSharp.Views.Desktop;
global using System.Buffers;
global using System.Collections;
global using System.Collections.Concurrent;
global using System.ComponentModel;
global using System.Data;
global using System.Diagnostics;
global using System.IO;
global using System.Net;
global using System.Numerics;
global using System.Reflection;
global using System.Runtime.CompilerServices;
global using System.Runtime.InteropServices;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using System.Windows;
using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Misc.Services;
using LoneEftDmaRadar.Tarkov;
using LoneEftDmaRadar.UI.ColorPicker;
using LoneEftDmaRadar.UI.Misc;
using LoneEftDmaRadar.UI.Radar.Maps;
using LoneEftDmaRadar.UI.Skia;
using LoneEftDmaRadar.UI.ESP;
using LoneEftDmaRadar.Web.EftApiTech;
using LoneEftDmaRadar.Web.TarkovDev.Data;
using LoneEftDmaRadar.Web.TarkovDev.Profiles;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using Velopack;
using Velopack.Sources;

namespace LoneEftDmaRadar
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        internal const string Name = "Moulman EFT DMA Radar";
        private const string MUTEX_ID = "0f908ff7-e614-6a93-60a3-cee36c9cea91";
        private static readonly Mutex _mutex;

        /// <summary>
        /// Path to the Configuration Folder in %AppData%
        /// </summary>
        public static DirectoryInfo ConfigPath { get; } =
            new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lone-EFT-DMA"));
        /// <summary>
        /// Global Program Configuration.
        /// </summary>
        public static EftDmaConfig Config { get; }
        /// <summary>
        /// Service Provider for Dependency Injection.
        /// NOTE: Web Radar has it's own container.
        /// </summary>
        public static IServiceProvider ServiceProvider { get; }
        /// <summary>
        /// HttpClientFactory for creating HttpClients.
        /// </summary>
        public static IHttpClientFactory HttpClientFactory { get; }
        /// <summary>
        /// TRUE if the application is currently using Dark Mode resources, otherwise FALSE for Light Mode.
        /// </summary>
        public static bool IsDarkMode { get; private set; }

        static App()
        {
            try
            {
                VelopackApp.Build().Run();
                _mutex = new Mutex(true, MUTEX_ID, out bool singleton);
                if (!singleton)
                    throw new InvalidOperationException("The application is already running.");
                Config = EftDmaConfig.Load();
                ServiceProvider = BuildServiceProvider();
                HttpClientFactory = ServiceProvider.GetRequiredService<IHttpClientFactory>();
                SetHighPerformanceMode();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), Name, MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }
        
        protected override async void OnStartup(StartupEventArgs e)
        {
            try
            {
                base.OnStartup(e);
                using var loading = new LoadingWindow();
                await ConfigureProgramAsync(loadingWindow: loading);

                //DebugLogger.Toggle(); // Auto-open debug console

                MainWindow = new MainWindow();
                MainWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), Name, MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                ESPManager.CloseESP();
                Config.Save();
            }
            finally
            {
                base.OnExit(e);
            }
        }

        #region Boilerplate

        /// <summary>
        /// Configure Program Startup.
        /// </summary>
        private async Task ConfigureProgramAsync(LoadingWindow loadingWindow)
        {
            await loadingWindow.ViewModel.UpdateProgressAsync(15, "Loading, Please Wait...");
            //_ = Task.Run(CheckForUpdatesAsync); // Run continuations on the thread pool
            var tarkovDataManager = TarkovDataManager.ModuleInitAsync();
            var eftMapManager = EftMapManager.ModuleInitAsync();
            var memoryInterface = MemoryInterface.ModuleInitAsync();
            var misc = Task.Run(() =>
            {
                IsDarkMode = GetIsDarkMode();
                if (IsDarkMode)
                {
                    SKPaints.PaintBitmap.ColorFilter = SKPaints.GetDarkModeColorFilter(0.7f);
                    SKPaints.PaintBitmapAlpha.ColorFilter = SKPaints.GetDarkModeColorFilter(0.7f);
                }
                RuntimeHelpers.RunClassConstructor(typeof(LocalCache).TypeHandle);
                RuntimeHelpers.RunClassConstructor(typeof(ColorPickerViewModel).TypeHandle);
            });
            await Task.WhenAll(tarkovDataManager, eftMapManager, memoryInterface, misc);
            await CheckForUpdatesGithubAsync();
            await loadingWindow.ViewModel.UpdateProgressAsync(100, "Loading Completed!");
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Config.Save();
        }

        /// <summary>
        /// Sets up the Dependency Injection container for the application.
        /// </summary>
        /// <returns></returns>
        private static IServiceProvider BuildServiceProvider()
        {
            var services = new ServiceCollection();
            services.AddHttpClient(); // Add default HttpClientFactory
            TarkovDevGraphQLApi.Configure(services);
            TarkovDevProfileProvider.Configure(services);
            EftApiTechProvider.Configure(services);
            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Sets High Performance mode in Windows Power Plans and Process Priority.
        /// </summary>
        private static void SetHighPerformanceMode()
        {
            /// Prepare Process for High Performance Mode
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_SYSTEM_REQUIRED |
                                           EXECUTION_STATE.ES_DISPLAY_REQUIRED);
            var highPerformanceGuid = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
            if (PowerSetActiveScheme(IntPtr.Zero, ref highPerformanceGuid) != 0)
                DebugLogger.LogDebug("WARNING: Unable to set High Performance Power Plan");
            const uint timerResolutionMs = 5;
            if (TimeBeginPeriod(timerResolutionMs) != 0)
                DebugLogger.LogDebug($"WARNING: Unable to set timer resolution to {timerResolutionMs}ms. This may cause performance issues.");
        }

        /// <summary>
        /// Checks the current ResourceDictionaries to determine if Dark Mode or Light Mode is active.
        /// NOTE: Only works after App is initialized and resources are loaded.
        /// </summary>
        private static bool GetIsDarkMode()
        {
            // Force dark mode resources regardless of detected theme.
            return true;
        }

        private static async Task CheckForUpdatesAsync()
        {
            //try
            //{
            //    var updater = new UpdateManager(
            //        source: new GithubSource(
            //            repoUrl: "https://github.com/lone-dma/Lone-EFT-DMA-Radar",
            //            accessToken: null,
            //            prerelease: false));
            //    if (!updater.IsInstalled)
            //        return;

            //    var newVersion = await updater.CheckForUpdatesAsync();
            //    if (newVersion is not null)
            //    {
            //        var result = MessageBox.Show(
            //            messageBoxText: $"A new version ({newVersion.TargetFullRelease.Version}) is available.\n\nWould you like to update now?",
            //            caption: App.Name,
            //            button: MessageBoxButton.YesNo,
            //            icon: MessageBoxImage.Question,
            //            defaultResult: MessageBoxResult.Yes,
            //            options: MessageBoxOptions.DefaultDesktopOnly);

            //        if (result == MessageBoxResult.Yes)
            //        {
            //            await updater.DownloadUpdatesAsync(newVersion);
            //            updater.ApplyUpdatesAndRestart(newVersion);
            //        }
            //    }
            //}
            //catch (Exception ex)
            //{
            //    MessageBox.Show(
            //        messageBoxText: $"An unhandled exception occurred while checking for updates: {ex}",
            //        caption: App.Name,
            //        button: MessageBoxButton.OK,
            //        icon: MessageBoxImage.Warning,
            //        defaultResult: MessageBoxResult.OK,
            //        options: MessageBoxOptions.DefaultDesktopOnly);
            //}
        }

        private static async Task CheckForUpdatesGithubAsync()
        {
            try
            {
                using var client = App.HttpClientFactory.CreateClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("EFT-DMA-Radar/1.0");
                var owner = "Lum0s36";
                var repo = "EFT-DMA-Radar";
                using var resp = await client.GetAsync($"https://api.github.com/repos/{owner}/{repo}/releases/latest");
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("tag_name", out var tagProp))
                    return;
                var latestTag = tagProp.GetString(); // e.g. v1.2.3
                if (string.IsNullOrWhiteSpace(latestTag)) return;

                var latest = NormalizeSemver(latestTag);
                var current = NormalizeSemver(GetCurrentVersionString());
                if (latest is null || current is null) return;

                if (IsNewer(latest.Value, current.Value))
                {
                    var result = MessageBox.Show(
                        messageBoxText: $"Version {current} ist veraltet. Neue Version {latest} verfügbar. Jetzt laden?",
                        caption: App.Name,
                        button: MessageBoxButton.YesNo,
                        icon: MessageBoxImage.Information);
                    if (result == MessageBoxResult.Yes)
                    {
                        // Open releases page for download
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = $"https://github.com/{owner}/{repo}/releases/latest",
                            UseShellExecute = true
                        });
                    }
                }
            }
            catch { /* ignore network errors */ }

            static (int major,int minor,int patch)? NormalizeSemver(string v)
            {
                if (string.IsNullOrWhiteSpace(v)) return null;
                if (v.StartsWith('v') || v.StartsWith('V')) v = v[1..];
                var parts = v.Split('.');
                if (parts.Length < 3) return null;
                if (int.TryParse(parts[0], out var maj) && int.TryParse(parts[1], out var min) && int.TryParse(parts[2], out var pat))
                    return (maj,min,pat);
                return null;
            }
            static string GetCurrentVersionString()
            {
                var asm = Assembly.GetExecutingAssembly();
                var v = asm.GetName().Version;
                if (v is null) return "0.0.0";
                return $"{v.Major}.{v.Minor}.{v.Build}";
            }
            static bool IsNewer((int major,int minor,int patch) a, (int major,int minor,int patch) b)
            {
                if (a.major != b.major) return a.major > b.major;
                if (a.minor != b.minor) return a.minor > b.minor;
                return a.patch > b.patch;
            }
        }

        [LibraryImport("kernel32.dll")]
        private static partial EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

        [Flags]
        public enum EXECUTION_STATE : uint
        {
            ES_AWAYMODE_REQUIRED = 0x00000040,
            ES_CONTINUOUS = 0x80000000,
            ES_DISPLAY_REQUIRED = 0x00000002,
            ES_SYSTEM_REQUIRED = 0x00000001
            // Legacy flag, should not be used.
            // ES_USER_PRESENT = 0x00000004
        }

        [LibraryImport("powrprof.dll")]
        private static partial uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

        [LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static partial uint TimeBeginPeriod(uint uMilliseconds);

        #endregion
    }
}
