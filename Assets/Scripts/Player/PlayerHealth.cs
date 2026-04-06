using UnityEngine;
using System; // 1. CRITICAL: You must have this at the top to use "Action"

public class PlayerHealth : MonoBehaviour
{
    [Header("Health Settings")]
    public float maxHealth = 100f;
    public float currentHealth;

    [Header("Respawn Settings")]
    private Vector3 startPosition;
    private Quaternion startRotation;
    private Rigidbody rb;

    // 2. THIS IS THE EVENT (The Loudspeaker). This fixes your error!
    public static event Action OnPlayerRespawn;

    void Awake()
    {
        startPosition = transform.position;
        startRotation = transform.rotation;
        currentHealth = maxHealth;
        rb = GetComponent<Rigidbody>();
    }

    public void ApplyDamage(float amount)
    {
        if (currentHealth <= 0) return;

        currentHealth -= amount;
        Debug.Log($"<color=orange>Player Hit! Remaining: {currentHealth}</color>");

        if (currentHealth <= 0)
        {
            RespawnPlayer();
        }
    }

    void RespawnPlayer()
    {
        Debug.Log("<color=red>Health reached zero. Respawning...</color>");

        // Optional: Turn off CharacterController if you end up using one
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        // Teleport Player
        transform.position = startPosition;
        transform.rotation = startRotation;

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (cc != null) cc.enabled = true;
        currentHealth = maxHealth;

        Debug.Log($"<color=cyan>Player Respawned. Health Reset to: {currentHealth}</color>");

        // 3. TRIGGER THE LOUDSPEAKER. This tells the NPC to reset.
        OnPlayerRespawn?.Invoke();
    }
}