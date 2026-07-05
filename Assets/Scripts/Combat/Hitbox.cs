using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Marks a collider as a special hit zone. Head hitboxes are trigger
    /// colliders (so they never block movement) that sit above the character
    /// controller capsule — a hit on one is an instant kill.
    /// </summary>
    public class Hitbox : MonoBehaviour
    {
        public bool isHead;
    }
}
