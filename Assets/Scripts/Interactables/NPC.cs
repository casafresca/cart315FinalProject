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
    [Tooltip("If true, the NPC enters combat when hit by a bullet.")]
    [SerializeField] private bool becomeHostileWhenShot = true;

    [Header("2D Sprite Visual (Optional)")]
    [Tooltip("SpriteRenderer used for 2D enemy art in the 3D world.")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [Tooltip("Standing sprite.")]
    [SerializeField] private Sprite idleSprite;
    [Tooltip("Gun-pointing sprite.")]
    [SerializeField] private Sprite attackSprite;

    [Header("2D Walking Animation (Optional)")]
    [Tooltip("Walking frame facing left.")]
    [SerializeField] private Sprite walkLeftSprite;
    [Tooltip("Walking frame facing right.")]
    [SerializeField] private Sprite walkRightSprite;
    [Tooltip("How fast the two walk frames alternate.")]
    [SerializeField] private float walkAnimFps = 6f;
    [Tooltip("Minimum movement speed before walk animation starts.")]
    [SerializeField] private float movingSpeedThreshold = 0.05f;

    private bool useLeftWalkFrame = true;
    private float nextWalkFrameTime;

    [Header("Sprite FX (Optional)")]
    [Tooltip("Health ratio at or below this value is considered injured.")]
    [SerializeField] private float injuredThreshold = 0.3f;
    [Tooltip("Enable alpha pulse while injured (before becoming ally).")]
    [SerializeField] private bool enableInjuredPulse = true;
    [Tooltip("Pulse speed for injured flicker.")]
    [SerializeField] private float injuredPulseSpeed = 4f;
    [Tooltip("Lowest alpha during injured pulse.")]
    [SerializeField, Range(0f, 1f)] private float injuredMinAlpha = 0.45f;
    [Tooltip("Highest alpha during injured pulse.")]
    [SerializeField, Range(0f, 1f)] private float injuredMaxAlpha = 1f;
    [Tooltip("Tint sprite blue when NPC is following.")]
    [SerializeField] private bool enableFollowingTint = true;
    [Tooltip("Color tint used when NPC has joined the player.")]
    [SerializeField] private Color followingTintColor = new Color(0.60f, 0.82f, 1f, 1f);
    [Tooltip("Seconds for blue ally tint to fade in/out.")]
    [SerializeField] private float followingTintFadeDuration = 0.5f;

    private float followingTintBlend;

    [Header("Ghost Mode Preset (Optional)")]
    [Tooltip("Toggle to apply the ghost visual preset values below.")]
    [SerializeField] private bool ghostMode = false;
    [Tooltip("If true, the NPC keeps a ghostly look all the time.")]
    [SerializeField] private bool alwaysGhost = false;
    [Tooltip("Base memory tint while Always Ghost is active.")]
    [SerializeField] private Color alwaysGhostBaseTint = new Color(0.82f, 0.76f, 0.70f, 1f);
    [Tooltip("Base opacity while Always Ghost is active.")]
    [SerializeField, Range(0f, 1f)] private float alwaysGhostBaseAlpha = 0.78f;
    [Tooltip("Enable subtle haunted breathing pulse while Always Ghost is active.")]
    [SerializeField] private bool alwaysGhostBreathingPulse = true;
    [Tooltip("Speed of the Always Ghost breathing pulse.")]
    [SerializeField] private float alwaysGhostPulseSpeed = 1.5f;
    [Tooltip("Strength of the Always Ghost breathing pulse.")]
    [SerializeField, Range(0f, 0.5f)] private float alwaysGhostPulseAmplitude = 0.08f;
    [Tooltip("Ghost pulse speed override.")]
    [SerializeField] private float ghostPulseSpeed = 2.8f;
    [Tooltip("Ghost min alpha override.")]
    [SerializeField, Range(0f, 1f)] private float ghostMinAlpha = 0.25f;
    [Tooltip("Ghost max alpha override.")]
    [SerializeField, Range(0f, 1f)] private float ghostMaxAlpha = 0.72f;
    [Tooltip("Ghost ally tint override.")]
    [SerializeField] private Color ghostFollowingTintColor = new Color(0.78f, 0.92f, 1f, 1f);
    [Tooltip("Ghost tint fade duration override.")]
    [SerializeField] private float ghostTintFadeDuration = 0.8f;
    [Tooltip("If true, injured memories also blend toward blue.")]
    [SerializeField] private bool blueWhenInjured = true;

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

        // Auto-find a sprite renderer on children to reduce inspector setup mistakes.
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        }

        nextWalkFrameTime = Time.time;
        UpdateVisualState();
        UpdateColorEffects();
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

        UpdateVisualState();
        UpdateColorEffects();
    }

    public void TakeDamage(float amount)
    {
        currentHealth -= amount;

        // Optional behavior: being shot can force combat mode on.
        // This is useful for sprite swapping (idle -> attack) and return fire.
        if (becomeHostileWhenShot && !isFollowing && !isCombatActive)
        {
            isCombatActive = true;
            if (npcGun != null) npcGun.SetActive(true);
        }

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
        UpdateVisualState();
    }

    public void StartFollowing()
    {
        isFollowing = true;
        isCombatActive = false;
        if (npcGun != null) npcGun.SetActive(false);
        if (agent != null) agent.enabled = true;
        if (rb != null) rb.isKinematic = true;
        UpdateVisualState();
    }

    protected override void Interact()
    {
        if (isFollowing) return;

        if ((currentHealth / maxHealth) <= 0.3f)
        {
            // Pause combat while dialogue is active.
            // DialogueManager will decide outcome after choices:
            // - success -> StartFollowing()
            // - failure -> ResumeCombat()
            isCombatActive = false;
            if (npcGun != null) npcGun.SetActive(false);
            UpdateVisualState();

            DialogueManager.GetInstance().EnterDialogueMode(inkJSON, this);
        }
        else if (!isCombatActive)
        {
            isCombatActive = true;
            if (npcGun != null) npcGun.SetActive(true);
            UpdateVisualState();
        }
    }

    private void UpdateVisualState()
    {
        if (spriteRenderer == null)
        {
            return;
        }

        // Attack image shows when hostile combat is active and NPC is not in follow mode.
        bool showAttack = isCombatActive && !isFollowing;
        bool canAnimateWalk = walkLeftSprite != null && walkRightSprite != null;
        bool isMoving = agent != null && agent.enabled && agent.velocity.sqrMagnitude > (movingSpeedThreshold * movingSpeedThreshold);

        Sprite next;
        if (showAttack && attackSprite != null)
        {
            next = attackSprite;
        }
        else if (canAnimateWalk && isMoving)
        {
            if (Time.time >= nextWalkFrameTime)
            {
                useLeftWalkFrame = !useLeftWalkFrame;
                nextWalkFrameTime = Time.time + (1f / Mathf.Max(1f, walkAnimFps));
            }

            next = useLeftWalkFrame ? walkLeftSprite : walkRightSprite;
        }
        else
        {
            next = idleSprite;
        }

        if (next != null && spriteRenderer.sprite != next)
        {
            spriteRenderer.sprite = next;
        }
    }

    private void UpdateColorEffects()
    {
        if (spriteRenderer == null)
        {
            return;
        }

        float healthRatio = maxHealth > 0f ? currentHealth / maxHealth : 1f;
        bool isInjured = healthRatio <= injuredThreshold;

        // Injured pulse applies only before ally/follow state.
        float alpha = 1f;
        float activePulseSpeed = ghostMode ? ghostPulseSpeed : injuredPulseSpeed;
        float activeMinAlpha = ghostMode ? ghostMinAlpha : injuredMinAlpha;
        float activeMaxAlpha = ghostMode ? ghostMaxAlpha : injuredMaxAlpha;
        if (enableInjuredPulse && isInjured && !isFollowing)
        {
            float pulse01 = (Mathf.Sin(Time.time * activePulseSpeed) + 1f) * 0.5f;
            alpha = Mathf.Lerp(activeMinAlpha, activeMaxAlpha, pulse01);
        }

        bool shouldBlueTint = enableFollowingTint && (isFollowing || (blueWhenInjured && isInjured));
        float targetTintBlend = shouldBlueTint ? 1f : 0f;
        float activeFadeDuration = ghostMode ? ghostTintFadeDuration : followingTintFadeDuration;
        float fadeSpeed = activeFadeDuration > 0.001f ? (1f / activeFadeDuration) : 999f;
        followingTintBlend = Mathf.MoveTowards(followingTintBlend, targetTintBlend, fadeSpeed * Time.deltaTime);

        Color activeFollowTint = ghostMode ? ghostFollowingTintColor : followingTintColor;
        bool useGhostBase = ghostMode || alwaysGhost;
        Color baseTint = useGhostBase ? alwaysGhostBaseTint : Color.white;
        float baseAlpha = useGhostBase ? alwaysGhostBaseAlpha : 1f;

        if (useGhostBase && alwaysGhostBreathingPulse)
        {
            float breath01 = (Mathf.Sin(Time.time * alwaysGhostPulseSpeed) + 1f) * 0.5f;
            baseAlpha += Mathf.Lerp(-alwaysGhostPulseAmplitude, alwaysGhostPulseAmplitude, breath01);
            baseAlpha = Mathf.Clamp01(baseAlpha);
        }

        Color tint = Color.Lerp(baseTint, activeFollowTint, followingTintBlend);
        tint.a *= baseAlpha * alpha;
        spriteRenderer.color = tint;
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
