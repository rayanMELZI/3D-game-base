using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// A teleport pad (trigger). When a player steps on it, they are moved to the
    /// linked pad. Only the local owner teleports themselves (netcode replicates
    /// the resulting position); a short cooldown on both pads prevents instantly
    /// bouncing back. Used by the P Story island's teleporter.
    /// </summary>
    public class TeleportPad : MonoBehaviour
    {
        public TeleportPad linked;
        private float readyAt;

        /// <summary>Block this pad from firing for a moment (set on arrival).</summary>
        public void Suppress(float seconds) => readyAt = Time.time + seconds;

        private void OnTriggerEnter(Collider other)
        {
            if (linked == null || Time.time < readyAt)
                return;

            var controller = other.GetComponentInParent<CharacterController>();
            if (controller == null)
                return;

            // In multiplayer only the owner moves itself; others get the position
            // through the networked transform.
            var netPlayer = controller.GetComponent<NetworkPlayer>();
            if (netPlayer != null && !netPlayer.IsOwner)
                return;

            // Don't let the destination immediately teleport them back.
            readyAt = Time.time + 1.5f;
            linked.Suppress(1.5f);

            Vector3 target = linked.transform.position + Vector3.up * 1.2f;
            controller.enabled = false; // CharacterController overrides transform writes
            controller.transform.position = target;
            controller.enabled = true;

            SfxSynth.PlayAt(SfxSynth.UiClick(), target, 0.7f);
        }
    }
}
