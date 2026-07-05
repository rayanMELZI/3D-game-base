namespace FpsBase
{
    /// <summary>
    /// Anything that can receive damage. Implemented by <see cref="Health"/>
    /// (offline) and NetworkHealth (multiplayer), so weapons don't need to know
    /// whether they're running online or offline.
    /// </summary>
    public interface IDamageable
    {
        /// <param name="headshot">True when a head hitbox was hit — always lethal.</param>
        void TakeDamage(float amount, bool headshot = false);
    }

    /// <summary>
    /// Read-only health values for UI (health bar). Implemented by both the
    /// offline Health and the networked NetworkHealth.
    /// </summary>
    public interface IHealthSource
    {
        float CurrentHealth { get; }
        float MaxHealth { get; }
    }
}
