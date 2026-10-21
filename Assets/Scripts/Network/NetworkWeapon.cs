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
            WeaponDefinition.DefaultIndex, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>Add-on bitmask of the currently held weapon, so others see it and a
        /// suppressor keeps you off their radar. See AttachmentType.</summary>
        public NetworkVariable<int> AttachmentMask = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<int> ColorIndex = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>Time.time of this player's last shot on THIS client (radar "on fire" mode).</summary>
        public float LastShotTime { get; private set; } = -999f;

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
            AttachmentMask.OnValueChanged += OnWeaponChanged;
            ColorIndex.OnValueChanged += OnWeaponChanged;
            RebuildThirdPersonModel();

            if (IsOwner)
            {
                rig.weaponController.ShotFired += OnLocalShotFired;
                rig.weaponController.GrenadeLaunched += OnLocalGrenadeLaunched;
                rig.weaponController.ReloadCuePlayed += OnLocalReloadCue;
            }
        }

        public override void OnNetworkDespawn()
        {
            WeaponIndex.OnValueChanged -= OnWeaponChanged;
            AttachmentMask.OnValueChanged -= OnWeaponChanged;
            ColorIndex.OnValueChanged -= OnWeaponChanged;
            if (IsOwner && rig.weaponController != null)
            {
                rig.weaponController.ShotFired -= OnLocalShotFired;
                rig.weaponController.GrenadeLaunched -= OnLocalGrenadeLaunched;
                rig.weaponController.ReloadCuePlayed -= OnLocalReloadCue;
            }
        }

        private void Update()
        {
            // Owner publishes its current weapon selection and that weapon's add-ons.
            if (IsSpawned && IsOwner)
            {
                int index = rig.weaponController.CurrentIndex;
                if (WeaponIndex.Value != index)
                    WeaponIndex.Value = index;

                var weapons = rig.weaponController.weapons;
                int mask = Attachments.Effective(
                    GameSettings.GetAttachments(GameSettings.SelectedClass, index),
                    index >= 0 && index < weapons.Length ? weapons[index] : null);
                if (AttachmentMask.Value != mask)
                    AttachmentMask.Value = mask;
                int color = index >= 0 && index < GameSettings.WeaponColors.Length ? GameSettings.WeaponColors[index] : 0;
                if (ColorIndex.Value != color) ColorIndex.Value = color;
            }
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
                weapons[index], rig.thirdPersonWeaponAnchor, Vector3.zero, castShadows: true, AttachmentMask.Value, ColorIndex.Value);
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
            // Shooting ends your own spawn protection — no protected campers.
            var player = GetComponent<NetworkPlayer>();
            if (player != null)
                player.ServerClearSpawnProtection();

            FireClientRpc(endPoint);
        }

        [ClientRpc]
        private void FireClientRpc(Vector3 endPoint)
        {
            // Radar ping on every client — unless a suppressor keeps you hidden.
            if (!Attachments.SuppressesRadar(AttachmentMask.Value))
                LastShotTime = Time.time;

            if (IsOwner)
                return; // the owner already played its local effects

            Vector3 from = hasThirdPersonModel
                ? thirdPersonModel.muzzle.position
                : transform.position + Vector3.up * 1.4f;

            var weapons = rig.weaponController.weapons;
            int index = Mathf.Clamp(WeaponIndex.Value, 0, weapons.Length - 1);
            var weapon = weapons[index];

            // Grenades arc and are replicated as real rounds (GrenadeClientRpc), so
            // no straight tracer or predicted explosion for them.
            bool arcing = weapon.isProjectile && weapon.projectileGravity > 0f;
            if (!weapon.isMelee && !arcing)
                WeaponController.SpawnTracerLine(from, endPoint);

            // Positional gunshot so you can hear where enemies are firing from
            // (a suppressor makes their shots much quieter).
            bool suppressed = Attachments.Has(AttachmentMask.Value, AttachmentType.Suppressor);
            SfxSynth.PlayAt(SfxSynth.Shot(weapon.model, suppressed), from, 0.8f * Attachments.ShotVolumeMultiplier(AttachmentMask.Value), weapon.shotPitch);

            // Remote rocket: explosion at the predicted impact after the flight time.
            if (weapon.isProjectile && !arcing)
                StartCoroutine(RemoteExplosion(endPoint, Vector3.Distance(from, endPoint) / Mathf.Max(1f, weapon.projectileSpeed)));
        }

        // ------------------------------------------------------------------
        // Reload sounds: others hear a magazine reload, each shotgun shell and
        // the final pump at the reloading player's position.
        // ------------------------------------------------------------------

        private void OnLocalReloadCue(ReloadCue cue)
        {
            if (IsServer)
                ReloadCueClientRpc((byte)cue);
            else
                ReloadCueServerRpc((byte)cue);
        }

        [ServerRpc]
        private void ReloadCueServerRpc(byte cue) => ReloadCueClientRpc(cue);

        [ClientRpc]
        private void ReloadCueClientRpc(byte cue)
        {
            if (IsOwner)
                return; // the owner already heard it in 2D

            Vector3 at = hasThirdPersonModel
                ? thirdPersonModel.root.transform.position
                : transform.position + Vector3.up * 1.3f;
            switch ((ReloadCue)cue)
            {
                case ReloadCue.Shell:
                    SfxSynth.PlayAtDelayed(SfxSynth.ShellInsert(), at, 0f, 0.7f); // short range: reload foley, not a gunshot
                    break;
                case ReloadCue.Rack:
                    SfxSynth.PlayAtDelayed(SfxSynth.PumpRack(), at, 0f, 0.75f);
                    break;
                default:
                    var weapons = rig.weaponController.weapons;
                    var weapon = weapons[Mathf.Clamp(WeaponIndex.Value, 0, weapons.Length - 1)];
                    SfxSynth.PlayReloadAt(weapon.model, weapon.reloadTime, at, 0.7f);
                    break;
            }
        }

        // ------------------------------------------------------------------
        // Grenade launcher: the server owns the round (and its damage); every
        // client flies a visual replica from the same launch parameters.
        // ------------------------------------------------------------------

        private void OnLocalGrenadeLaunched(Vector3 origin, Vector3 velocity)
        {
            int index = rig.weaponController.CurrentIndex;
            if (IsServer)
            {
                SpawnGrenade(index, origin, velocity, GrenadeProjectile.DamageMode.Server);
                GrenadeClientRpc(index, origin, velocity);
            }
            else
            {
                // Instant local feedback; the server's copy decides the damage.
                SpawnGrenade(index, origin, velocity, GrenadeProjectile.DamageMode.None);
                GrenadeServerRpc(index, origin, velocity);
            }
        }

        [ServerRpc]
        private void GrenadeServerRpc(int index, Vector3 origin, Vector3 velocity)
        {
            if (!SpawnGrenade(index, origin, velocity, GrenadeProjectile.DamageMode.Server))
                return;
            GrenadeClientRpc(index, origin, velocity);
        }

        [ClientRpc]
        private void GrenadeClientRpc(int index, Vector3 origin, Vector3 velocity)
        {
            if (IsOwner || IsServer)
                return; // the shooter and the server already have their copy
            SpawnGrenade(index, origin, velocity, GrenadeProjectile.DamageMode.None);
        }

        private bool SpawnGrenade(int index, Vector3 origin, Vector3 velocity, GrenadeProjectile.DamageMode mode)
        {
            var weapons = rig.weaponController.weapons;
            if (index < 0 || index >= weapons.Length)
                return false;
            var weapon = weapons[index];
            if (!weapon.isProjectile || weapon.projectileGravity <= 0f)
                return false; // only grenade weapons launch grenades
            velocity = Vector3.ClampMagnitude(velocity, weapon.projectileSpeed * 1.05f); // no hyper-fast rounds
            GrenadeProjectile.Launch(origin, velocity, weapon.projectileGravity, weapon.damage,
                weapon.explosionRadius, transform, mode, OwnerClientId);
            return true;
        }

        private System.Collections.IEnumerator RemoteExplosion(Vector3 point, float delay)
        {
            yield return new WaitForSeconds(Mathf.Min(delay, 4f));
            Effects.SpawnExplosion(point);
            SfxSynth.PlayAt(SfxSynth.Explosion(), point, 1f);
        }
    }
}
