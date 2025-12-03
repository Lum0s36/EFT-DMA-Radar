/*
 * Auto-Update Manager using Squirrel.Windows
 * 
 * Checks for updates on GitHub Releases and applies them automatically
 */

using Squirrel;
using System.Threading.Tasks;

namespace LoneEftDmaRadar.Misc
{
    /// <summary>
    /// Manages automatic updates via Squirrel.Windows
    /// </summary>
    public static class UpdateManager
    {
        private const string GITHUB_REPO_URL = "https://github.com/Lum0s36/EFT-DMA-Radar";
        
        /// <summary>
        /// Check for updates and install them if available
        /// </summary>
        public static async Task CheckForUpdatesAsync()
        {
            try
            {
                using var mgr = new Squirrel.UpdateManager(GITHUB_REPO_URL);
                
                // Check for updates
                var updateInfo = await mgr.CheckForUpdate();
                
                if (updateInfo.ReleasesToApply.Count > 0)
                {
                    DebugLogger.LogDebug($"[UpdateManager] Found {updateInfo.ReleasesToApply.Count} update(s)");
                    
                    // Download and apply updates
                    var newVersion = await mgr.UpdateApp();
                    
                    if (newVersion != null)
                    {
                        DebugLogger.LogDebug($"[UpdateManager] Updated to version {newVersion}");
                        
                        // Notify user
                        var result = MessageBox.Show(
                            $"Update to version {newVersion} has been downloaded.\n\n" +
                            "The application will restart to complete the update.",
                            "Update Available",
                            MessageBoxButton.OKCancel,
                            MessageBoxImage.Information);
                        
                        if (result == MessageBoxResult.OK)
                        {
                            // Restart application
                            Squirrel.UpdateManager.RestartApp();
                        }
                    }
                }
                else
                {
                    DebugLogger.LogDebug("[UpdateManager] No updates available");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[UpdateManager] Error checking for updates: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Handle Squirrel events (install/uninstall shortcuts, etc.)
        /// Call this in App.xaml.cs during startup
        /// </summary>
        public static void HandleSquirrelEvents()
        {
            try
            {
                SquirrelAwareApp.HandleEvents(
                    onInitialInstall: OnAppInstall,
                    onAppUninstall: OnAppUninstall,
                    onEveryRun: OnAppRun);
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[UpdateManager] Error handling Squirrel events: {ex.Message}");
            }
        }
        
        private static void OnAppInstall(SemanticVersion version, IAppTools tools)
        {
            tools.CreateShortcutForThisExe(ShortcutLocation.StartMenu | ShortcutLocation.Desktop);
        }
        
        private static void OnAppUninstall(SemanticVersion version, IAppTools tools)
        {
            tools.RemoveShortcutForThisExe(ShortcutLocation.StartMenu | ShortcutLocation.Desktop);
        }
        
        private static void OnAppRun(SemanticVersion version, IAppTools tools, bool firstRun)
        {
            tools.SetProcessAppUserModelId();
        }
    }
}
