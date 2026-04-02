using UnityEngine;

public class NPCShoot : MonoBehaviour
{
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private Transform bulletSpawn;
    [SerializeField] private float fireRate = 1.2f;
    [SerializeField] private float bulletVelocity = 25f;
    [SerializeField] private float aimAccuracyThreshold = 0.9f; // 1.0 is a perfect aim

    private float nextFireTime;
    private NPC npc;
    private Transform playerTransform;

    void Start()
    {
        npc = GetComponentInParent<NPC>();

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) playerTransform = player.transform;
    }

    void Update()
    {
        if (npc != null && npc.isCombatActive && playerTransform != null)
        {
            // Calculate if the NPC is actually facing the player before shooting
            Vector3 dirToPlayer = (playerTransform.position - transform.position).normalized;
            float alignment = Vector3.Dot(transform.forward, dirToPlayer);

            // Only fire if the cooldown is over AND we are reasonably aimed at the player
            if (Time.time > nextFireTime && alignment > aimAccuracyThreshold)
            {
                Fire();
                nextFireTime = Time.time + fireRate;
            }
        }
    }

    void Fire()
    {
        if (bulletPrefab == null || bulletSpawn == null) return;

        GameObject bulletObj = Instantiate(bulletPrefab, bulletSpawn.position, bulletSpawn.rotation);

        // --- THE FIX: Ignore the NPC's Collider ---
        // We look for the collider on the root object (the NPC body)
        Collider npcCollider = npc.GetComponent<Collider>();
        Collider bulletCollider = bulletObj.GetComponent<Collider>();

        if (npcCollider != null && bulletCollider != null)
        {
            Physics.IgnoreCollision(bulletCollider, npcCollider);
        }

        // --- SHOOTER IDENTIFICATION ---
        Bullet bulletScript = bulletObj.GetComponent<Bullet>();
        if (bulletScript != null)
        {
            bulletScript.shooter = npc.gameObject;
        }

        // --- PHYSICS ---
        Rigidbody rb = bulletObj.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.AddForce(bulletSpawn.forward * bulletVelocity, ForceMode.Impulse);
        }

        // Cleanup bullet if it flies into the void

        // First shot after vulnerable state cancels flicker (combat resumed).
        npc.NotifyFiredAgain();
        Destroy(bulletObj, 5f);
    }
}
