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
    public GameObject npcGun; // Drag your 'GunPivot' object here

    [Header("Movement Settings")]
    [SerializeField] private float stoppingDistance = 2.5f;

    [Header("Simple Combat Movement")]
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private float changeDirInterval = 2f;
    private Vector3 moveDirection;
    private float moveTimer;

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

        // Set initial physics state
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }
    }

    void Update()
    {
        // 1. COMBAT BEHAVIOR
        if (isCombatActive && !isFollowing)
        {
            HandleSimpleCombatMovement();
            HandleAiming();
        }

        // 2. FOLLOWING BEHAVIOR (Your original working logic)
        if (isFollowing && agent != null && agent.enabled)
        {
            HandleFollowingLogic();
        }
    }

    private void HandleSimpleCombatMovement()
    {
        moveTimer += Time.deltaTime;
        if (moveTimer >= changeDirInterval)
        {
            float randomX = Random.Range(-1f, 1f);
            float randomZ = Random.Range(-1f, 1f);
            moveDirection = new Vector3(randomX, 0, randomZ).normalized;
            moveTimer = 0;
        }

        // Simple nudge movement
        transform.Translate(moveDirection * moveSpeed * Time.deltaTime, Space.World);
    }

    private void HandleAiming()
    {
        if (playerTransform == null) return;

        // Rotate Body
        Vector3 direction = (playerTransform.position - transform.position).normalized;
        direction.y = 0;
        if (direction != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 10f);
        }

        // Aim GunPivot (Shoulder) with Clamps
        if (npcGun != null)
        {
            npcGun.transform.LookAt(playerTransform.position + Vector3.up * 1.2f);
            Vector3 localRot = npcGun.transform.localEulerAngles;

            if (localRot.x > 180) localRot.x -= 360;
            if (localRot.y > 180) localRot.y -= 360;

            localRot.x = Mathf.Clamp(localRot.x, -30f, 30f);
            localRot.y = Mathf.Clamp(localRot.y, -50f, 50f);

            npcGun.transform.localEulerAngles = new Vector3(localRot.x, localRot.y, 0f);
        }
    }

    public void TakeDamage(float amount)
    {
        currentHealth -= amount;

        // STAGGER: Stop moving and shooting at 30% health
        if (isCombatActive && (currentHealth / maxHealth) <= 0.3f)
        {
            isCombatActive = false;
            moveDirection = Vector3.zero;
            if (npcGun != null) npcGun.SetActive(false);
            Debug.Log("NPC Staggered. Ready for interaction.");
        }
    }

    public void ResumeCombat()
    {
        isCombatActive = true;
        currentHealth = maxHealth;
        if (npcGun != null) npcGun.SetActive(true);
        moveTimer = changeDirInterval;
    }

    public void StartFollowing()
    {
        isFollowing = true;
        isCombatActive = false;
        moveDirection = Vector3.zero;

        if (npcGun != null) npcGun.SetActive(false);

        // Physics Fix: Ignore player so the NPC doesn't get "pushed" away
        if (playerTransform != null)
        {
            Collider pCol = playerTransform.GetComponent<Collider>();
            Collider nCol = GetComponent<Collider>();
            if (pCol != null && nCol != null) Physics.IgnoreCollision(nCol, pCol, true);
        }

        // Safe Enable: Snap to floor before turning on Agent
        NavMeshHit hit;
        if (NavMesh.SamplePosition(transform.position, out hit, 2.0f, NavMesh.AllAreas))
        {
            transform.position = hit.position;
        }

        if (agent != null)
        {
            agent.enabled = true;
            if (agent.isActiveAndEnabled)
            {
                agent.Warp(transform.position); // Ensure the agent is grounded
                agent.updateRotation = true;
            }
        }

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
            ResumeCombat();
        }
    }

    private void HandleFollowingLogic()
    {
        // Your original Y-axis floor fix
        if (transform.position.y < 0.15f)
        {
            Vector3 fixedPos = transform.position;
            fixedPos.y = 0.16f;
            transform.position = fixedPos;
        }

        if (playerTransform != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            agent.SetDestination(playerTransform.position);
            agent.isStopped = Vector3.Distance(transform.position, playerTransform.position) < stoppingDistance;
        }
    }
}
