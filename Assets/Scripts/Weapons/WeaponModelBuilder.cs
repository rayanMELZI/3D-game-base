using UnityEngine;

namespace FpsBase
{
    /// <summary>A built weapon model plus the points the gameplay code needs.</summary>
    public struct WeaponModelInstance
    {
        public GameObject root;
        public Transform muzzle;
        public Transform rightHandGrip;
        public Transform leftHandGrip;
        public Transform magazine;
        public Vector3 magazineRestPosition;
        public Quaternion magazineRestRotation;
        public Transform pull;
        public Vector3 pullRestPosition;
        public Quaternion pullRestRotation;
        public Light muzzleFlash;
        /// <summary>True when an optic is mounted: aim through <see cref="opticSight"/>.</summary>
        public bool hasOpticSight;
        /// <summary>Centre of the optic's lens (red dot), in model-root space.</summary>
        public Vector3 opticSight;
    }

    /// <summary>
    /// Builds each weapon model. Matched weapon types (pistol / SMG / shotgun /
    /// rifle / sniper / RPG / knife) use imported low-poly Asset Store models
    /// loaded from Resources/Weapons. Any failed load falls back to the original
    /// Unity-primitive model, so the project still runs if a prefab is missing.
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
            if (!TryBuildImported(def, root.transform, out muzzlePos))
                muzzlePos = BuildProcedural(def.model, root.transform);

            // Authored sockets are optional. Weapons without them keep using the
            // old generic hand placement and hard-coded muzzle position.
            Transform rightHandGrip = FindDeepChild(root.transform, "RightHandGrip");
            Transform leftHandGrip = FindDeepChild(root.transform, "LeftHandGrip");
            Transform muzzleSocket = FindDeepChild(root.transform, "MuzzleSocket");
            // "Modern" pack parts end in _magazine/_pull; the AR_/SMG_/Recon_ packs
            // name the same parts _Mag/_Bolt.
            Transform magazine = FindDeepChildEndingWith(root.transform, "_magazine")
                                 ?? FindDeepChildEndingWith(root.transform, "_Mag");
            Transform pull = FindDeepChildEndingWith(root.transform, "_pull")
                             ?? FindDeepChildEndingWith(root.transform, "_Bolt");
            if (muzzleSocket != null)
                muzzlePos = root.transform.InverseTransformPoint(muzzleSocket.position);

            // Bolt on the chosen add-ons (may push the muzzle forward: suppressor).
            attachmentMask = Attachments.Sanitize(attachmentMask, def.model);
            // A built-in suppressor is part of the model already: no extra can.
            int mountedMask = def.integralSuppressor
                ? attachmentMask & ~Attachments.Bit(AttachmentType.Suppressor) : attachmentMask;
            Vector3? opticSight = null;
            if (mountedMask != 0)
                opticSight = AddAttachments(root.transform, mountedMask, ref muzzlePos, magazine, def);

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

            var posePoints = root.AddComponent<WeaponPosePoints>();
            posePoints.Configure(rightHandGrip, leftHandGrip);

            return new WeaponModelInstance
            {
                root = root,
                muzzle = muzzle.transform,
                rightHandGrip = rightHandGrip,
                leftHandGrip = leftHandGrip,
                magazine = magazine,
                magazineRestPosition = magazine != null ? magazine.localPosition : Vector3.zero,
                magazineRestRotation = magazine != null ? magazine.localRotation : Quaternion.identity,
                pull = pull,
                pullRestPosition = pull != null ? pull.localPosition : Vector3.zero,
                pullRestRotation = pull != null ? pull.localRotation : Quaternion.identity,
                muzzleFlash = flash,
                hasOpticSight = opticSight.HasValue,
                opticSight = opticSight ?? Vector3.zero
            };
        }

        private static Transform FindDeepChild(Transform parent, string childName)
        {
            foreach (var child in parent.GetComponentsInChildren<Transform>(true))
                if (child.name == childName)
                    return child;
            return null;
        }

        private static Transform FindDeepChildEndingWith(Transform parent, string suffix)
        {
            foreach (var child in parent.GetComponentsInChildren<Transform>(true))
                if (child.name.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase))
                    return child;
            return null;
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
        // Add-ons, fitted to each model from its measured parts.
        //
        // Every weapon is measured in holder space (renderer bounds per part),
        // then: the optic sits on the receiver top above the trigger and the
        // iron sights fold away; the suppressor's rear lines up with the muzzle;
        // the foregrip hangs under the handguard, ahead of the magazine; the
        // extended mag stretches the weapon's own magazine downwards. One scale
        // for every add-on keeps them true to size next to each other. Per-model
        // nudges, if one still looks off: AddonNudge.
        // ------------------------------------------------------------------

        /// <summary>Add-on model scale (the pack is metre-built, like the AR_ weapons at 0.85).</summary>
        private const float AddonScale = 0.85f;

        private struct PartBox
        {
            public Transform transform;
            public Bounds bounds;
            public Vector3[] points; // mesh vertices in holder space (null: unreadable, use bounds)
            public int[] triangles;
        }

        // Vertices + triangles per mesh: models are rebuilt on every weapon switch.
        private static readonly System.Collections.Generic.Dictionary<Mesh, (Vector3[] vertices, int[] triangles)> meshData =
            new System.Collections.Generic.Dictionary<Mesh, (Vector3[], int[])>();

        /// <summary>Mounts the add-ons; returns the optic's sight point if one was mounted.</summary>
        private static Vector3? AddAttachments(Transform holder, int mask, ref Vector3 muzzlePos, Transform magazine, WeaponDefinition def)
        {
            Vector3? sight = null;
            var parts = MeasureParts(holder);
            if (parts.Count == 0)
                return null;
            Bounds all = parts[0].bounds;
            foreach (var p in parts) all.Encapsulate(p.bounds);
            float trigger = TriggerZ(holder, parts, magazine);
            // Handguns get smaller add-ons (compact red-dot, short can).
            float scale = def.model == WeaponModelType.Pistol ? 0.6f : AddonScale;

            if (Attachments.Has(mask, AttachmentType.Suppressor))
            {
                // Screwed on the real barrel tip, centred on its bore (some
                // muzzle points sit ahead of or above the barrel).
                muzzlePos = BarrelTip(parts, muzzlePos);
                var can = MountAddon("Suppressor", holder, scale);
                if (can != null)
                {
                    // Rear of the can on the barrel tip, centred on the bore.
                    var b = BoundsIn(holder, can.transform);
                    can.transform.localPosition += muzzlePos - new Vector3(b.center.x, b.center.y, b.min.z + 0.01f);
                    muzzlePos.z += b.size.z - 0.01f; // the flash now exits the can
                }
                else
                {
                    const float len = 0.13f;
                    Part(holder, PrimitiveType.Cylinder, muzzlePos + new Vector3(0, 0, len * 0.5f), new Vector3(90, 0, 0),
                        new Vector3(0.045f, len * 0.5f, 0.045f), MakeMat(new Color(0.06f, 0.06f, 0.07f), 0.3f, 0.4f));
                    muzzlePos.z += len;
                }
            }

            if (Attachments.Has(mask, AttachmentType.Optic))
            {
                foreach (var ironSight in IronSights(holder))
                    ironSight.gameObject.SetActive(false);
                var optic = MountAddon("Optic", holder, scale);
                if (optic != null)
                {
                    var b = BoundsIn(holder, optic.transform);
                    // Over the trigger, a little forward; on the highest surface
                    // under its footprint (rail/receiver top, sights excluded).
                    float z = trigger + b.size.z * 0.35f + Nudge(def, AttachmentType.Optic).z;
                    float top = TopAt(parts, z - b.extents.z * 0.8f, z + b.extents.z * 0.8f, all.max.y);
                    var target = new Vector3(0f, top + Nudge(def, AttachmentType.Optic).y, z);
                    optic.transform.localPosition += target - new Vector3(b.center.x, b.min.y, b.center.z);
                    sight = AddRedDot(holder, optic.transform);
                }
            }

            if (Attachments.Has(mask, AttachmentType.Foregrip))
            {
                var grip = MountAddon("Foregrip", holder, scale);
                if (grip != null)
                {
                    var b = BoundsIn(holder, grip.transform);
                    // Under the handguard: between the magazine and the muzzle.
                    float magFront = magazine != null ? BoundsIn(holder, magazine).max.z : trigger + 0.08f;
                    float z = Mathf.Max(magFront + b.extents.z + 0.03f, Mathf.Lerp(trigger, muzzlePos.z, 0.55f));
                    z = Mathf.Min(z, muzzlePos.z - b.extents.z - 0.02f) + Nudge(def, AttachmentType.Foregrip).z;
                    // Only the handguard counts: bipods, loops and guards hang lower.
                    float bottom = BottomAt(parts, magazine, z - b.extents.z, z + b.extents.z, muzzlePos.y - 0.02f, muzzlePos.y - 0.09f);
                    var target = new Vector3(0f, bottom + Nudge(def, AttachmentType.Foregrip).y, z);
                    grip.transform.localPosition += target - new Vector3(b.center.x, b.max.y - 0.004f, b.center.z);
                }
            }

            // Tube-fed shotguns and the break-action launcher carry the extra
            // rounds out of sight: no visible mag there (the stat still applies).
            bool visibleMag = def.model != WeaponModelType.Shotgun && def.model != WeaponModelType.GrenadeLauncher;
            if (visibleMag && Attachments.Has(mask, AttachmentType.ExtendedMag))
            {
                if (magazine != null)
                    StretchMagazine(holder, magazine, def.model == WeaponModelType.Pistol ? 1.3f : 1.45f);
                else
                {
                    var mag = MountAddon("ExtendedMag", holder, scale);
                    if (mag != null)
                    {
                        var b = BoundsIn(holder, mag.transform);
                        float bottom = BottomAt(parts, null, trigger + 0.04f, trigger + 0.04f + b.size.z, all.min.y, float.MinValue);
                        var target = new Vector3(0f, bottom + 0.01f, trigger + 0.05f + b.extents.z);
                        mag.transform.localPosition += target - new Vector3(b.center.x, b.max.y, b.center.z);
                    }
                }
            }
            return sight;
        }

        /// <summary>
        /// A small glowing dot in the middle of the optic's lens; returns its
        /// position (holder space) so ADS can line the dot up with the screen centre.
        /// </summary>
        private static Vector3 AddRedDot(Transform holder, Transform optic)
        {
            Transform lens = null;
            foreach (var t in optic.GetComponentsInChildren<Transform>())
                if (t.name.IndexOf("lens", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    lens = t;
            Bounds b = BoundsIn(holder, lens != null ? lens : optic);
            var dot = new Vector3(b.center.x, b.center.y, b.center.z);
            Part(holder, PrimitiveType.Sphere, dot, Vector3.zero, Vector3.one * 0.0035f,
                EnvironmentBuilder.SharedMaterial(new Color(1f, 0.08f, 0.05f), emission: 4f));
            return dot;
        }

        /// <summary>Optional per-model correction (holder units) after the automatic fit.</summary>
        private static Vector3 Nudge(WeaponDefinition def, AttachmentType type)
        {
            return Vector3.zero;
        }

        private static GameObject MountAddon(string name, Transform holder, float scale)
        {
            var prefab = Resources.Load<GameObject>("Weapons/Attachments/" + name);
            if (prefab == null) return null;
            var instance = Object.Instantiate(prefab, holder, false);
            instance.name = name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * scale;
            foreach (var collider in instance.GetComponentsInChildren<Collider>()) Strip(collider);
            return instance;
        }

        /// <summary>Every visible part of the bare model with its bounds in holder space.</summary>
        private static System.Collections.Generic.List<PartBox> MeasureParts(Transform holder)
        {
            var list = new System.Collections.Generic.List<PartBox>();
            foreach (var r in holder.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)
                    continue;
                var box = new PartBox { transform = r.transform, bounds = RendererBoundsIn(holder, r) };
                PointsIn(holder, r, ref box);
                list.Add(box);
            }
            return list;
        }

        /// <summary>Bounds of everything under <paramref name="target"/>, in holder space.</summary>
        private static Bounds BoundsIn(Transform holder, Transform target)
        {
            bool any = false;
            var result = new Bounds(holder.InverseTransformPoint(target.position), Vector3.zero);
            foreach (var r in target.GetComponentsInChildren<Renderer>())
            {
                var b = RendererBoundsIn(holder, r);
                if (any) result.Encapsulate(b); else result = b;
                any = true;
            }
            return result;
        }

        private static Bounds RendererBoundsIn(Transform holder, Renderer r)
        {
            Bounds local;
            var filter = r.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null) local = filter.sharedMesh.bounds;
            else if (r is SkinnedMeshRenderer skinned && skinned.sharedMesh != null) local = skinned.sharedMesh.bounds;
            else return new Bounds(holder.InverseTransformPoint(r.bounds.center), r.bounds.size);

            Matrix4x4 toHolder = holder.worldToLocalMatrix * r.transform.localToWorldMatrix;
            var result = new Bounds(toHolder.MultiplyPoint3x4(local.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                result.Encapsulate(toHolder.MultiplyPoint3x4(corner));
            }
            return result;
        }

        /// <summary>
        /// The part's mesh in holder space, so a one-piece model is measured by
        /// its real silhouette (top of the receiver, bottom of the handguard)
        /// instead of its whole box. Needs Read/Write on the model import;
        /// unreadable meshes fall back to their bounds.
        /// </summary>
        private static void PointsIn(Transform holder, Renderer r, ref PartBox box)
        {
            var filter = r.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
                return;
            if (!meshData.TryGetValue(mesh, out var data))
            {
                data = mesh.isReadable ? (mesh.vertices, mesh.triangles) : (null, null);
                meshData[mesh] = data;
            }
            if (data.vertices == null || data.triangles == null || data.triangles.Length == 0)
                return;
            Matrix4x4 toHolder = holder.worldToLocalMatrix * r.transform.localToWorldMatrix;
            var points = new Vector3[data.vertices.Length];
            for (int i = 0; i < points.Length; i++)
                points[i] = toHolder.MultiplyPoint3x4(data.vertices[i]);
            box.points = points;
            box.triangles = data.triangles;
        }

        /// <summary>
        /// Lowest and highest y of a part's surface within the slab z0..z1:
        /// triangle edges clipped to the slab, so long low-poly faces count too.
        /// </summary>
        private static bool SliceY(PartBox p, float z0, float z1, out float minY, out float maxY)
        {
            minY = float.MaxValue;
            maxY = float.MinValue;
            if (p.bounds.max.z <= z0 || p.bounds.min.z >= z1)
                return false;
            if (p.points == null)
            {
                minY = p.bounds.min.y;
                maxY = p.bounds.max.y;
                return true;
            }
            var v = p.points;
            var t = p.triangles;
            for (int i = 0; i + 2 < t.Length; i += 3)
                for (int e = 0; e < 3; e++)
                {
                    Vector3 a = v[t[i + e]], b = v[t[i + (e + 1) % 3]];
                    if ((a.z < z0 && b.z < z0) || (a.z > z1 && b.z > z1))
                        continue;
                    // Clip the edge to the slab, keep the y of both clipped ends.
                    float dz = b.z - a.z;
                    float ta = 0f, tb = 1f;
                    if (Mathf.Abs(dz) > 1e-6f)
                    {
                        float t0 = (z0 - a.z) / dz, t1 = (z1 - a.z) / dz;
                        ta = Mathf.Clamp01(Mathf.Min(t0, t1));
                        tb = Mathf.Clamp01(Mathf.Max(t0, t1));
                    }
                    float ya = Mathf.Lerp(a.y, b.y, ta), yb = Mathf.Lerp(a.y, b.y, tb);
                    minY = Mathf.Min(minY, Mathf.Min(ya, yb));
                    maxY = Mathf.Max(maxY, Mathf.Max(ya, yb));
                }
            return maxY >= minY;
        }

        private static bool IsSightName(string name)
        {
            string n = name.ToLowerInvariant();
            return n.Contains("ironsight") || n.Contains("_optic_") || n.EndsWith("_sight")
                   || n.Contains("_sight_") || n.Contains("rearsight") || n.Contains("frontsight");
        }

        /// <summary>The model's own iron sights (topmost matching transforms only).</summary>
        private static System.Collections.Generic.List<Transform> IronSights(Transform holder)
        {
            var list = new System.Collections.Generic.List<Transform>();
            foreach (var t in holder.GetComponentsInChildren<Transform>())
                if (IsSightName(t.name) && (t.parent == null || !IsSightName(t.parent.name)))
                    list.Add(t);
            return list;
        }

        private static bool UnderSight(Transform t)
        {
            for (; t != null; t = t.parent)
                if (IsSightName(t.name)) return true;
            return false;
        }

        /// <summary>Where the trigger hand is along the barrel (holder z).</summary>
        private static float TriggerZ(Transform holder, System.Collections.Generic.List<PartBox> parts, Transform magazine)
        {
            foreach (var p in parts)
                if (p.transform.name.EndsWith("_trigger", System.StringComparison.OrdinalIgnoreCase))
                    return p.bounds.center.z;
            var hand = FindDeepChild(holder, "RightHandGrip");
            if (hand != null) return holder.InverseTransformPoint(hand.position).z + 0.06f;
            if (magazine != null) return BoundsIn(holder, magazine).center.z - 0.06f;
            return 0.02f; // weapons are fitted with the trigger near the holder origin
        }

        /// <summary>
        /// Front end of the barrel: the model's foremost point around the bore
        /// axis, with x/y re-centred on the cross-section found there.
        /// </summary>
        private static Vector3 BarrelTip(System.Collections.Generic.List<PartBox> parts, Vector3 muzzle)
        {
            const float band = 0.035f;
            float tip = float.MinValue;
            foreach (var p in parts)
            {
                if (p.points == null) continue;
                foreach (var v in p.points)
                    if (Mathf.Abs(v.x - muzzle.x) < band && Mathf.Abs(v.y - muzzle.y) < band
                        && v.z > muzzle.z - 0.3f && v.z > tip)
                        tip = v.z;
            }
            if (tip == float.MinValue)
                return muzzle;

            // Cross-section of the last centimetre of barrel.
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            foreach (var p in parts)
            {
                if (p.points == null) continue;
                foreach (var v in p.points)
                    if (v.z > tip - 0.01f && Mathf.Abs(v.x - muzzle.x) < band && Mathf.Abs(v.y - muzzle.y) < band)
                    {
                        min = Vector2.Min(min, new Vector2(v.x, v.y));
                        max = Vector2.Max(max, new Vector2(v.x, v.y));
                    }
            }
            return new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, tip);
        }

        /// <summary>Highest point of the model within [z0, z1], iron sights excluded.</summary>
        private static float TopAt(System.Collections.Generic.List<PartBox> parts, float z0, float z1, float fallback)
        {
            float top = float.MinValue;
            foreach (var p in parts)
            {
                if (!UnderSight(p.transform) && SliceY(p, z0, z1, out _, out float maxY))
                    top = Mathf.Max(top, maxY);
            }
            return top > float.MinValue ? top : fallback;
        }

        /// <summary>Lowest point of the model within [z0, z1], magazine and parts deeper than <paramref name="floor"/> excluded.</summary>
        private static float BottomAt(System.Collections.Generic.List<PartBox> parts, Transform magazine, float z0, float z1, float fallback, float floor)
        {
            float bottom = float.MaxValue;
            foreach (var p in parts)
            {
                if (magazine != null && p.transform.IsChildOf(magazine)) continue;
                // Parts reaching below the floor (bipod legs, a P90's loop) are skipped.
                if (SliceY(p, z0, z1, out float minY, out _) && minY >= floor)
                    bottom = Mathf.Min(bottom, minY);
            }
            return bottom < float.MaxValue ? bottom : fallback;
        }

        /// <summary>
        /// Extended mag = the weapon's own magazine, longer: scaled along its
        /// long axis, the end in the magazine well kept in place.
        /// </summary>
        private static void StretchMagazine(Transform holder, Transform magazine, float factor)
        {
            Bounds local = default;
            bool any = false;
            foreach (var filter in magazine.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                Matrix4x4 toMag = magazine.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var mb = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = toMag.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (any) local.Encapsulate(corner); else local = new Bounds(corner, Vector3.zero);
                    any = true;
                }
            }
            if (!any) return;

            // Long axis in the magazine's own space; its upper end (holder y) is the well.
            int axis = local.size.x >= local.size.y && local.size.x >= local.size.z ? 0 : local.size.y >= local.size.z ? 1 : 2;
            Vector3 dir = Vector3.zero;
            dir[axis] = local.extents[axis];
            Vector3 endA = local.center + dir, endB = local.center - dir;
            Vector3 well = holder.InverseTransformPoint(magazine.TransformPoint(endA)).y
                           >= holder.InverseTransformPoint(magazine.TransformPoint(endB)).y ? endA : endB;

            Vector3 before = magazine.TransformPoint(well);
            var scale = magazine.localScale;
            scale[axis] *= factor;
            magazine.localScale = scale;
            magazine.position += before - magazine.TransformPoint(well);
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
                case WeaponModelType.Knife:
                    return new ModelFit
                    {
                        resource = "Weapons/Modern/Knife",
                        scale = new Vector3(0.2f, 0.2f, 0.2f),
                        euler = Vector3.zero,
                        offset = Vector3.zero,
                        muzzle = new Vector3(0, 0, 0.22f)
                    };
                case WeaponModelType.Pistol:
                    return new ModelFit
                    {
                        resource = "Weapons/Modern/Pistol",
                        scale = new Vector3(0.2f, 0.2f, 0.2f),
                        euler = Vector3.zero,
                        offset = new Vector3(0, -0.055f, -0.03f),
                        muzzle = new Vector3(0, 0.018f, 0.26f)
                    };
                case WeaponModelType.Smg:
                    return new ModelFit
                    {
                        resource = "Weapons/Modern/SMG",
                        scale = new Vector3(0.2f, 0.2f, 0.2f),
                        euler = Vector3.zero,
                        offset = new Vector3(0, -0.075f, -0.1f),
                        muzzle = new Vector3(0, 0.035f, 0.43f)
                    };
                case WeaponModelType.Shotgun:
                    return new ModelFit
                    {
                        resource = "Weapons/Modern/ShotGun",
                        // This prefab's mesh children already carry a 1.25 scale.
                        scale = new Vector3(0.16f, 0.16f, 0.16f),
                        euler = Vector3.zero,
                        offset = new Vector3(0, -0.09f, -0.12f),
                        muzzle = new Vector3(0, 0.035f, 0.56f)
                    };
                case WeaponModelType.Rifle:
                    return new ModelFit
                    {
                        resource = "Weapons/Modern/AssaultRifle",
                        scale = new Vector3(0.2f, 0.2f, 0.2f),
                        euler = Vector3.zero,
                        offset = new Vector3(0, -0.08f, -0.12f),
                        // Rifle_1's barrel sits above the pack origin. The old
                        // value (y = .02) emitted tracers below the real muzzle.
                        muzzle = new Vector3(0, 0.045f, 0.5f)
                    };
                case WeaponModelType.Sniper:
                    return new ModelFit
                    {
                        resource = "Weapons/Modern/SniperRifle",
                        scale = new Vector3(0.2f, 0.2f, 0.2f),
                        euler = Vector3.zero,
                        offset = new Vector3(0, -0.07f, -0.15f),
                        muzzle = new Vector3(0, 0.04f, 0.55f)
                    };
                case WeaponModelType.Rpg:
                    return new ModelFit
                    {
                        resource = "Weapons/Modern/RocketLauncher",
                        scale = new Vector3(0.18f, 0.18f, 0.18f),
                        euler = Vector3.zero,
                        offset = new Vector3(0, -0.09f, -0.1f),
                        muzzle = new Vector3(0, 0.005f, 0.52f)
                    };
                case WeaponModelType.Lmg:
                    return new ModelFit
                    {
                        resource = "Weapons/Modern/LMG",
                        scale = new Vector3(0.2f, 0.2f, 0.2f),
                        euler = Vector3.zero,
                        offset = new Vector3(0, -0.08f, -0.12f),
                        muzzle = new Vector3(0, 0.04f, 0.52f)
                    };
                case WeaponModelType.GrenadeLauncher:
                    // The packs have no usable grenade launcher (the RocketLauncher
                    // made it look like the RPG; the ithappy launchers are URP-only),
                    // so it uses the procedural break-action launcher (BuildGrenadeLauncher).
                    return new ModelFit { resource = null };
                default:
                    return new ModelFit { resource = null };
            }
        }

        /// <summary>
        /// Fit for weapons that name a specific imported model
        /// (<see cref="WeaponDefinition.modelName"/>). Same rules as FitFor: one row
        /// per model, tweak here. Each offset lines the model's trigger up with
        /// the hand position of the tuned weapon of the same family (rifle / SMG /
        /// sniper / pistol); the muzzle sits at the measured barrel tip. The
        /// "Modern" pack is centimetre-built (scale 0.2 like the base arsenal),
        /// the AR_/Pistol_/SMG_/Recon_ packs are metre-built (scale 0.85).
        /// </summary>
        private static ModelFit FitForModel(string modelName)
        {
            switch (modelName)
            {
                case "Modern/Rifle_2_Used": // AK-12 style rifle
                    return Fit("Weapons/Modern/Rifle_2_Used", 0.2f, new Vector3(0, -0.084f, -0.12f), new Vector3(0, 0.005f, 0.709f));
                case "Modern/Rifle_11_Used": // AKM
                    return Fit("Weapons/Modern/Rifle_11_Used", 0.2f, new Vector3(0, -0.093f, -0.128f), new Vector3(0, 0.03f, 0.847f));
                case "Modern/Rifle_10_Used": // SCAR-H style
                    return Fit("Weapons/Modern/Rifle_10_Used", 0.2f, new Vector3(0, -0.077f, -0.118f), new Vector3(0, 0.01f, 0.599f));
                case "Modern/Rifle_9_Used": // M16 style
                    return Fit("Weapons/Modern/Rifle_9_Used", 0.2f, new Vector3(0, -0.081f, -0.122f), new Vector3(0, 0.02f, 0.885f));
                case "Modern/Rifle_4_Used": // VSS style (integral suppressor)
                    return Fit("Weapons/Modern/Rifle_4_Used", 0.2f, new Vector3(0, -0.069f, -0.122f), new Vector3(0, 0.007f, 0.669f));
                case "Modern/Rifle_14_Used": // SVD style
                    return Fit("Weapons/Modern/Rifle_14_Used", 0.2f, new Vector3(0, -0.071f, -0.167f), new Vector3(0, 0.003f, 1.246f));
                case "Modern/Rifle_3_Used": // Uzi style
                    return Fit("Weapons/Modern/Rifle_3_Used", 0.2f, new Vector3(0, -0.105f, -0.116f), new Vector3(0, -0.025f, 0.168f));
                case "Modern/Rifle_7_Used": // MP7 style
                    return Fit("Weapons/Modern/Rifle_7_Used", 0.2f, new Vector3(0, -0.094f, -0.096f), new Vector3(0, -0.048f, 0.145f));
                case "Modern/Rifle_8_Used": // P90 style
                    return Fit("Weapons/Modern/Rifle_8_Used", 0.2f, new Vector3(0, -0.088f, -0.081f), new Vector3(0, -0.037f, 0.126f));
                case "Modern/Pistol_1_Used": // Glock style
                    return Fit("Weapons/Modern/Pistol_1_Used", 0.2f, new Vector3(0, -0.068f, -0.032f), new Vector3(0, -0.002f, 0.138f));
                case "Modern/Pistol_3_Used": // Five-seveN style
                    return Fit("Weapons/Modern/Pistol_3_Used", 0.2f, new Vector3(0, -0.057f, -0.029f), new Vector3(0, 0.006f, 0.141f));
                case "Modern/Pistol_4_Used": // Desert Eagle style
                    return Fit("Weapons/Modern/Pistol_4_Used", 0.2f, new Vector3(0, -0.084f, -0.039f), new Vector3(0, 0.024f, 0.235f));
                case "AR_T": // Galil style
                    return Fit("Weapons/AR_T", 0.85f, new Vector3(0, -0.053f, -0.056f), new Vector3(0, 0.004f, 0.604f));
                case "AR_U": // SG 552 style
                    return Fit("Weapons/AR_U", 0.85f, new Vector3(0, -0.053f, -0.071f), new Vector3(0, 0.002f, 0.356f));
                case "AR_W": // AR-15 carbine
                    return Fit("Weapons/AR_W", 0.85f, new Vector3(0, -0.054f, -0.075f), new Vector3(0, 0.014f, 0.526f));
                case "AR_X": // AK-74 style
                    return Fit("Weapons/AR_X", 0.85f, new Vector3(0, -0.053f, -0.056f), new Vector3(0, -0.008f, 0.647f));
                case "Pistol_P": // Micro Uzi style
                    return Fit("Weapons/Pistol_P", 0.85f, new Vector3(0, -0.028f, 0.007f), new Vector3(0, 0.003f, 0.106f));
                case "SMG_P": // modern 9mm SMG
                    return Fit("Weapons/SMG_P", 0.85f, new Vector3(0, -0.067f, -0.081f), new Vector3(0, -0.018f, 0.26f));
                case "ShotGun_P": // pump shotgun
                    return Fit("Weapons/ShotGun_P", 0.85f, new Vector3(0, -0.049f, -0.073f), new Vector3(0, -0.017f, 0.565f));
                case "Recon_P": // bolt-action sniper
                    return Fit("Weapons/Recon_P", 0.85f, new Vector3(0, -0.07f, -0.116f), new Vector3(0, -0.006f, 0.651f));
                case "Launcher_G": // Barrett style .50 (despite the pack name)
                    return Fit("Weapons/Launcher_G", 0.85f, new Vector3(0, -0.072f, -0.115f), new Vector3(0, -0.016f, 0.61f));
                case "Pistol_R": // revolver
                    return Fit("Weapons/Pistol_R", 0.85f, new Vector3(0, -0.029f, 0.008f), new Vector3(0, 0.011f, 0.243f));
                default:
                    // Unknown name: still try to load it with a neutral rifle fit.
                    return Fit("Weapons/" + modelName, 0.2f, new Vector3(0, -0.08f, -0.12f), new Vector3(0, 0.02f, 0.5f));
            }
        }

        private static ModelFit Fit(string resource, float scale, Vector3 offset, Vector3 muzzle)
            => new ModelFit { resource = resource, scale = Vector3.one * scale, euler = Vector3.zero, offset = offset, muzzle = muzzle };

        private static bool TryBuildImported(WeaponDefinition def, Transform holder, out Vector3 muzzlePos)
        {
            muzzlePos = Vector3.zero;
            var fit = string.IsNullOrEmpty(def.modelName) ? FitFor(def.model) : FitForModel(def.modelName);
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
                Strip(col);
            foreach (var rb in model.GetComponentsInChildren<Rigidbody>())
                Strip(rb);

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
                case WeaponModelType.GrenadeLauncher:
                    BuildGrenadeLauncher(t, metal, grip, accent);
                    return new Vector3(0, 0.02f, 0.37f);
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

        private static void BuildGrenadeLauncher(Transform t, Material metal, Material grip, Material accent)
        {
            // Break-action 40 mm launcher: short fat barrel over a wooden forend and stock.
            var wood = MakeMat(new Color(0.42f, 0.28f, 0.16f), 0.05f, 0.4f);
            Part(t, PrimitiveType.Cube, new Vector3(0, 0.01f, -0.02f), Vector3.zero, new Vector3(0.06f, 0.08f, 0.14f), metal);        // breech block
            Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.02f, 0.2f), new Vector3(90, 0, 0), new Vector3(0.056f, 0.16f, 0.056f), metal); // 40 mm barrel
            Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.02f, 0.355f), new Vector3(90, 0, 0), new Vector3(0.064f, 0.012f, 0.064f), accent); // muzzle ring
            Part(t, PrimitiveType.Cube, new Vector3(0, -0.02f, 0.16f), Vector3.zero, new Vector3(0.05f, 0.035f, 0.18f), wood);        // forend
            // Named *_sight so a mounted optic folds them away.
            Part(t, PrimitiveType.Cube, new Vector3(0, 0.072f, 0.04f), Vector3.zero, new Vector3(0.03f, 0.04f, 0.006f), accent).name = "Leaf_sight";
            Part(t, PrimitiveType.Cube, new Vector3(0, 0.056f, 0.33f), Vector3.zero, new Vector3(0.008f, 0.02f, 0.01f), metal).name = "Front_sight";
            Part(t, PrimitiveType.Cube, new Vector3(0, -0.075f, -0.07f), new Vector3(-18, 0, 0), new Vector3(0.04f, 0.1f, 0.05f), grip); // grip
            Part(t, PrimitiveType.Cube, new Vector3(0, -0.03f, -0.24f), new Vector3(-6, 0, 0), new Vector3(0.05f, 0.085f, 0.22f), wood); // stock
            Part(t, PrimitiveType.Cube, new Vector3(0, -0.042f, -0.355f), new Vector3(-6, 0, 0), new Vector3(0.052f, 0.09f, 0.02f), grip); // butt pad
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

        private static GameObject Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 euler, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Strip(go.GetComponent<Collider>()); // weapon models are cosmetic only
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().material = mat;
            return go;
        }

        // Edit-mode builds (preview renders) can't use the deferred Destroy.
        private static void Strip(Object target)
        {
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }

        // Shared cache: weapon models are rebuilt on every switch (Gun Game
        // changes weapon each kill), so fresh materials here would leak.
        private static Material MakeMat(Color color, float metallic, float smoothness)
            => EnvironmentBuilder.SharedMaterial(color, metallic, smoothness);
    }
}
