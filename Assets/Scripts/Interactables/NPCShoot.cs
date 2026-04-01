using UnityEngine;

public class NPCShoot : MonoBehaviour
{
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private Transform bulletSpawn;
    [SerializeField] private float fireRate = 1.2f;
    [SerializeField] private float bulletVelocity = 25f;

    private float nextFireTime;
    private NPC npc;

    void Start()
    {
        npc = GetComponentInParent<NPC>();
    }

    void Update()
    {
        if (npc != null && npc.isCombatActive && Time.time > nextFireTime)
        {
            Fire();
            nextFireTime = Time.time + fireRate;
        }
    }

    void Fire()
    {
        GameObject bulletObj = Instantiate(bulletPrefab, bulletSpawn.position, bulletSpawn.rotation);

        // --- THE FIX: Ignore the NPC's Collider ---
        Collider npcCollider = GetComponentInParent<Collider>();
        Collider bulletCollider = bulletObj.GetComponent<Collider>();

        if (npcCollider != null && bulletCollider != null)
        {
            Physics.IgnoreCollision(bulletCollider, npcCollider);
        }

        Bullet bulletScript = bulletObj.GetComponent<Bullet>();
        if (bulletScript != null)
        {
            bulletScript.shooter = transform.root.gameObject;
        }

        Rigidbody rb = bulletObj.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.AddForce(bulletSpawn.forward * bulletVelocity, ForceMode.Impulse);
        }
    }
}
