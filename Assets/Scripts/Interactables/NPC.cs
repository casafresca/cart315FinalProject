using System.Collections;
using System.IO;
using UnityEngine;
using Ink.Runtime;
using UnityEngine.AI;
using UnityEngine.Networking;

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
    public bool isDead { get; private set; }
    public bool IsReadyForDialogueInteraction => IsAtDialogueThreshold();

    [Header("Combat & Health")]
    public float maxHealth = 100f;
    public float currentHealth;
    public GameObject npcGun;
    [Tooltip("If true, the NPC enters combat when hit by a bullet.")]
    [SerializeField] private bool becomeHostileWhenShot = true;
    [Tooltip("Health ratio where combat stops and dialogue becomes available.")]
    [SerializeField, Range(0.05f, 0.95f)] private float dialogueHealthThreshold = 0.3f;
    [Tooltip("If true, damage stops at the dialogue threshold instead of killing the NPC outright.")]
    [SerializeField] private bool preventDeathBeforeDialogue = true;

    [Header("Intro Audio")]
    [Tooltip("Local WAV file to play before starting combat.")]
    [SerializeField] private string introWavRelativePath = "TTS/wavs/soldier1.wav";
    [Tooltip("The text of the soldier's opening line, used to generate context-sensitive reply options.")]
    [SerializeField] private string introLineText = "Who are you?";
    [Tooltip("If true, generate custom reply options while the intro WAV plays.")]
    [SerializeField] private bool useGeneratedReplyOptions = true;

    private bool isIntroSequenceRunning = false;
    private bool choiceOptionsReady = false;
    private string[] generatedChoiceOptions;

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

    [Tooltip("If true, flicker starts when the NPC is staggered (stops shooting) and continues until subdued or combat resumes.")]
    [SerializeField] private bool persistFlickerAfterHit = true;

    private float followingTintBlend;
    private bool isHitFlickerActive;
    private float sanity = 0.5f;

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

    [Header("Optional AI Debate")]
    [Tooltip("Optional debate battle component. If present and allowed, interaction can start AI conversation battle instead of normal flow.")]
    [SerializeField] private NPCDebateBattle debateBattle;
    public NPCDebateBattle DebateBattle => debateBattle;

    [Header("Optional AI Typed Conversation")]
    [Tooltip("Optional constrained free-text interaction. If present and enabled, it can start before the debate path.")]
    [SerializeField] private AITypedConversation typedConversation;
    [SerializeField] private bool showApproachChoiceWhenBothModesAvailable = true;

    [Header("Movement Settings")]
    [SerializeField] private float stoppingDistance = 2.5f;

    [Header("Simple Combat Movement")]
    [SerializeField] private float moveSpeed = 2f;
    [SerializeField] private float changeDirInterval = 2f;
    private Vector3 moveDirection;
    private float moveTimer;
    private const float DialogueThresholdEpsilon = 0.0001f;

    // --- NEW MEMORY VARIABLES ---
    private Vector3 startPosition;
    private Quaternion startRotation;
    private bool initialCombatState;
    private bool isApproachChoiceActive;

    void Start()
    {
        // --- NEW: Save original state ---
        startPosition = transform.position;
        startRotation = transform.rotation;
        initialCombatState = isCombatActive; // Remember if they started hostile or peaceful

        // --- NEW: Listen for player respawn ---
        PlayerHealth.OnPlayerRespawn += ResetNPC;

        // Initialize health and start with gun hidden.
        currentHealth = maxHealth;
        isHitFlickerActive = false;
        if (npcGun != null) npcGun.SetActive(isCombatActive); // Set based on initial state

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

        if (debateBattle == null)
        {
            debateBattle = GetComponent<NPCDebateBattle>();
        }

        if (typedConversation == null)
        {
            typedConversation = GetComponent<AITypedConversation>();
        }

        nextWalkFrameTime = Time.time;
        UpdateVisualState();
        UpdateColorEffects();
    }

    // --- NEW: Stop listening if destroyed ---
    void OnDestroy()
    {
        PlayerHealth.OnPlayerRespawn -= ResetNPC;
    }

    // --- NEW: The Reset Function ---
    public void ResetNPC()
    {
        // 1. Turn off NavMeshAgent if it was following
        if (agent != null && agent.enabled)
        {
            agent.isStopped = true;
            agent.ResetPath();
            agent.enabled = false;
        }

        // 2. Reset Physics/Collision
        if (rb != null)
        {
            rb.isKinematic = false; // Undo the kinematic lock from StartFollowing()
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Re-enable collisions with player if they were following
        if (playerTransform != null)
        {
            Collider pCol = playerTransform.GetComponent<Collider>();
            Collider nCol = GetComponent<Collider>();
            if (pCol != null && nCol != null) Physics.IgnoreCollision(nCol, pCol, false);
        }

        // 3. Teleport back to start
        transform.position = startPosition;
        transform.rotation = startRotation;

        // 4. Reset all stats and states
        currentHealth = maxHealth;
        isDead = false;
        isFollowing = false;
        isCombatActive = initialCombatState;
        isHitFlickerActive = false;
        moveDirection = Vector3.zero;

        // 5. Reset visuals
        if (npcGun != null) npcGun.SetActive(isCombatActive);
        UpdateVisualState();
        UpdateColorEffects();

        Debug.Log($"NPC {gameObject.name} fully reset to starting state.");
    }

    void Update()
    {
        // While hostile, rotate body toward the player so shots feel intentional.
        if (isCombatActive && playerTransform != null)
        {
            HandleSimpleCombatMovement();
            HandleAiming();
        }

        // 2. FOLLOWING BEHAVIOR (Your original working logic)
        if (isFollowing && agent != null && agent.enabled)
        {
            // When converted to ally mode, keep chasing the player.
            HandleFollowingLogic();
        }

        // Visuals are recomputed every frame from state/health.
        UpdateVisualState();
        UpdateColorEffects();
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
        if (isDead || amount <= 0f)
        {
            return;
        }

        // Damage is raw float reduction for now.
        currentHealth -= amount;
        currentHealth = Mathf.Min(currentHealth, maxHealth);

        // Optional behavior: being shot can force combat mode on.
        // This is useful for sprite swapping (idle -> attack) and return fire.
        if (becomeHostileWhenShot && !isFollowing && !isCombatActive)
        {
            isCombatActive = true;
            if (npcGun != null) npcGun.SetActive(true);
        }

        bool canEnterDialogue = !isFollowing && inkJSON != null;
        float healthRatio = maxHealth > 0f ? currentHealth / maxHealth : 0f;

        if (canEnterDialogue && IsAtDialogueThreshold())
        {
            if (preventDeathBeforeDialogue)
            {
                currentHealth = Mathf.Max(currentHealth, maxHealth * dialogueHealthThreshold);
            }

            isCombatActive = false;
            moveDirection = Vector3.zero;
            if (persistFlickerAfterHit) isHitFlickerActive = true;
            if (npcGun != null) npcGun.SetActive(false);
            Debug.Log("NPC Staggered. Ready for interaction.");
            return;
        }

        if (currentHealth <= 0f)
        {
            Die();
            return;
        }

        // At low health, hostile mode is disabled so player can trigger dialogue.
        if (isCombatActive && IsAtDialogueThreshold())
        {
            isCombatActive = false;
            moveDirection = Vector3.zero;
            if (persistFlickerAfterHit) isHitFlickerActive = true;
            if (npcGun != null) npcGun.SetActive(false);
            Debug.Log("NPC Staggered. Ready for interaction.");
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
        moveDirection = Vector3.zero;
        isHitFlickerActive = false;

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
        UpdateVisualState();
    }

    protected override void Interact()
    {
        Debug.Log($"NPC.Interact called. isFollowing={isFollowing}, isDead={isDead}, currentHealth={currentHealth}, maxHealth={maxHealth}, healthRatio={(maxHealth > 0f ? currentHealth / maxHealth : 0f)}, threshold={dialogueHealthThreshold}, isCombatActive={isCombatActive}");

        if (TryStartApproachChoice())
        {
            return;
        }

        if (typedConversation != null && typedConversation.TryStartTypedConversation())
        {
            return;
        }

        if (debateBattle != null && debateBattle.TryStartDebateFromInteract())
        {
            return;
        }

        if (isFollowing || isDead) return;

        if (IsAtDialogueThreshold())
        {
            // Pause combat while dialogue is active.
            // DialogueManager will decide outcome after choices:
            // - success -> StartFollowing()
            // - failure -> ResumeCombat()
            isCombatActive = false;
            if (npcGun != null) npcGun.SetActive(false);
            UpdateVisualState();

            DialogueManager dialogueManager = DialogueManager.GetInstance();
            if (dialogueManager == null)
            {
                Debug.LogError("NPC: DialogueManager instance was not found.");
                return;
            }

            Debug.Log("NPC is entering dialogue mode.");
            dialogueManager.EnterDialogueMode(inkJSON, this);
        }
        else if (!isCombatActive)
        {
            if (useGeneratedReplyOptions)
            {
                StartCoroutine(PlayIntroThenTTS());
            }
            else
            {
                isCombatActive = true;
                if (npcGun != null) npcGun.SetActive(true);
                UpdateVisualState();
            }
        }
    }

    private bool HasTypedConversationAvailable()
    {
        return typedConversation != null && typedConversation.enabled && typedConversation.IsTypedConversationEnabled;
    }

    private bool HasDebateBattleAvailable()
    {
        return debateBattle != null && debateBattle.enabled && debateBattle.IsDebateBattleEnabled;
    }

    private bool TryStartApproachChoice()
    {
        if (!showApproachChoiceWhenBothModesAvailable || isApproachChoiceActive)
        {
            return false;
        }

        if (!HasTypedConversationAvailable() || !HasDebateBattleAvailable())
        {
            return false;
        }

        if (TTSRunner.Instance == null || !TTSRunner.Instance.IsReady)
        {
            return false;
        }

        bool canShowAtThisMoment = isFollowing || IsAtDialogueThreshold();
        if (!canShowAtThisMoment)
        {
            return false;
        }

        DialogueManager dialogueManager = DialogueManager.GetInstance();
        if (dialogueManager == null)
        {
            return false;
        }

        if (!dialogueManager.BeginExternalChoiceSession(
            "Choose your approach.",
            new[]
            {
                "Debate: confront him with guided choices",
                "Type: build your own sentence"
            },
            OnApproachChoiceSelected))
        {
            return false;
        }

        isApproachChoiceActive = true;
        return true;
    }

    private void OnApproachChoiceSelected(int choiceIndex)
    {
        isApproachChoiceActive = false;
        StartCoroutine(StartChosenApproachNextFrame(choiceIndex));
    }

    private IEnumerator StartChosenApproachNextFrame(int choiceIndex)
    {
        yield return null;

        if (choiceIndex == 0)
        {
            if (debateBattle != null && debateBattle.TryStartDebateFromInteract())
            {
                yield break;
            }

            typedConversation?.TryStartTypedConversation();
        }
        else
        {
            if (typedConversation != null && typedConversation.TryStartTypedConversation())
            {
                yield break;
            }

            debateBattle?.TryStartDebateFromInteract();
        }
    }
    private bool isWaitingForChoice = false;

    private IEnumerator PlayIntroThenTTS()
    {
        if (isIntroSequenceRunning)
            yield break;

        isIntroSequenceRunning = true;

        generatedChoiceOptions = null;
        bool done = false;

        if (TTSRunner.Instance != null)
        {
            TTSRunner.Instance.GenerateChoiceOptions(
                $"The soldier said: \"{introLineText}\". Generate four short player reply options.",
                options =>
                {
                    generatedChoiceOptions = options;
                    done = true;
                    Debug.Log("CHOICES GENERATED");
                }
            );
        }
        else
        {
            done = true;
        }

        // 1. PLAY INTRO WAV while the reply options are generating in parallel.
        string wavPath = System.IO.Path.Combine(Application.streamingAssetsPath, introWavRelativePath);
        yield return PlayLocalWav(wavPath);

        // 2. Wait only if the options are still not ready yet.
        yield return new WaitUntil(() => done);

        // FAILSAFE
        if (generatedChoiceOptions == null || generatedChoiceOptions.Length == 0)
        {
            Debug.LogError("NO CHOICES GENERATED");
            isIntroSequenceRunning = false;
            yield break;
        }

        // 3. SHOW CHOICES
        isWaitingForChoice = true;

        DialogueManager.GetInstance().BeginGeneratedChoiceSession(
            "How do you respond?",
            generatedChoiceOptions,
            OnPlayerChoiceSelected
        );

        Debug.Log("WAITING FOR PLAYER CHOICE");

        // 4. WAIT FOR CLICK
        yield return new WaitUntil(() => isWaitingForChoice == false);

        Debug.Log("PLAYER CHOSE SOMETHING");

        isIntroSequenceRunning = false;
    }
    private void OnPlayerChoiceSelected(int choiceIndex)
    {
        if (generatedChoiceOptions == null) return;

        string selected = generatedChoiceOptions[choiceIndex];

        Debug.Log("CHOICE CLICKED: " + selected);

        isWaitingForChoice = false; // 🔥 THIS UNLOCKS THE COROUTINE

        StartCoroutine(PlayResponseAfterChoice(selected));
    }

    private IEnumerator PlayResponseAfterChoice(string selectedChoice)
    {
        // 1. GET NPC RESPONSE
        string prompt = $"The soldier said: \"{introLineText}\". The player replied: \"{selectedChoice}\". Respond as the soldier with a short, in-character line.";

        if (TTSRunner.Instance != null)
        {
            TTSRunner.Instance.SpeakAs("soldier", prompt);
            yield return new WaitUntil(() => TTSRunner.Instance != null && !TTSRunner.Instance.IsSpeaking);
        }

        // 2. MODIFY SANITY BASED ON PLAYER INPUT
        float delta = EvaluateChoiceImpact(selectedChoice);
        sanity += delta;

        Debug.Log($"SANITY CHANGED: {sanity} (delta: {delta})");

        // 3. CHECK END CONDITIONS
        if (sanity <= 0f)
        {
            Debug.Log("NPC LOST. Reset required.");
            isIntroSequenceRunning = false;
            yield break;
        }

        if (sanity >= 1f)
        {
            Debug.Log("NPC SNAPPED. ENTERING COMBAT.");

            isCombatActive = true;
            if (npcGun != null) npcGun.SetActive(true);
            UpdateVisualState();

            isIntroSequenceRunning = false;
            yield break;
        }

        // 4. LOOP BACK → GENERATE NEW CHOICES
        generatedChoiceOptions = null;
        bool done = false;

        if (TTSRunner.Instance != null)
        {
            TTSRunner.Instance.GenerateChoiceOptions(
                $"The conversation continues. The player said: \"{selectedChoice}\". Generate four new reply options.",
                options =>
                {
                    generatedChoiceOptions = options;
                    done = true;
                }
            );
        }
        else done = true;

        yield return new WaitUntil(() => done);

        if (generatedChoiceOptions == null || generatedChoiceOptions.Length == 0)
        {
            Debug.LogError("FAILED TO GENERATE NEXT CHOICES");
            isIntroSequenceRunning = false;
            yield break;
        }

        // 5. SHOW NEXT CHOICES (LOOP CONTINUES)
        isWaitingForChoice = true;

        DialogueManager.GetInstance().BeginGeneratedChoiceSession(
            "What do you say next?",
            generatedChoiceOptions,
            OnPlayerChoiceSelected
        );

        yield return new WaitUntil(() => isWaitingForChoice == false);
    }
    private float EvaluateChoiceImpact(string choice)
    {
        choice = choice.ToLower();

        // crude but effective — you can upgrade this later with AI scoring
        if (choice.Contains("help") || choice.Contains("understand") || choice.Contains("why"))
            return +0.15f;

        if (choice.Contains("kill") || choice.Contains("hate") || choice.Contains("shut up"))
            return -0.2f;

        return Random.Range(-0.05f, 0.05f); // chaos factor
    }
    private IEnumerator PlayLocalWav(string path)
    {
        if (!File.Exists(path))
        {
            Debug.LogError($"NPC: intro WAV not found at {path}");
            yield break;
        }

        string url = "file:///" + path.Replace("\\", "/");
        using var req = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"NPC: failed to load intro WAV: {req.error}");
            yield break;
        }

        AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
        if (clip == null)
        {
            Debug.LogError("NPC: intro WAV loaded null clip.");
            yield break;
        }

        AudioSource source = GetComponent<AudioSource>();
        if (source == null)
            source = gameObject.AddComponent<AudioSource>();

        source.clip = clip;
        source.Play();
        yield return new WaitWhile(() => source.isPlaying);
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

        // Flicker/pulse applies while the NPC is in "hit vulnerability" state and
        // has not yet become an ally. This lets the effect persist during dialogue.
        bool shouldPulse = isHitFlickerActive && !isFollowing;
        float alpha = 1f;
        if (enableInjuredPulse && shouldPulse)
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


    /// <summary>
    /// Called by NPCShoot when this NPC fires again.
    /// Used to end the vulnerable flicker once combat fully resumes.
    /// </summary>
    public void NotifyFiredAgain()
    {
        isHitFlickerActive = false;
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

        if (playerTransform != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            // Keep some distance so NPC doesn't overlap the player.
            agent.SetDestination(playerTransform.position);
            agent.isStopped = Vector3.Distance(transform.position, playerTransform.position) < stoppingDistance;
        }
    }

    private void Die()
    {
        isDead = true;
        isCombatActive = false;
        isFollowing = false;
        moveDirection = Vector3.zero;
        currentHealth = 0f;
        isHitFlickerActive = false;

        if (npcGun != null)
        {
            npcGun.SetActive(false);
        }

        if (agent != null && agent.enabled)
        {
            agent.isStopped = true;
            agent.ResetPath();
            agent.enabled = false;
        }

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
        }

        Debug.Log("NPC died.");
    }

    private bool IsAtDialogueThreshold()
    {
        if (maxHealth <= 0f)
        {
            return true;
        }

        return currentHealth <= (maxHealth * dialogueHealthThreshold) + DialogueThresholdEpsilon;
    }

    [System.Serializable]
    private class ChoicePayload
    {
        public string[] choices;
    }
}
