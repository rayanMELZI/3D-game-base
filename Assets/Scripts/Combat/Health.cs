using System;
using UnityEngine;

namespace FpsBase
{
    /// <summary>
    /// Generic health component usable by the player, target dummies,
    /// enemies, destructibles — anything that can take damage.
    /// </summary>
    public class Health : MonoBehaviour, IDamageable, IHealthSource
    {
        public float maxHealth = 100f;

        public float Current { get; private set; }
        public bool IsDead => Current <= 0f;

        float IHealthSource.CurrentHealth => Current;
        float IHealthSource.MaxHealth => maxHealth;

        /// <summary>Fired when health reaches zero.</summary>
        public event Action OnDeath;

        /// <summary>Fired every time damage is taken (amount).</summary>
        public event Action<float> OnDamaged;

        private void Awake()
        {
            Current = maxHealth;
        }

        public void TakeDamage(float amount, bool headshot = false)
        {
            if (IsDead)
                return;

            if (headshot)
                amount = maxHealth; // headshots are always lethal

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
