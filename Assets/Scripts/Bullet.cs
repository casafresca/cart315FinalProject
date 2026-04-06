using UnityEngine;

public class Bullet : MonoBehaviour
{
    [Header("Bullet Settings")]
    [SerializeField] private float damageAmount = 15f;
    [SerializeField] private float bulletLifeTime = 3f;

    // This is set when the bullet is instantiated by Weapon.cs or NPCShoot.cs
    [HideInInspector] public GameObject shooter;

    private void Start()
    {
        // Safety: ensure the bullet is destroyed eventually even if it hits nothing
        Destroy(gameObject, bulletLifeTime);
    }

    private void OnCollisionEnter(Collision collision)
    {
        // 1. SAFETY CHECK: If we hit the person who fired us, do absolutely nothing.
        // The Physics.IgnoreCollision in your Weapon/NPCShoot scripts should prevent this,
        // but this line is a backup to stop the bullet from destroying itself.
        if (shooter != null && (collision.gameObject == shooter || collision.transform.IsChildOf(shooter.transform)))
        {
            return;
        }

        // 2. Check if we hit an NPC
        NPC npc = collision.gameObject.GetComponentInParent<NPC>();
        if (npc != null)
        {
            // Only deal damage if they are an enemy (not following)
            if (!npc.isFollowing)
            {
                npc.TakeDamage(damageAmount);
                Debug.Log($"Bullet hit NPC: {collision.gameObject.name}");
            }
            // If they are following, we treat them like a teammate and let the bullet pass or destroy
            Destroy(gameObject);
            return;
        }

        // 3. Check if we hit the Player
        // Look for the custom PlayerHitbox script on the object we hit
        PlayerHitbox hitbox = collision.gameObject.GetComponent<PlayerHitbox>();

        if (hitbox != null)
        {
            // Apply the damage. The PlayerHealth script will handle the console logs and respawning!
            hitbox.ApplyDamage(damageAmount);
        }

        // 4. Always destroy the bullet on impact with anything else (walls, floors, etc.)
        Destroy(gameObject);
    }
}
