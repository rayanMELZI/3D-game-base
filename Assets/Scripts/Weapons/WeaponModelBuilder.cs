using UnityEngine;

namespace FpsBase
{
    /// <summary>A built weapon model plus the points the gameplay code needs.</summary>
    public struct WeaponModelInstance
    {
        public GameObject root;
        public Transform muzzle;
        public Light muzzleFlash;
    }

    /// <summary>
    /// Builds each weapon model. Matched weapon types (pistol / SMG / shotgun /
    /// rifle / sniper / RPG) use the imported low-poly Asset Store models loaded
    /// from Resources/Weapons; the knife (no imported match) and any failed load
    /// fall back to the original Unity-primitive models, so the project still
    /// runs even if the imported prefabs are missing.
    /// Used both for the first-person viewmodel and the third-person model
    /// other players see in multiplayer.
    /// </summary>
    public static class WeaponModelBuilder
    {
        public static WeaponModelInstance Build(WeaponDefinition def, Transform parent, Vector3 localPosition, bool castShadows, int attachmentMask = 0, int colorIndex = 0)
        {
            var root = new GameObject(def.displayName + "Model");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;

            Vector3 muzzlePos;
            if (!TryBuildImported(def.model, root.transform, out muzzlePos))
                muzzlePos = BuildProcedural(def.model, root.transform);

            // Bolt on the chosen add-ons (may push the muzzle forward: suppressor).
            attachmentMask = Attachments.Sanitize(attachmentMask, def.model);
            if (attachmentMask != 0)
                AddAttachments(root.transform, attachmentMask, ref muzzlePos);

            ApplyColor(root, colorIndex);

            foreach (var r in root.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = castShadows
                    ? UnityEngine.Rendering.ShadowCastingMode.On
                    : UnityEngine.Rendering.ShadowCastingMode.Off;

            // Muzzle point with a flash light (enabled briefly per shot).
            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(root.transform, false);
            muzzle.transform.localPosition = muzzlePos;

            var flash = muzzle.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = new Color(1f, 0.82f, 0.45f);
            // A suppressor swallows most of the flash.
            flash.intensity = Attachments.Has(attachmentMask, AttachmentType.Suppressor) ? 1.6f : 4f;
            flash.range = 4f;
            flash.enabled = false;

            return new WeaponModelInstance { root = root, muzzle = muzzle.transform, muzzleFlash = flash };
        }

        private static readonly Color[] SkinColors =
        {
            Color.white, new Color(0.78f, 0.12f, 0.08f), new Color(0.08f, 0.35f, 0.82f),
            new Color(0.12f, 0.65f, 0.24f), new Color(0.72f, 0.48f, 0.08f), new Color(0.4f, 0.12f, 0.58f),
        };
        private static readonly string[] SkinNames = { "DEFAULT", "CRIMSON", "COBALT", "FOREST", "GOLD", "VIOLET" };
        public static string ColorName(int index) => SkinNames[Mathf.Clamp(index, 0, SkinNames.Length - 1)];
        private static void ApplyColor(GameObject root, int index)
        {
            index = Mathf.Clamp(index, 0, SkinColors.Length - 1);
            if (index == 0) return;
            var block = new MaterialPropertyBlock();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.GetPropertyBlock(block);
                block.SetColor("_Color", SkinColors[index]);
                block.SetColor("_BaseColor", SkinColors[index]);
                renderer.SetPropertyBlock(block);
            }
        }

        // ------------------------------------------------------------------
        // Add-on models (procedural, positioned relative to the muzzle/holder).
        // First pass — good enough to read at a glance; exact per-weapon fit is
        // best nudged visually in Unity if any piece clips the base model.
        // ------------------------------------------------------------------

        private static void AddAttachments(Transform holder, int mask, ref Vector3 muzzlePos)
        {
            var black = MakeMat(new Color(0.06f, 0.06f, 0.07f), 0.3f, 0.4f);
            var metal = MakeMat(new Color(0.14f, 0.14f, 0.15f), 0.65f, 0.72f);
            var dot = MakeMat(new Color(0.95f, 0.12f, 0.08f), 0.2f, 0.9f);

            if (Attachments.Has(mask, AttachmentType.Suppressor))
            {
                const float len = 0.13f;
                var centre = muzzlePos + new Vector3(0, 0, len * 0.5f - 0.01f);
                if (!ImportedAttachment("Suppressor", holder, centre, new Vector3(0.32f, 0.32f, 0.32f)))
                    Part(holder, PrimitiveType.Cylinder, centre, new Vector3(90, 0, 0), new Vector3(0.05f, len * 0.5f, 0.05f), black);
                muzzlePos += new Vector3(0, 0, len); // the flash now exits the can
            }
            if (Attachments.Has(mask, AttachmentType.Optic))
            {
                float z = Mathf.Min(0.02f, muzzlePos.z - 0.3f); // sit over the receiver, behind the muzzle
                ImportedAttachment("Optic", holder, new Vector3(0, 0.085f, z), new Vector3(0.3f, 0.3f, 0.3f));
                Part(holder, PrimitiveType.Cube, new Vector3(0, 0.085f, z), Vector3.zero, new Vector3(0.05f, 0.03f, 0.06f), black); // mount
                Part(holder, PrimitiveType.Cube, new Vector3(0, 0.108f, z), Vector3.zero, new Vector3(0.046f, 0.016f, 0.05f), black); // hood
                Part(holder, PrimitiveType.Cube, new Vector3(0, 0.097f, z + 0.006f), Vector3.zero, new Vector3(0.008f, 0.008f, 0.006f), dot); // red dot
            }
            if (Attachments.Has(mask, AttachmentType.Foregrip))
            {
                float z = Mathf.Max(0.12f, muzzlePos.z - 0.2f);
                if (!ImportedAttachment("Foregrip", holder, new Vector3(0, -0.085f, z), new Vector3(0.3f, 0.3f, 0.3f)))
                    Part(holder, PrimitiveType.Cube, new Vector3(0, -0.085f, z), new Vector3(6, 0, 0), new Vector3(0.028f, 0.09f, 0.032f), black);
            }
            if (Attachments.Has(mask, AttachmentType.ExtendedMag))
            {
                // A longer mag hanging below the normal magazine well.
                if (!ImportedAttachment("ExtendedMag", holder, new Vector3(0, -0.185f, 0.02f), new Vector3(0.28f, 0.28f, 0.28f)))
                    Part(holder, PrimitiveType.Cube, new Vector3(0, -0.185f, 0.02f), new Vector3(10, 0, 0), new Vector3(0.036f, 0.11f, 0.052f), metal);
            }
        }

        private static bool ImportedAttachment(string name, Transform holder, Vector3 position, Vector3 scale)
        {
            var prefab = Resources.Load<GameObject>("Weapons/Attachments/" + name);
            if (prefab == null) return false;
            var instance = Object.Instantiate(prefab, holder, false);
            instance.name = name;
            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = scale;
            foreach (var collider in instance.GetComponentsInChildren<Collider>()) Object.Destroy(collider);
            return true;
        }

        // ------------------------------------------------------------------
        // Imported Asset Store models (Resources/Weapons/*)
        // ------------------------------------------------------------------

        /// <summary>
        /// Per-weapon placement of the imported model inside its holder.
        /// The imported prefabs are meter-scale with +Z as the barrel, matching
        /// the primitive convention, so these are small nudges. If a model sits
        /// wrong in-game, tweak the numbers here (one row per weapon) — that is
        /// the single place to fine-tune fit.
        /// </summary>
        private struct ModelFit
        {
            public string resource;   // path under Resources (null = no imported model)
            public Vector3 scale;
            public Vector3 euler;     // extra rotation to align the barrel to +Z
            public Vector3 offset;    // local position of the model within the holder
            public Vector3 muzzle;    // muzzle/flash local position within the holder
        }

        private static ModelFit FitFor(WeaponModelType type)
        {
            switch (type)
            {
                case WeaponModelType.Pistol:
                    return new ModelFit { resource = "Weapons/Pistol_P", scale = Vector3.one,
                        euler = Vector3.zero, offset = new Vector3(0, 0, -0.02f), muzzle = new Vector3(0, 0.03f, 0.2f) };
                case WeaponModelType.Smg:
                    return new ModelFit { resource = "Weapons/SMG_P", scale = Vector3.one,
                        euler = Vector3.zero, offset = new Vector3(0, 0, -0.1f), muzzle = new Vector3(0, 0.02f, 0.32f) };
                case WeaponModelType.Shotgun:
                    return new ModelFit { resource = "Weapons/ShotGun_P", scale = Vector3.one,
                        euler = Vector3.zero, offset = new Vector3(0, 0, -0.12f), muzzle = new Vector3(0, 0.02f, 0.55f) };
                case WeaponModelType.Rifle:
                    return new ModelFit { resource = "Weapons/AR_T", scale = Vector3.one,
                        euler = Vector3.zero, offset = new Vector3(0, 0, -0.12f), muzzle = new Vector3(0, 0.02f, 0.5f) };
                case WeaponModelType.Sniper:
                    return new ModelFit { resource = "Weapons/Recon_P", scale = Vector3.one,
                        euler = Vector3.zero, offset = new Vector3(0, 0, -0.15f), muzzle = new Vector3(0, 0.02f, 0.75f) };
                case WeaponModelType.Rpg:
                    return new ModelFit { resource = "Weapons/Launcher_G", scale = Vector3.one,
                        euler = Vector3.zero, offset = new Vector3(0, 0, -0.1f), muzzle = new Vector3(0, 0.04f, 0.55f) };
                case WeaponModelType.Lmg:
                    return new ModelFit { resource = "Weapons/AR_T", scale = new Vector3(1.08f, 1.08f, 1.16f),
                        euler = Vector3.zero, offset = new Vector3(0, 0, -0.12f), muzzle = new Vector3(0, 0.02f, 0.58f) };
                case WeaponModelType.GrenadeLauncher:
                    return new ModelFit { resource = "Weapons/Launcher_G", scale = new Vector3(0.82f, 0.82f, 0.78f),
                        euler = Vector3.zero, offset = new Vector3(0, 0, -0.1f), muzzle = new Vector3(0, 0.04f, 0.43f) };
                default: // Knife has no imported match — always procedural.
                    return new ModelFit { resource = null };
            }
        }

        private static bool TryBuildImported(WeaponModelType type, Transform holder, out Vector3 muzzlePos)
        {
            muzzlePos = Vector3.zero;
            var fit = FitFor(type);
            if (string.IsNullOrEmpty(fit.resource))
                return false;

            var prefab = Resources.Load<GameObject>(fit.resource);
            if (prefab == null)
                return false; // not imported yet — fall back to primitives

            var model = Object.Instantiate(prefab, holder, false);
            model.transform.localPosition = fit.offset;
            model.transform.localRotation = Quaternion.Euler(fit.euler);
            model.transform.localScale = fit.scale;

            // Weapon models are purely cosmetic; strip any colliders/rigidbodies.
            foreach (var col in model.GetComponentsInChildren<Collider>())
                Object.Destroy(col);
            foreach (var rb in model.GetComponentsInChildren<Rigidbody>())
                Object.Destroy(rb);

            muzzlePos = fit.muzzle;
            return true;
        }

        // ------------------------------------------------------------------
        // Procedural fallback models (all sized in meters, +Z is the barrel)
        // ------------------------------------------------------------------

        private static Vector3 BuildProcedural(WeaponModelType type, Transform t)
        {
            var metal = MakeMat(new Color(0.14f, 0.14f, 0.15f), 0.65f, 0.72f);
            var grip = MakeMat(new Color(0.09f, 0.09f, 0.1f), 0.1f, 0.35f);
            var accent = MakeMat(new Color(0.32f, 0.34f, 0.38f), 0.8f, 0.6f);

            switch (type)
            {
                case WeaponModelType.Knife:
                    BuildKnife(t, metal, grip, accent);
                    return new Vector3(0, 0, 0.22f);
                case WeaponModelType.Pistol:
                    BuildPistol(t, metal, grip, accent);
                    return new Vector3(0, 0.03f, 0.18f);
                case WeaponModelType.Smg:
                    BuildSmg(t, metal, grip, accent);
                    return new Vector3(0, 0.015f, 0.3f);
                case WeaponModelType.Shotgun:
                    BuildShotgun(t, metal, grip, accent);
                    return new Vector3(0, 0.02f, 0.52f);
                case WeaponModelType.Sniper:
                    BuildSniper(t, metal, grip, accent);
                    return new Vector3(0, 0.02f, 0.74f);
                case WeaponModelType.Rpg:
                    BuildRpg(t, metal, grip, accent);
                    return new Vector3(0, 0.04f, 0.55f);
                default:
                    BuildRifle(t, metal, grip, accent);
                    return new Vector3(0, 0.02f, 0.48f);
            }
        }

        // ------------------------------------------------------------------
        // Models (all sized in meters, +Z is the barrel direction)
        // ------------------------------------------------------------------

        private static void BuildKnife(Transform t, Material metal, Material grip, Material accent)
        {
            // Polished tactical knife: tapered clip-point blade with a dark spine
            // and fuller groove, brass crossguard, ringed wrap handle + pommel.
            var steel = MakeMat(new Color(0.8f, 0.84f, 0.9f), 0.95f, 0.92f);
            var darkSteel = MakeMat(new Color(0.22f, 0.24f, 0.28f), 0.8f, 0.7f);
            var brass = MakeMat(new Color(0.62f, 0.5f, 0.24f), 0.9f, 0.75f);
            var wrap = MakeMat(new Color(0.08f, 0.08f, 0.09f), 0.05f, 0.3f);

            // Blade.
            Part(t, PrimitiveType.Cube, new Vector3(0, 0.004f, 0.13f), Vector3.zero, new Vector3(0.01f, 0.042f, 0.22f), steel);        // body
            Part(t, PrimitiveType.Cube, new Vector3(0, 0.002f, 0.255f), new Vector3(7f, 0, 0), new Vector3(0.008f, 0.028f, 0.06f), steel); // clip-point taper
            Part(t, PrimitiveType.Cube, new Vector3(0, 0.026f, 0.11f), Vector3.zero, new Vector3(0.012f, 0.007f, 0.18f), darkSteel);   // spine
            Part(t, PrimitiveType.Cube, new Vector3(0, -0.018f, 0.13f), new Vector3(0, 0, 45f), new Vector3(0.007f, 0.007f, 0.21f), steel); // edge bevel glint
            Part(t, PrimitiveType.Cube, new Vector3(0, 0.008f, 0.12f), Vector3.zero, new Vector3(0.011f, 0.005f, 0.16f), darkSteel);   // fuller groove

            // Brass crossguard, slightly swept.
            Part(t, PrimitiveType.Cube, new Vector3(0, 0, 0.015f), new Vector3(0, 0, 4f), new Vector3(0.018f, 0.075f, 0.014f), brass);

            // Handle: wrapped grip angled down, brass rings and pommel.
            Part(t, PrimitiveType.Cube, new Vector3(0, -0.008f, -0.045f), new Vector3(-7f, 0, 0), new Vector3(0.024f, 0.036f, 0.105f), wrap);
            Part(t, PrimitiveType.Cube, new Vector3(0, -0.006f, -0.02f), new Vector3(-7f, 0, 0), new Vector3(0.026f, 0.038f, 0.012f), brass);
            Part(t, PrimitiveType.Cube, new Vector3(0, -0.011f, -0.07f), new Vector3(-7f, 0, 0), new Vector3(0.026f, 0.038f, 0.012f), brass);
            Part(t, PrimitiveType.Cube, new Vector3(0, -0.016f, -0.103f), new Vector3(-7f, 0, 0), new Vector3(0.026f, 0.042f, 0.02f), brass);
        }

        private static void BuildSmg(Transform t, Material metal, Material grip, Material accent)
        {
            Part(t, PrimitiveType.Cube, new Vector3(0, 0, 0), Vector3.zero, new Vector3(0.055f, 0.075f, 0.3f), metal);              // compact receiver
            Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.015f, 0.25f), new Vector3(90, 0, 0), new Vector3(0.022f, 0.06f, 0.022f), metal); // stub barrel
            Part(t, PrimitiveType.Cube, new Vector3(0, -0.11f, 0.02f), new Vector3(6, 0, 0), new Vector3(0.035f, 0.16f, 0.05f), grip);        // long magazine
            Part(t, PrimitiveType.Cube, new Vector3(0, -0.075f, -0.09f), new Vector3(-18, 0, 0), new Vector3(0.038f, 0.1f, 0.05f), grip);     // pistol grip
            Part(t, PrimitiveType.Cube, new Vector3(0, 0.005f, -0.22f), Vector3.zero, new Vector3(0.03f, 0.05f, 0.14f), accent);    // wire stock
            Part(t, PrimitiveType.Cube, new Vector3(0, 0.055f, 0.2f), Vector3.zero, new Vector3(0.01f, 0.022f, 0.01f), metal);      // front sight
        }

        private static void BuildShotgun(Transform t, Material metal, Material grip, Material accent)
        {
            var wood = MakeMat(new Color(0.42f, 0.28f, 0.16f), 0.05f, 0.4f);
            Part(t, PrimitiveType.Cube, new Vector3(0, 0, -0.02f), Vector3.zero, new Vector3(0.06f, 0.08f, 0.3f), metal);           // receiver
            Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.02f, 0.3f), new Vector3(90, 0, 0), new Vector3(0.03f, 0.22f, 0.03f), metal);   // barrel
            Part(t, PrimitiveType.Cylinder, new Vector3(0, -0.025f, 0.28f), new Vector3(90, 0, 0), new Vector3(0.026f, 0.18f, 0.026f), metal); // tube magazine
            Part(t, PrimitiveType.Cube, new Vector3(0, -0.025f, 0.22f), Vector3.zero, new Vector3(0.05f, 0.045f, 0.12f), wood);     // pump
            Part(t, PrimitiveType.Cube, new Vector3(0, -0.02f, -0.28f), new Vector3(-6, 0, 0), new Vector3(0.05f, 0.09f, 0.24f), wood); // stock
            Part(t, PrimitiveType.Cube, new Vector3(0, 0.06f, 0.48f), Vector3.zero, new Vector3(0.01f, 0.02f, 0.012f), metal);      // bead sight
        }

        private static void BuildRpg(Transform t, Material metal, Material grip, Material accent)
        {
            var warheadMat = MakeMat(new Color(0.55f, 0.35f, 0.2f), 0.3f, 0.5f);
            Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.04f, 0.05f), new Vector3(90, 0, 0), new Vector3(0.08f, 0.35f, 0.08f), metal);  // launch tube
            Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.04f, -0.32f), new Vector3(90, 0, 0), new Vector3(0.095f, 0.05f, 0.095f), accent); // rear flare
            Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.04f, 0.46f), new Vector3(90, 0, 0), new Vector3(0.06f, 0.08f, 0.06f), warheadMat); // warhead body
            Part(t, PrimitiveType.Sphere, new Vector3(0, 0.04f, 0.56f), Vector3.zero, new Vector3(0.06f, 0.06f, 0.09f), warheadMat);           // warhead tip
            Part(t, PrimitiveType.Cube, new Vector3(0, -0.07f, -0.02f), new Vector3(-14, 0, 0), new Vector3(0.04f, 0.1f, 0.05f), grip);        // grip
            Part(t, PrimitiveType.Cube, new Vector3(0, 0.11f, -0.05f), Vector3.zero, new Vector3(0.03f, 0.05f, 0.09f), accent);     // sight block
        }

        private static void BuildPistol(Transform t, Material metal, Material grip, Material accent)
        {
            Part(t, PrimitiveType.Cube, new Vector3(0, 0.025f, 0.04f), Vector3.zero, new Vector3(0.05f, 0.055f, 0.23f), metal);   // slide
            Part(t, PrimitiveType.Cube, new Vector3(0, -0.02f, 0.03f), Vector3.zero, new Vector3(0.046f, 0.045f, 0.17f), accent); // frame
            Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.03f, 0.16f), new Vector3(90, 0, 0), new Vector3(0.024f, 0.02f, 0.024f), metal); // barrel tip
            Part(t, PrimitiveType.Cube, new Vector3(0, -0.095f, -0.03f), new Vector3(-16, 0, 0), new Vector3(0.044f, 0.13f, 0.062f), grip);  // grip
            Part(t, PrimitiveType.Cube, new Vector3(0, 0.062f, 0.14f), Vector3.zero, new Vector3(0.01f, 0.018f, 0.012f), metal);  // front sight
            Part(t, PrimitiveType.Cube, new Vector3(0, 0.062f, -0.06f), Vector3.zero, new Vector3(0.024f, 0.016f, 0.012f), metal); // rear sight
        }

        private static void BuildRifle(Transform t, Material metal, Material grip, Material accent)
        {
            Part(t, PrimitiveType.Cube, new Vector3(0, 0, 0), Vector3.zero, new Vector3(0.06f, 0.085f, 0.42f), metal);            // receiver
            Part(t, PrimitiveType.Cube, new Vector3(0, 0, 0.27f), Vector3.zero, new Vector3(0.052f, 0.06f, 0.2f), accent);        // handguard
            Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.02f, 0.42f), new Vector3(90, 0, 0), new Vector3(0.024f, 0.08f, 0.024f), metal); // barrel
            Part(t, PrimitiveType.Cube, new Vector3(0, -0.1f, 0.05f), new Vector3(12, 0, 0), new Vector3(0.04f, 0.15f, 0.07f), grip);        // magazine
            Part(t, PrimitiveType.Cube, new Vector3(0, -0.085f, -0.13f), new Vector3(-18, 0, 0), new Vector3(0.04f, 0.11f, 0.05f), grip);    // pistol grip
            Part(t, PrimitiveType.Cube, new Vector3(0, -0.005f, -0.31f), Vector3.zero, new Vector3(0.046f, 0.075f, 0.2f), grip);  // stock
            Part(t, PrimitiveType.Cube, new Vector3(0, 0.062f, 0.36f), Vector3.zero, new Vector3(0.012f, 0.03f, 0.012f), metal);  // front sight
            Part(t, PrimitiveType.Cube, new Vector3(0, 0.062f, -0.12f), Vector3.zero, new Vector3(0.026f, 0.024f, 0.02f), metal); // rear sight
        }

        private static void BuildSniper(Transform t, Material metal, Material grip, Material accent)
        {
            Part(t, PrimitiveType.Cube, new Vector3(0, 0, 0.05f), Vector3.zero, new Vector3(0.055f, 0.08f, 0.4f), metal);         // receiver
            Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.02f, 0.47f), new Vector3(90, 0, 0), new Vector3(0.022f, 0.24f, 0.022f), metal); // long barrel
            Part(t, PrimitiveType.Cube, new Vector3(0, 0.02f, 0.72f), Vector3.zero, new Vector3(0.034f, 0.034f, 0.05f), accent);  // muzzle brake
            Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.095f, 0.02f), new Vector3(90, 0, 0), new Vector3(0.036f, 0.11f, 0.036f), metal); // scope tube
            Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.095f, 0.14f), new Vector3(90, 0, 0), new Vector3(0.044f, 0.015f, 0.044f), accent); // scope front ring
            Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.095f, -0.1f), new Vector3(90, 0, 0), new Vector3(0.042f, 0.015f, 0.042f), accent); // scope rear ring
            Part(t, PrimitiveType.Cube, new Vector3(0, -0.02f, -0.3f), Vector3.zero, new Vector3(0.05f, 0.09f, 0.24f), grip);     // stock
            Part(t, PrimitiveType.Cube, new Vector3(0, 0.05f, -0.3f), Vector3.zero, new Vector3(0.044f, 0.03f, 0.14f), grip);     // cheek riser
            Part(t, PrimitiveType.Cube, new Vector3(0, -0.09f, -0.1f), new Vector3(-18, 0, 0), new Vector3(0.04f, 0.1f, 0.05f), grip); // pistol grip
        }

        // ------------------------------------------------------------------

        private static void Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 euler, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>()); // weapon models are cosmetic only
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().material = mat;
        }

        // Shared cache: weapon models are rebuilt on every switch (Gun Game
        // changes weapon each kill), so fresh materials here would leak.
        private static Material MakeMat(Color color, float metallic, float smoothness)
            => EnvironmentBuilder.SharedMaterial(color, metallic, smoothness);
    }
}
