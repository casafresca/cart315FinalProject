using UnityEngine;
using Ink.Runtime;
using UnityEngine.AI;

public class NPC : Interactable
{
    [SerializeField] private TextAsset inkJSON;

    private NavMeshAgent agent;
    private Transform playerTransform;
    private Rigidbody rb; // Stored reference to the Rigidbody
    public bool isFollowing = false;

    [Header("Movement Settings")]
    [SerializeField] private float stoppingDistance = 2.5f;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();

        // --- FIX 1: Assign the Rigidbody here ---
        rb = GetComponent<Rigidbody>();

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerTransform = player.transform;
        }

        if (agent != null)
        {
            agent.enabled = false;
            agent.stoppingDistance = stoppingDistance;
        }
    }

    void Update()
    {
        if (isFollowing && agent != null && agent.enabled)
        {
            // --- FIX 2: Emergency Hard Lock with Null Check ---
            // 0.16f is your floor's Y position from your inspector images
            if (transform.position.y < 0.15f)
            {
                Vector3 fixedPos = transform.position;
                fixedPos.y = 0.16f;
                transform.position = fixedPos;

                // Only try to use rb if it actually exists
                if (rb != null)
                {
                    rb.linearVelocity = Vector3.zero;
                }
            }

            if (playerTransform != null)
            {
                agent.SetDestination(playerTransform.position);

                if (Vector3.Distance(transform.position, playerTransform.position) < stoppingDistance)
                {
                    agent.isStopped = true;
                }
                else
                {
                    agent.isStopped = false;
                }
            }
        }
    }

    public void StartFollowing()
    {
        isFollowing = true;

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            Debug.Log("Rigidbody stabilized and momentum zeroed.");
        }

        if (agent != null)
        {
            agent.enabled = true;

            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 5.0f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
                Debug.Log($"NPC safely grounded on NavMesh at: {hit.position}");
            }
            else
            {
                agent.Warp(transform.position);
                Debug.LogWarning("SamplePosition failed, using standard Warp.");
            }

            agent.isStopped = false;
        }
        Debug.Log("Recruitment successful! NPC is following.");
    }

    protected override void Interact()
    {
        if (isFollowing) return;
        base.Interact();
        DialogueManager.GetInstance().EnterDialogueMode(inkJSON, this);
    }
}
