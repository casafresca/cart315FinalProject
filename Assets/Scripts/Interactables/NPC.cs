using UnityEngine;
using Ink.Runtime;
using UnityEngine.AI;

/// <summary>
/// Main enemy/ally brain for the soldier NPC.
/// Handles:
/// - Combat state (shooting on/off)
/// - Dialogue transition at low health
/// - Follow behavior after successful dialogue
/// - Sprite swapping (idle / walk / attack)
/// - Ghost + color FX (injury pulse, blue compliance tint)
/// </summary>
public class NPC : Interactable
{
    // Ink file used when this NPC enters dialogue mode.
    [SerializeField] private TextAsset inkJSON;

    // Cached components and references.
    private NavMeshAgent agent;
    private Transform playerTransform;
    private Rigidbody rb;

    // Runtime states read by other scripts (e.g., NPCShoot, dialogue logic).
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
    [Tooltip("If true, injured memories slowly fluctuate between normal tint and blue tint.")]
    [SerializeField] private bool blueWhenInjured = true;
    [Tooltip("Speed of injured blue fluctuation.")]
    [SerializeField] private float injuredBlueFluctuationSpeed = 1.2f;
    [Tooltip("How much blue can be reached while injured (0-1).")]
    [SerializeField, Range(0f, 1f)] private float injuredBlueMaxBlend = 0.6f;

    private float followingTintBlend;

    [Header("Always Ghost (Optional)")]
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

    [Header("Movement Settings")]
    [SerializeField] private float stoppingDistance = 2.5f;

    void Start()
    {
        // Initialize health and start with gun hidden.
        currentHealth = maxHealth;
        if (npcGun != null) npcGun.SetActive(false);

        agent = GetComponent<NavMeshAgent>();
        rb = GetComponent<Rigidbody>();

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) playerTransform = player.transform;

        if (agent != null)
        {
            // Agent is enabled only after the NPC agrees to follow.
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
        // While hostile, rotate body toward the player so shots feel intentional.
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
            // When converted to ally mode, keep chasing the player.
            HandleFollowingLogic();
        }

        // Visuals are recomputed every frame from state/health.
        UpdateVisualState();
        UpdateColorEffects();
    }

    public void TakeDamage(float amount)
    {
        // Damage is raw float reduction for now.
        currentHealth -= amount;

        // Optional behavior: being shot can force combat mode on.
        // This is useful for sprite swapping (idle -> attack) and return fire.
        if (becomeHostileWhenShot && !isFollowing && !isCombatActive)
        {
            isCombatActive = true;
            if (npcGun != null) npcGun.SetActive(true);
        }

        // At low health, hostile mode is disabled so player can trigger dialogue.
        if (isCombatActive && (currentHealth / maxHealth) <= 0.3f)
        {
            isCombatActive = false;
            if (npcGun != null) npcGun.SetActive(false);
        }
    }

    // Called by DialogueManager when player fails the conversation.
    public void ResumeCombat()
    {
        isCombatActive = true;
        currentHealth = maxHealth;
        if (npcGun != null) npcGun.SetActive(true);
        UpdateVisualState();
    }

    // Called by DialogueManager when player succeeds; NPC becomes ally.
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
        // Already allied NPC should not re-open dialogue/combat.
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
            // First interaction at healthy state "wakes up" combat.
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

        // Priority:
        // 1) Attack sprite while hostile
        // 2) Walk animation while moving
        // 3) Idle sprite otherwise
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

        // Injury is based on health ratio (0..1), not absolute HP value.
        float healthRatio = maxHealth > 0f ? currentHealth / maxHealth : 1f;
        bool isInjured = healthRatio <= injuredThreshold;

        // Injured pulse applies only before ally/follow state.
        float alpha = 1f;
        if (enableInjuredPulse && isInjured && !isFollowing)
        {
            float pulse01 = (Mathf.Sin(Time.time * injuredPulseSpeed) + 1f) * 0.5f;
            alpha = Mathf.Lerp(injuredMinAlpha, injuredMaxAlpha, pulse01);
        }

        // Following = full blue. Injured = slow blue fluctuation. Healthy/not following = no blue tint.
        float targetTintBlend = 0f;
        if (enableFollowingTint)
        {
            if (isFollowing)
            {
                targetTintBlend = 1f;
            }
            else if (blueWhenInjured && isInjured)
            {
                float fluctuation01 = (Mathf.Sin(Time.time * injuredBlueFluctuationSpeed) + 1f) * 0.5f;
                targetTintBlend = Mathf.Lerp(0f, injuredBlueMaxBlend, fluctuation01);
            }
        }

        float fadeSpeed = followingTintFadeDuration > 0.001f ? (1f / followingTintFadeDuration) : 999f;
        followingTintBlend = Mathf.MoveTowards(followingTintBlend, targetTintBlend, fadeSpeed * Time.deltaTime);

        Color baseTint = alwaysGhost ? alwaysGhostBaseTint : Color.white;
        float baseAlpha = alwaysGhost ? alwaysGhostBaseAlpha : 1f;

        if (alwaysGhost && alwaysGhostBreathingPulse)
        {
            // Subtle low-frequency alpha movement for "memory/ghost" feel.
            float breath01 = (Mathf.Sin(Time.time * alwaysGhostPulseSpeed) + 1f) * 0.5f;
            baseAlpha += Mathf.Lerp(-alwaysGhostPulseAmplitude, alwaysGhostPulseAmplitude, breath01);
            baseAlpha = Mathf.Clamp01(baseAlpha);
        }

        Color tint = Color.Lerp(baseTint, followingTintColor, followingTintBlend);
        tint.a *= baseAlpha * alpha;
        spriteRenderer.color = tint;
    }

    private void HandleFollowingLogic()
    {
        // Safety clamp to keep follower from sinking below floor in your scene setup.
        if (transform.position.y < 0.15f)
        {
            Vector3 fixedPos = transform.position;
            fixedPos.y = 0.16f;
            transform.position = fixedPos;
        }

        if (playerTransform != null)
        {
            // Keep some distance so NPC doesn't overlap the player.
            agent.SetDestination(playerTransform.position);
            agent.isStopped = Vector3.Distance(transform.position, playerTransform.position) < stoppingDistance;
        }
    }
}
