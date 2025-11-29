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
        internal const string Name = "Gaming Chair - EFT DMA RADAR";
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
                // Initialize Velopack self-updater. The update source is configured via packaging (artifacts feed).
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
                
                // Check for updates before showing loading window
                await CheckForVelopackUpdatesAsync();
                
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
            await loadingWindow.ViewModel.UpdateProgressAsync(100, "Loading Completed!");
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        /// <summary>
        /// Check for updates using Velopack with GitHub Releases as source.
        /// </summary>
        private async Task CheckForVelopackUpdatesAsync()
        {
            try
            {
                // Check if we're running in a Velopack-installed context
                var updateManager = new UpdateManager(
                    new GithubSource("https://github.com/Lum0s36/EFT-DMA-Radar", null, false)
                );

                // Check for updates
                var updateInfo = await updateManager.CheckForUpdatesAsync();
                
                if (updateInfo == null)
                {
                    // No updates available
                    return;
                }

                // Get current version
                var currentVersion = updateManager.CurrentVersion?.ToString() ?? "Unknown";
                var newVersion = updateInfo.TargetFullRelease.Version.ToString();

                // Show update prompt
                var result = MessageBox.Show(
                    $"A new version is available!\n\n" +
                    $"Current Version: {currentVersion}\n" +
                    $"New Version: {newVersion}\n\n" +
                    $"Would you like to download and install the update now?\n" +
                    $"The application will restart after the update.",
                    "Update Available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );

                if (result == MessageBoxResult.Yes)
                {
                    // Download and apply update
                    await updateManager.DownloadUpdatesAsync(updateInfo);
                    
                    MessageBox.Show(
                        "Update downloaded successfully!\n" +
                        "The application will now restart to complete the installation.",
                        "Update Ready",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                    
                    // Apply updates and restart
                    updateManager.ApplyUpdatesAndRestart(updateInfo);
                }
            }
            catch (Exception ex)
            {
                // Don't block startup if update check fails
                DebugLogger.LogDebug($"[Velopack] Update check failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Check system dependencies (optional - can be expanded).
        /// </summary>
        private bool CheckDependencies()
        {
            try
            {
                // Check .NET 9 Runtime
                var runtimeVersion = Environment.Version;
                if (runtimeVersion.Major < 9)
                {
                    MessageBox.Show(
                        $"This application requires .NET 9 or higher.\n" +
                        $"Current version: {runtimeVersion}\n\n" +
                        $"Please install .NET 9 Runtime from:\n" +
                        $"https://dotnet.microsoft.com/download/dotnet/9.0",
                        "Missing Dependency",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    return false;
                }

                // Add more dependency checks here if needed
                // (Visual C++ Redistributables, DirectX, etc.)

                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[Dependencies] Check failed: {ex}");
                return true; // Don't block if check fails
            }
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Config.Save();
        }

        /// <summary>
        /// Sets up the Dependency Injection container for the application.
        /// </summary>
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

        [LibraryImport("kernel32.dll")]
        private static partial EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

        [Flags]
        public enum EXECUTION_STATE : uint
        {
            ES_AWAYMODE_REQUIRED = 0x00000040,
            ES_CONTINUOUS = 0x80000000,
            ES_DISPLAY_REQUIRED = 0x00000002,
            ES_SYSTEM_REQUIRED = 0x00000001
        }

        [LibraryImport("powrprof.dll")]
        private static partial uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

        [LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static partial uint TimeBeginPeriod(uint uMilliseconds);

        #endregion
    }
}
