using System;
using System.Threading.Tasks;
using UnityEngine;
using Velopack;
using Velopack.Sources;

public class VelopackUpdater : MonoBehaviour
{
    [Tooltip("The full URL to your public GitHub repository")]
    public string githubRepoUrl = "https://github.com/rayanMELZI/3D-game-base";

    void Start()
    {
        // Run the update process asynchronously so it doesn't freeze the game
        _ = CheckAndApplyUpdates();
    }

    private async Task CheckAndApplyUpdates()
    {
        // Velopack only works in the compiled build, not the Unity Editor
#if !UNITY_EDITOR
        try
        {
            // Connect to your GitHub repository's releases
            var source = new GithubSource(githubRepoUrl, null, false);
            using var mgr = new UpdateManager(source);

            // Check if there is a newer version available on GitHub
            var newVersion = await mgr.CheckForUpdatesAsync();
            if (newVersion == null)
            {
                Debug.Log("Game is up to date.");
                return; 
            }

            Debug.Log($"Update {newVersion.TargetFullRelease.Version} found. Downloading...");
            
            // Download the delta/full update
            await mgr.DownloadUpdatesAsync(newVersion);

            Debug.Log("Update downloaded. Restarting game...");

            // Closes the game, swaps the files in the background, and restarts it
            mgr.ApplyUpdatesAndRestart(newVersion);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Velopack update failed: {ex.Message}");
        }
#else
        Debug.Log("Velopack updates are disabled in the Unity Editor.");
        await Task.CompletedTask;
#endif
    }
}