using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Marks a spawn location in a custom map prefab. Drop these into your map
    /// (Tools > FPS Base > New Map creates some for you) and set the team:
    ///   0 = blue side, 1 = orange side, -1 = any (used by FFA / Gun Game).
    /// The game reads every SpawnPoint's world position and picks the safest one.
    /// </summary>
    public class SpawnPoint : MonoBehaviour
    {
        [Tooltip("0 = blue team side, 1 = orange team side, -1 = usable by anyone.")]
        public int team = -1;

        // Draw a colored marker in the Scene view so spawns are easy to place.
        private void OnDrawGizmos()
        {
            Gizmos.color = team == 0 ? new Color(0.25f, 0.5f, 1f)
                : team == 1 ? new Color(1f, 0.55f, 0.15f)
                : new Color(0.4f, 0.9f, 0.5f);
            Vector3 p = transform.position;
            Gizmos.DrawWireSphere(p + Vector3.up * 0.9f, 0.4f);
            Gizmos.DrawLine(p, p + Vector3.up * 1.8f);
            Gizmos.DrawRay(p + Vector3.up * 1.8f, transform.forward * 0.8f); // facing hint
        }
    }
}
