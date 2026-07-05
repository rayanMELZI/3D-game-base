using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Small code-generated effects: cube-burst deaths, explosions and helpers.
    /// </summary>
    public static class Effects
    {
        /// <summary>Classic charm: the character bursts into glowing physical cubes.</summary>
        public static void SpawnDeathBurst(Vector3 center, Color color)
        {
            var mat = EnvironmentBuilder.MakeEmissiveMaterial(color, 1.4f);
            SpawnCubeBurst(center, mat, 12, 3.5f, 5f, 0.12f, 0.28f, 1.1f, 1.7f);
        }

        /// <summary>Explosion: flash light + burst of glowing hot cubes.</summary>
        public static void SpawnExplosion(Vector3 center)
        {
            var flashGo = new GameObject("ExplosionFlash");
            flashGo.transform.position = center + Vector3.up * 0.5f;
            var flash = flashGo.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = new Color(1f, 0.7f, 0.35f);
            flash.intensity = 8f;
            flash.range = 16f;
            Object.Destroy(flashGo, 0.15f);

            var hotMat = EnvironmentBuilder.MakeEmissiveMaterial(new Color(1f, 0.55f, 0.15f), 2.6f);
            SpawnCubeBurst(center, hotMat, 16, 7f, 8f, 0.15f, 0.35f, 0.5f, 0.9f);
        }

        private static void SpawnCubeBurst(
            Vector3 center, Material mat, int count,
            float velocity, float upVelocity, float minSize, float maxSize,
            float minLife, float maxLife)
        {
            for (int i = 0; i < count; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "BurstCube";
                cube.transform.position = center + Random.insideUnitSphere * 0.35f;
                cube.transform.rotation = Random.rotation;
                cube.transform.localScale = Vector3.one * Random.Range(minSize, maxSize);
                cube.GetComponent<Renderer>().material = mat;

                var body = cube.AddComponent<Rigidbody>();
                body.mass = 0.3f;
                body.linearVelocity = Random.insideUnitSphere * velocity + Vector3.up * Random.Range(upVelocity * 0.5f, upVelocity);
                body.angularVelocity = Random.insideUnitSphere * 13f;

                cube.AddComponent<TimedShrink>().lifetime = Random.Range(minLife, maxLife);
            }
        }
    }

    /// <summary>Shrinks an object to nothing over its lifetime, then destroys it.</summary>
    public class TimedShrink : MonoBehaviour
    {
        public float lifetime = 1.5f;

        private Vector3 initialScale;
        private float age;

        private void Start()
        {
            initialScale = transform.localScale;
        }

        private void Update()
        {
            age += Time.deltaTime;
            if (age >= lifetime)
            {
                Destroy(gameObject);
                return;
            }
            transform.localScale = initialScale * (1f - age / lifetime);
        }
    }
}
