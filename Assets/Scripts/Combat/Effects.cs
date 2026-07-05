using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Small code-generated effects: the cube-burst death explosion and helpers.
    /// </summary>
    public static class Effects
    {
        /// <summary>Classic charm: the character bursts into glowing physical cubes.</summary>
        public static void SpawnDeathBurst(Vector3 center, Color color)
        {
            var mat = EnvironmentBuilder.MakeEmissiveMaterial(color, 1.4f);

            for (int i = 0; i < 12; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "DeathCube";
                cube.transform.position = center + Random.insideUnitSphere * 0.35f;
                cube.transform.rotation = Random.rotation;
                cube.transform.localScale = Vector3.one * Random.Range(0.12f, 0.28f);
                cube.GetComponent<Renderer>().material = mat;

                var body = cube.AddComponent<Rigidbody>();
                body.mass = 0.3f;
                body.linearVelocity = Random.insideUnitSphere * 3.5f + Vector3.up * Random.Range(2f, 5f);
                body.angularVelocity = Random.insideUnitSphere * 12f;

                var shrink = cube.AddComponent<TimedShrink>();
                shrink.lifetime = Random.Range(1.1f, 1.7f);
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
