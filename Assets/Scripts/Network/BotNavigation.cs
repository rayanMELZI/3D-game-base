using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace FpsBase
{
    /// <summary>
    /// Server-only navigation for bots. Maps are built from code (or loaded
    /// from prefabs) at runtime, so there is no baked NavMesh: this bakes one
    /// in the background from the map's colliders every time the map changes,
    /// with the built-in UnityEngine.AI builder (no extra package). Bots only
    /// ask it for paths and walk them with their own CharacterController.
    /// Until the bake is done (or if a map can't be baked) bots fall back to
    /// direct steering with obstacle probes.
    /// </summary>
    public static class BotNavigation
    {
        private static NavMeshData data;
        private static NavMeshDataInstance instance;
        private static AsyncOperation pending;

        /// <summary>The map index the current NavMesh was baked for (-1 = none).</summary>
        public static int BuiltMap { get; private set; } = -1;

        public static bool Ready => data != null && pending != null && pending.isDone;

        /// <summary>Bakes the active map (found by name). Safe to call again: replaces the old mesh.</summary>
        public static void Rebuild(int mapIndex)
        {
            Clear();
            BuiltMap = mapIndex;
            var root = GameObject.Find(EnvironmentBuilder.MapRootName);
            if (root == null)
                return;

            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(root.transform, Physics.DefaultRaycastLayers,
                NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
            if (sources.Count == 0)
                return;

            // Bounds of everything solid in the map (triggers are pads/markers).
            bool any = false;
            var bounds = new Bounds();
            foreach (var col in root.GetComponentsInChildren<Collider>())
            {
                if (col.isTrigger)
                    continue;
                if (!any) { bounds = col.bounds; any = true; }
                else bounds.Encapsulate(col.bounds);
            }
            if (!any)
                return;
            bounds.Expand(new Vector3(2f, 4f, 2f));

            // Sized like a player: capsule radius 0.35, 1.75 tall with the head,
            // same step height and slope limit as the CharacterController.
            var settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = 0.4f;
            settings.agentHeight = 1.8f;
            settings.agentClimb = 0.45f;
            settings.agentSlope = 48f;

            data = new NavMeshData(settings.agentTypeID);
            instance = NavMesh.AddNavMeshData(data);
            pending = NavMeshBuilder.UpdateNavMeshDataAsync(data, settings, sources, bounds);
        }

        public static void Clear()
        {
            if (instance.valid)
                instance.Remove();
            instance = default;
            data = null;
            pending = null;
            BuiltMap = -1;
        }

        /// <summary>
        /// Path corners from <paramref name="from"/> to <paramref name="to"/> (both snapped
        /// onto the mesh). False when there is no mesh yet or no way there.
        /// </summary>
        public static bool TryPath(Vector3 from, Vector3 to, NavMeshPath path, List<Vector3> corners)
        {
            corners.Clear();
            if (!Ready)
                return false;
            if (!NavMesh.SamplePosition(from, out var start, 2f, NavMesh.AllAreas)
                || !NavMesh.SamplePosition(to, out var end, 4f, NavMesh.AllAreas))
                return false;
            if (!NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path)
                || path.status == NavMeshPathStatus.PathInvalid)
                return false;
            corners.AddRange(path.corners);
            return corners.Count > 0;
        }

        /// <summary>A random reachable point on the mesh near <paramref name="center"/>.</summary>
        public static bool RandomPoint(Vector3 center, float radius, out Vector3 point)
        {
            point = center;
            if (!Ready)
                return false;
            for (int i = 0; i < 6; i++)
            {
                Vector2 offset = Random.insideUnitCircle * radius;
                if (NavMesh.SamplePosition(center + new Vector3(offset.x, 0f, offset.y), out var hit, 3f, NavMesh.AllAreas))
                {
                    point = hit.position;
                    return true;
                }
            }
            return false;
        }
    }
}
