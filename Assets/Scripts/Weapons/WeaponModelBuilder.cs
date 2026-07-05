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
    /// Builds the three weapon models (pistol / assault rifle / sniper) out of
    /// Unity primitives, so the project needs no imported 3D assets.
    /// Used both for the first-person viewmodel and the third-person model
    /// other players see in multiplayer.
    /// </summary>
    public static class WeaponModelBuilder
    {
        public static WeaponModelInstance Build(WeaponDefinition def, Transform parent, Vector3 localPosition, bool castShadows)
        {
            var root = new GameObject(def.displayName + "Model");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;

            var metal = MakeMat(new Color(0.14f, 0.14f, 0.15f), 0.65f, 0.72f);
            var grip = MakeMat(new Color(0.09f, 0.09f, 0.1f), 0.1f, 0.35f);
            var accent = MakeMat(new Color(0.32f, 0.34f, 0.38f), 0.8f, 0.6f);

            Vector3 muzzlePos;
            switch (def.model)
            {
                case WeaponModelType.Pistol:
                    BuildPistol(root.transform, metal, grip, accent);
                    muzzlePos = new Vector3(0, 0.03f, 0.18f);
                    break;
                case WeaponModelType.Sniper:
                    BuildSniper(root.transform, metal, grip, accent);
                    muzzlePos = new Vector3(0, 0.02f, 0.74f);
                    break;
                default:
                    BuildRifle(root.transform, metal, grip, accent);
                    muzzlePos = new Vector3(0, 0.02f, 0.48f);
                    break;
            }

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
            flash.intensity = 4f;
            flash.range = 4f;
            flash.enabled = false;

            return new WeaponModelInstance { root = root, muzzle = muzzle.transform, muzzleFlash = flash };
        }

        // ------------------------------------------------------------------
        // Models (all sized in meters, +Z is the barrel direction)
        // ------------------------------------------------------------------

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

        private static Material MakeMat(Color color, float metallic, float smoothness)
        {
            var mat = new Material(Shader.Find("Standard"));
            mat.color = color;
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Glossiness", smoothness);
            return mat;
        }
    }
}
