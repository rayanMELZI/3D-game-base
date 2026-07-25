using System;
using System.Threading.Tasks;
using UnityEngine;
using Velopack;
using Velopack.Sources;

public class VelopackUpdater : MonoBehaviour
{
    [Tooltip("The full URL to your public GitHub repository")]
    public string githubRepoUrl = "https://github.com/rayanMELZI/3D-game-base";

    // 1. Automatically runs right when the game starts before scene objects load
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeVelopack()
    {
#if !UNITY_EDITOR
        try
        {
            // Initializes VelopackLocator and processes installer hooks
            VelopackApp.Build().Run();
            Debug.Log("[Velopack] Initialized successfully.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Velopack] Initialization failed: {ex.Message}");
        }
#endif
    }

    void Start()
    {
        _ = CheckAndApplyUpdates();
    }

    private async Task CheckAndApplyUpdates()
    {
#if !UNITY_EDITOR
        try
        {
            var source = new GithubSource(githubRepoUrl, null, false);
            var mgr = new UpdateManager(source);

            Debug.Log($"[Velopack] Is Installed: {mgr.IsInstalled}");
            Debug.Log($"[Velopack] Current Version: {mgr.CurrentVersion}");

            if (!mgr.IsInstalled)
            {
                Debug.LogWarning("[Velopack] Application is running directly from source/uninstalled folder. Skipping auto-update.");
                return;
            }

            var newVersion = await mgr.CheckForUpdatesAsync();
            if (newVersion == null)
            {
                Debug.Log("[Velopack] Game is up to date.");
                return; 
            }

            Debug.Log($"[Velopack] Update {newVersion.TargetFullRelease.Version} found. Downloading...");
            await mgr.DownloadUpdatesAsync(newVersion);

            Debug.Log("[Velopack] Update downloaded. Restarting game...");
            mgr.ApplyUpdatesAndRestart(newVersion);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Velopack] Update check failed: {ex.Message}");
        }
#else
        Debug.Log("[Velopack] Updates are disabled in the Unity Editor.");
        await Task.CompletedTask;
#endif
    }
}