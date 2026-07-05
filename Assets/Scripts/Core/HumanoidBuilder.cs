using UnityEngine;

namespace FpsBase
{
    /// <summary>Everything a humanoid body exposes to gameplay code.</summary>
    public struct HumanoidParts
    {
        public GameObject root;             // "Body" container under the character root
        public Renderer[] teamRenderers;    // tinted with the team color
        public Renderer headRenderer;       // dark helmet
        public Renderer visorRenderer;      // glowing visor
        public Renderer chestStripeRenderer;// glowing team stripe
        public Renderer[] headParts;        // hidden in first person (head + visor)
        public Renderer[] allRenderers;
        public Transform armL, armR, legL, legR; // pivots for LimbAnimator
        public GameObject headObject;       // carries the headshot hitbox
    }

    /// <summary>
    /// Builds a stylized humanoid soldier (~1.9m) out of primitives: hips, torso
    /// with a glowing chest stripe, helmet head with glowing visor, swinging
    /// arms and legs, backpack. Materials are applied separately at runtime
    /// (ApplyMaterials) so the body can also be baked into a prefab in the editor.
    /// </summary>
    public static class HumanoidBuilder
    {
        public static HumanoidParts Build(Transform parent, bool addHeadHitbox)
        {
            var parts = new HumanoidParts();

            var body = new GameObject("Body");
            body.transform.SetParent(parent, false);
            parts.root = body;
            var t = body.transform;

            // Torso & hips.
            var hips = Part(t, PrimitiveType.Cube, "Hips", new Vector3(0, 0.95f, 0), new Vector3(0.34f, 0.16f, 0.2f));
            var torso = Part(t, PrimitiveType.Cube, "Torso", new Vector3(0, 1.28f, 0), new Vector3(0.4f, 0.5f, 0.24f));
            var stripe = Part(t, PrimitiveType.Cube, "ChestStripe", new Vector3(0, 1.38f, 0.125f), new Vector3(0.3f, 0.07f, 0.02f));
            var backpack = Part(t, PrimitiveType.Cube, "Backpack", new Vector3(0, 1.3f, -0.18f), new Vector3(0.3f, 0.38f, 0.14f));

            // Head: dark helmet sphere + glowing visor, above the controller capsule
            // so only the head trigger hitbox can catch it (headshots).
            var head = Part(t, PrimitiveType.Sphere, "Head", new Vector3(0, 1.72f, 0), Vector3.one * 0.3f);
            var visor = Part(head.transform, PrimitiveType.Cube, "Visor", new Vector3(0, 0.05f, 0.42f), new Vector3(0.75f, 0.3f, 0.35f));
            parts.headObject = head;

            if (addHeadHitbox)
            {
                var hitboxCollider = head.AddComponent<SphereCollider>();
                hitboxCollider.isTrigger = true; // never blocks movement
                head.AddComponent<Hitbox>().isHead = true;
            }

            // Arms: pivot at the shoulder so LimbAnimator can swing them.
            parts.armL = LimbPivot(t, "ArmL", new Vector3(-0.26f, 1.46f, 0));
            parts.armR = LimbPivot(t, "ArmR", new Vector3(0.26f, 1.46f, 0));
            var armMeshL = Part(parts.armL, PrimitiveType.Cube, "ArmMesh", new Vector3(0, -0.2f, 0), new Vector3(0.1f, 0.42f, 0.12f));
            var armMeshR = Part(parts.armR, PrimitiveType.Cube, "ArmMesh", new Vector3(0, -0.2f, 0), new Vector3(0.1f, 0.42f, 0.12f));
            var handL = Part(parts.armL, PrimitiveType.Sphere, "Hand", new Vector3(0, -0.44f, 0), Vector3.one * 0.11f);
            var handR = Part(parts.armR, PrimitiveType.Sphere, "Hand", new Vector3(0, -0.44f, 0), Vector3.one * 0.11f);

            // Legs: pivot at the hip.
            parts.legL = LimbPivot(t, "LegL", new Vector3(-0.1f, 0.9f, 0));
            parts.legR = LimbPivot(t, "LegR", new Vector3(0.1f, 0.9f, 0));
            var legMeshL = Part(parts.legL, PrimitiveType.Cube, "LegMesh", new Vector3(0, -0.42f, 0), new Vector3(0.13f, 0.84f, 0.16f));
            var legMeshR = Part(parts.legR, PrimitiveType.Cube, "LegMesh", new Vector3(0, -0.42f, 0), new Vector3(0.13f, 0.84f, 0.16f));
            var footL = Part(parts.legL, PrimitiveType.Cube, "Foot", new Vector3(0, -0.85f, 0.04f), new Vector3(0.13f, 0.08f, 0.24f));
            var footR = Part(parts.legR, PrimitiveType.Cube, "Foot", new Vector3(0, -0.85f, 0.04f), new Vector3(0.13f, 0.08f, 0.24f));

            parts.headRenderer = head.GetComponent<Renderer>();
            parts.visorRenderer = visor.GetComponent<Renderer>();
            parts.chestStripeRenderer = stripe.GetComponent<Renderer>();
            parts.teamRenderers = Renderers(hips, torso, backpack, armMeshL, armMeshR, legMeshL, legMeshR);
            parts.headParts = Renderers(head, visor);
            parts.allRenderers = Renderers(
                hips, torso, stripe, backpack, head, visor,
                armMeshL, armMeshR, handL, handR, legMeshL, legMeshR, footL, footR);
            return parts;
        }

        /// <summary>
        /// Runtime material pass (also used by PlayerRigRefs when the prefab wakes up).
        /// Dark hands/feet and helmet, team-colored suit, glowing visor + chest stripe.
        /// </summary>
        public static void ApplyMaterials(
            Renderer[] teamRenderers, Renderer headRenderer, Renderer visorRenderer,
            Renderer chestStripeRenderer, Renderer[] allRenderers, Color teamColor)
        {
            var darkMat = EnvironmentBuilder.MakeMaterial(new Color(0.12f, 0.12f, 0.14f), 0.3f, 0.5f);
            foreach (var r in allRenderers)
                r.material = darkMat; // hands, feet, anything not covered below

            var teamMat = EnvironmentBuilder.MakeMaterial(teamColor, 0.1f, 0.55f);
            foreach (var r in teamRenderers)
                r.material = teamMat;

            if (headRenderer != null)
                headRenderer.material = EnvironmentBuilder.MakeMaterial(new Color(0.13f, 0.14f, 0.16f), 0.5f, 0.7f);
            if (visorRenderer != null)
                visorRenderer.material = EnvironmentBuilder.MakeEmissiveMaterial(new Color(0.65f, 0.92f, 1f), 1.6f);
            if (chestStripeRenderer != null)
                chestStripeRenderer.material = EnvironmentBuilder.MakeEmissiveMaterial(teamColor, 1.8f);
        }

        /// <summary>Retint an already-materialized body with a new team color.</summary>
        public static void ApplyTeamColor(
            Renderer[] teamRenderers, Renderer chestStripeRenderer, Color teamColor)
        {
            foreach (var r in teamRenderers)
                r.material.color = teamColor;
            if (chestStripeRenderer != null)
            {
                chestStripeRenderer.material.color = teamColor;
                chestStripeRenderer.material.SetColor("_EmissionColor", teamColor * 1.8f);
            }
        }

        // ------------------------------------------------------------------

        private static Transform LimbPivot(Transform parent, string name, Vector3 localPos)
        {
            var pivot = new GameObject(name);
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = localPos;
            return pivot.transform;
        }

        private static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 localPos, Vector3 localScale)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            SafeDestroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            return go;
        }

        private static Renderer[] Renderers(params GameObject[] objects)
        {
            var result = new Renderer[objects.Length];
            for (int i = 0; i < objects.Length; i++)
                result[i] = objects[i].GetComponent<Renderer>();
            return result;
        }

        private static void SafeDestroy(Object obj)
        {
            if (Application.isPlaying)
                Object.Destroy(obj);
            else
                Object.DestroyImmediate(obj); // editor path (prefab baking)
        }
    }
}
