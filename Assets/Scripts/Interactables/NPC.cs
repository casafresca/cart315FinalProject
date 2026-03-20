using UnityEngine;
using Ink.Runtime;
using UnityEngine.AI;

public class NPC : Interactable
{
    [SerializeField] private TextAsset inkJSON;
    private NavMeshAgent agent;
    private Transform playerTransform;
    private Rigidbody rb;

    public bool isFollowing = false;
    public bool isCombatActive = false;

    [Header("Combat & Health")]
    public float maxHealth = 100f;
    public float currentHealth;
    public GameObject npcGun;

    [Header("Movement Settings")]
    [SerializeField] private float stoppingDistance = 2.5f;

    void Start()
    {
        currentHealth = maxHealth;
        if (npcGun != null) npcGun.SetActive(false);

        agent = GetComponent<NavMeshAgent>();
        rb = GetComponent<Rigidbody>();

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) playerTransform = player.transform;

        if (agent != null)
        {
            agent.enabled = false;
            agent.stoppingDistance = stoppingDistance;
        }
    }

    void Update()
    {
        // TRACKING: Face the player during combat
        if (isCombatActive && playerTransform != null)
        {
            Vector3 direction = (playerTransform.position - transform.position).normalized;
            direction.y = 0;
            if (direction != Vector3.zero)
            {
                Quaternion targetRot = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 5f);
            }
        }

        if (isFollowing && agent != null && agent.enabled)
        {
            HandleFollowingLogic();
        }
    }

    public void TakeDamage(float amount)
    {
        currentHealth -= amount;
        if (isCombatActive && (currentHealth / maxHealth) <= 0.3f)
        {
            isCombatActive = false;
            if (npcGun != null) npcGun.SetActive(false);
        }
    }

    public void ResumeCombat()
    {
        isCombatActive = true;
        currentHealth = maxHealth;
        if (npcGun != null) npcGun.SetActive(true);
    }

    public void StartFollowing()
    {
        isFollowing = true;
        isCombatActive = false;
        if (npcGun != null) npcGun.SetActive(false);
        if (agent != null) agent.enabled = true;
        if (rb != null) rb.isKinematic = true;
    }

    protected override void Interact()
    {
        if (isFollowing) return;

        if ((currentHealth / maxHealth) <= 0.3f)
        {
            DialogueManager.GetInstance().EnterDialogueMode(inkJSON, this);
        }
        else if (!isCombatActive)
        {
            isCombatActive = true;
            if (npcGun != null) npcGun.SetActive(true);
        }
    }

    private void HandleFollowingLogic()
    {
        // Your existing Y-axis floor fix
        if (transform.position.y < 0.15f)
        {
            Vector3 fixedPos = transform.position;
            fixedPos.y = 0.16f;
            transform.position = fixedPos;
        }

        if (playerTransform != null)
        {
            agent.SetDestination(playerTransform.position);
            agent.isStopped = Vector3.Distance(transform.position, playerTransform.position) < stoppingDistance;
        }
    }
}
