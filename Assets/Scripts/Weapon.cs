using UnityEngine;

public class Weapon : MonoBehaviour
{
    public GameObject bulletPrefab;
    public Transform bulletSpawn;
    public float bulletVelocity = 30f;
    public float bulletLifetime = 3f;

    void Update()
    {
        // Block shooting if dialogue is active
        if (DialogueManager.GetInstance() != null && DialogueManager.GetInstance().dialogueIsPlaying)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Mouse0))
        {
            FireWeapon();
        }
    }

    private void FireWeapon()
    {
        GameObject bulletObj = Instantiate(bulletPrefab, bulletSpawn.position, bulletSpawn.rotation);

        // --- THE FIX: Ignore the Player's Collider ---
        Collider playerCollider = GetComponentInParent<Collider>();
        Collider bulletCollider = bulletObj.GetComponent<Collider>();

        if (playerCollider != null && bulletCollider != null)
        {
            Physics.IgnoreCollision(bulletCollider, playerCollider);
        }

        // Set shooter reference for the Bullet script logic
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

        Destroy(bulletObj, bulletLifetime);
    }
}