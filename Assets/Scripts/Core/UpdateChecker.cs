using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace FpsBase
{
    /// <summary>
    /// Lightweight update notice: fetches a version string from
    /// GameSettings.UpdateCheckUrl (e.g. a raw GitHub version.txt) and the
    /// menu shows "update available" when it differs from the local version.
    /// Actual patching is handled by the distribution platform (see README:
    /// Steam depots / itch.io butler push binary diffs, so players only
    /// download what changed instead of the whole build).
    /// </summary>
    public class UpdateChecker : MonoBehaviour
    {
        public static string LatestVersion { get; private set; }
        public static bool UpdateAvailable =>
            !string.IsNullOrEmpty(LatestVersion) && LatestVersion != GameSettings.Version;

        private static bool started;

        public static void EnsureStarted()
        {
            if (started)
                return;
            started = true;
            if (string.IsNullOrEmpty(GameSettings.UpdateCheckUrl))
                return; // not configured
            var go = new GameObject("UpdateChecker");
            DontDestroyOnLoad(go);
            go.AddComponent<UpdateChecker>();
        }

        private IEnumerator Start()
        {
            using (var request = UnityWebRequest.Get(GameSettings.UpdateCheckUrl))
            {
                request.timeout = 8;
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    string text = request.downloadHandler.text.Trim();
                    if (text.Length > 0 && text.Length < 40)
                        LatestVersion = text;
                }
            }
            Destroy(gameObject);
        }
    }
}
