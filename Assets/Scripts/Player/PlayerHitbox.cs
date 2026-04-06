using UnityEngine;

public class PlayerHitbox : MonoBehaviour
{
    // Reference to the health script on the same object
    private PlayerHealth health;

    [Tooltip("Multiplier for damage (e.g., 2.0 for double damage)")]
    public float damageMultiplier = 1.0f;

    void Awake()
    {
        // Automatically find the PlayerHealth script on this object
        health = GetComponent<PlayerHealth>();
    }

    // This is the function the NPC Bullets will call when they hit the player
    public void ApplyDamage(float damageAmount)
    {
        // Send the final damage amount to the PlayerHealth script
        if (health != null)
            health.ApplyDamage(damageAmount * damageMultiplier);
        else
            Debug.LogWarning("PlayerHitbox: No PlayerHealth script found on this object!");
    }
}
