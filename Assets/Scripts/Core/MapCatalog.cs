using System.Collections.Generic;
using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// The list of playable maps = a couple of built-in code maps plus every
    /// custom map prefab the user dropped into Resources/Maps. Custom maps are
    /// sorted by name so the index is identical on every client (map choice is
    /// synced by index over the network).
    ///
    /// A custom map is just a prefab in Assets/Resources/Maps/ containing your
    /// geometry (anything with colliders) and some SpawnPoint markers.
    /// Tools > FPS Base > New Map creates a starter one.
    /// </summary>
    public static class MapCatalog
    {
        private enum Kind { Arena, Backrooms, Nuketown, Custom }

        private struct Entry
        {
            public string name;
            public Kind kind;
            public GameObject prefab; // custom only
        }

        private static List<Entry> entries;

        public static int Count
        {
            get { EnsureLoaded(); return entries.Count; }
        }

        public static string Name(int index)
        {
            EnsureLoaded();
            return entries[Mathf.Clamp(index, 0, entries.Count - 1)].name;
        }

        public static int Clamp(int index)
        {
            EnsureLoaded();
            return Mathf.Clamp(index, 0, entries.Count - 1);
        }

        /// <summary>Force a re-scan (e.g. after adding a map in the editor).</summary>
        public static void Refresh() => entries = null;

        private static void EnsureLoaded()
        {
            if (entries != null)
                return;

            entries = new List<Entry>
            {
                new Entry { name = "ARENA", kind = Kind.Arena },
                new Entry { name = "BACKROOMS", kind = Kind.Backrooms },
                new Entry { name = "NUKETOWN 2025", kind = Kind.Nuketown },
            };

            // Custom map prefabs, sorted for a stable cross-client index order.
            var prefabs = Resources.LoadAll<GameObject>("Maps");
            System.Array.Sort(prefabs, (a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (var prefab in prefabs)
                entries.Add(new Entry { name = prefab.name.ToUpperInvariant(), kind = Kind.Custom, prefab = prefab });
        }

        // ------------------------------------------------------------------
        // Building
        // ------------------------------------------------------------------

        /// <summary>Builds the map under a fresh MapRoot object (destroy the old one first).</summary>
        public static void Build(int index)
        {
            EnsureLoaded();
            var entry = entries[Clamp(index)];
            switch (entry.kind)
            {
                case Kind.Arena:
                    EnvironmentBuilder.BuildArena(60f);
                    break;
                case Kind.Backrooms:
                    EnvironmentBuilder.BuildBackrooms();
                    break;
                case Kind.Nuketown:
                    EnvironmentBuilder.BuildNuketown();
                    break;
                default:
                    var root = new GameObject(EnvironmentBuilder.MapRootName);
                    var instance = Object.Instantiate(entry.prefab, root.transform);
                    instance.transform.localPosition = Vector3.zero;
                    FixMissingMaterials(instance);
                    break;
            }
        }

        /// <summary>
        /// Custom prefabs saved from a play-mode copy lose their code-generated
        /// materials (they only exist at runtime) and render pink. Swap any
        /// missing material for a neutral gray so the map is always playable.
        /// (Tools > FPS Base > Bake Built-in Map saves real materials instead.)
        /// </summary>
        private static void FixMissingMaterials(GameObject instance)
        {
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var mats = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null)
                    {
                        mats[i] = EnvironmentBuilder.SharedMaterial(new Color(0.55f, 0.55f, 0.58f), 0f, 0.4f);
                        changed = true;
                    }
                }
                if (changed)
                    renderer.sharedMaterials = mats;
            }
        }

        // ------------------------------------------------------------------
        // Spawn points
        // ------------------------------------------------------------------

        /// <summary>
        /// Candidate spawn positions for a side (0/1), or all of them for side = -1
        /// (free-for-all / gun game). Custom maps read their SpawnPoint markers.
        /// </summary>
        public static Vector3[] SpawnCandidates(int index, int side)
        {
            EnsureLoaded();
            var entry = entries[Clamp(index)];

            if (entry.kind == Kind.Custom)
            {
                var markers = entry.prefab.GetComponentsInChildren<SpawnPoint>(true);
                var result = new List<Vector3>();
                foreach (var marker in markers)
                    if (side < 0 || marker.team < 0 || marker.team == side)
                        result.Add(marker.transform.position); // prefab-local == world at origin
                if (result.Count > 0)
                    return result.ToArray();
                // No markers placed: fall back to a safe ring so nobody voids.
                return DefaultRing();
            }

            // Built-in maps.
            Vector3[] ForSide(int s) => entry.kind == Kind.Nuketown
                ? EnvironmentBuilder.NuketownSpawnCandidates(s)
                : EnvironmentBuilder.BuiltinSpawnCandidates(entry.kind == Kind.Backrooms, s);
            if (side < 0)
            {
                var a = ForSide(0);
                var b = ForSide(1);
                var all = new Vector3[a.Length + b.Length];
                a.CopyTo(all, 0);
                b.CopyTo(all, a.Length);
                return all;
            }
            return ForSide(side);
        }

        private static Vector3[] DefaultRing()
        {
            return new[]
            {
                new Vector3(0, 0.2f, 10f), new Vector3(10f, 0.2f, 0), new Vector3(0, 0.2f, -10f),
                new Vector3(-10f, 0.2f, 0), new Vector3(7f, 0.2f, 7f), new Vector3(-7f, 0.2f, -7f),
            };
        }
    }
}
