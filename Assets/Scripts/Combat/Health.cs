using System;
using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Generic health component usable by the player, target dummies,
    /// enemies, destructibles — anything that can take damage.
    /// </summary>
    public class Health : MonoBehaviour
    {
        public float maxHealth = 100f;

        public float Current { get; private set; }
        public bool IsDead => Current <= 0f;

        /// <summary>Fired when health reaches zero.</summary>
        public event Action OnDeath;

        /// <summary>Fired every time damage is taken (amount).</summary>
        public event Action<float> OnDamaged;

        private void Awake()
        {
            Current = maxHealth;
        }

        public void TakeDamage(float amount)
        {
            if (IsDead)
                return;

            Current = Mathf.Max(0f, Current - amount);
            OnDamaged?.Invoke(amount);

            if (IsDead)
                OnDeath?.Invoke();
        }

        public void ResetHealth()
        {
            Current = maxHealth;
        }
    }
}
