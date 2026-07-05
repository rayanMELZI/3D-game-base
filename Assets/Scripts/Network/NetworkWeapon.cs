using Unity.Netcode;
using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Replicates weapon state to other players: which weapon is held (shown as
    /// a third-person model on the body) and shot effects (tracers), so everyone
    /// sees everyone shooting. Damage itself goes through NetworkHealth.
    /// </summary>
    [RequireComponent(typeof(PlayerRigRefs))]
    public class NetworkWeapon : NetworkBehaviour
    {
        public NetworkVariable<int> WeaponIndex = new NetworkVariable<int>(
            1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private PlayerRigRefs rig;
        private WeaponModelInstance thirdPersonModel;
        private bool hasThirdPersonModel;
        private bool visible = true;

        private void Awake()
        {
            rig = GetComponent<PlayerRigRefs>();
        }

        public override void OnNetworkSpawn()
        {
            WeaponIndex.OnValueChanged += OnWeaponChanged;
            RebuildThirdPersonModel();

            if (IsOwner)
                rig.weaponController.ShotFired += OnLocalShotFired;
        }

        public override void OnNetworkDespawn()
        {
            WeaponIndex.OnValueChanged -= OnWeaponChanged;
            if (IsOwner && rig.weaponController != null)
                rig.weaponController.ShotFired -= OnLocalShotFired;
        }

        private void Update()
        {
            // Owner publishes its current weapon selection.
            if (IsSpawned && IsOwner && WeaponIndex.Value != rig.weaponController.CurrentIndex)
                WeaponIndex.Value = rig.weaponController.CurrentIndex;
        }

        // ------------------------------------------------------------------
        // Third-person weapon model (what other players see)
        // ------------------------------------------------------------------

        private void OnWeaponChanged(int previous, int next) => RebuildThirdPersonModel();

        private void RebuildThirdPersonModel()
        {
            if (hasThirdPersonModel)
            {
                Destroy(thirdPersonModel.root);
                hasThirdPersonModel = false;
            }

            if (IsOwner)
                return; // the owner sees the first-person viewmodel instead

            var weapons = rig.weaponController.weapons;
            int index = Mathf.Clamp(WeaponIndex.Value, 0, weapons.Length - 1);
            thirdPersonModel = WeaponModelBuilder.Build(
                weapons[index], rig.thirdPersonWeaponAnchor, Vector3.zero, castShadows: true);
            hasThirdPersonModel = true;
            thirdPersonModel.root.SetActive(visible);
        }

        /// <summary>Hidden while the player is dead (called by NetworkPlayer).</summary>
        public void SetThirdPersonVisible(bool value)
        {
            visible = value;
            if (hasThirdPersonModel)
                thirdPersonModel.root.SetActive(value);
        }

        // ------------------------------------------------------------------
        // Shot effect replication
        // ------------------------------------------------------------------

        private void OnLocalShotFired(Vector3 endPoint) => FireServerRpc(endPoint);

        [ServerRpc]
        private void FireServerRpc(Vector3 endPoint)
        {
            FireClientRpc(endPoint);
        }

        [ClientRpc]
        private void FireClientRpc(Vector3 endPoint)
        {
            if (IsOwner)
                return; // the owner already played its local effects

            Vector3 from = hasThirdPersonModel
                ? thirdPersonModel.muzzle.position
                : transform.position + Vector3.up * 1.4f;
            WeaponController.SpawnTracerLine(from, endPoint);

            // Positional gunshot so you can hear where enemies are firing from.
            var weapons = rig.weaponController.weapons;
            int index = Mathf.Clamp(WeaponIndex.Value, 0, weapons.Length - 1);
            SfxSynth.PlayAt(SfxSynth.Shot(weapons[index].model), from, 0.8f);
        }
    }
}
