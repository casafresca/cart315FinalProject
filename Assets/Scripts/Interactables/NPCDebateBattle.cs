using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Optional AI-vs-AI debate sequence for a soldier NPC.
/// Triggered from NPC.Interact() when this component is present and enabled.
/// </summary>
public class NPCDebateBattle : MonoBehaviour
{
    public enum PlayerTurnMode
    {
        AiGenerated,
        FastPresetLine,
        InteractiveChoices
    }

    [Serializable]
    public class DebateChoiceOption
    {
        public string title = "Ground him";
        [TextArea] public string line = "Breathe. You are here with me now.";
        [Range(-100, 100)] public int insanityDelta = -10;
        [Range(-3, 3)] public int calmPointDelta = 1;
    }

    [Header("Enable")]
    [SerializeField] private bool enableDebateBattle = true;

    [Header("References")]
    [SerializeField] private NPC npc;
    [Tooltip("Optional room zone. If assigned, debate starts only while player is inside this collider bounds.")]
    [SerializeField] private Collider requiredRoomZone;
    [Tooltip("If true, debate can be triggered even while the NPC is already following.")]
    [SerializeField] private bool allowWhileFollowing = true;
    [Tooltip("If true, pressing interact on a following soldier can start debate anywhere (ignores room zone).")]
    [SerializeField] private bool debateAnywhereWhileFollowing = true;

    [Header("Player Turn")]
    [SerializeField] private PlayerTurnMode playerTurnMode = PlayerTurnMode.InteractiveChoices;
    [Tooltip("How long to wait for key 1/2/3 in InteractiveChoices mode.")]
    [SerializeField] private float choiceInputTimeoutSeconds = 8f;
    [Tooltip("If no key is pressed in time, this option index is used (0-based).")]
    [SerializeField] private int defaultChoiceIndex = 0;
    [SerializeField] private DebateChoiceOption[] choiceOptions;

    [Header("Fast Preset Mode")]
    [SerializeField] private string[] fastPresetPlayerLines =
    {
        "Breathe. I'm here.",
        "You are not back there. You are here.",
        "Look at me. Stay in this room."
    };
    [SerializeField] private int fastPresetInsanityDelta = -12;
    [SerializeField] private int fastPresetCalmPointDelta = 1;

    [Header("Debate Structure")]
    [SerializeField, Min(1)] private int rounds = 3;
    [SerializeField, Min(1)] private int calmPointsToWin = 2;
    [SerializeField] private float betweenTurnsDelay = 0.3f;
    [SerializeField] private float endTextHoldSeconds = 1.2f;

    [Header("Soldier Insanity")]
    [SerializeField, Range(0, 100)] private int soldierInsanityStart = 35;
    [SerializeField, Range(1, 100)] private int soldierInsanityPerRound = 20;

    [Header("Prompt Seeds")]
    [TextArea] [SerializeField] private string openingText = "The air tightens. This will be a battle of words.";
    [TextArea] [SerializeField] private string soldierFallbackLine = "You don't understand what I saw.";
    [TextArea] [SerializeField] private string playerFallbackLine = "I hear you. You're not alone right now.";

    [Header("Outcome")]
    [SerializeField] private bool makeSoldierFollowOnWin = true;
    [SerializeField] private bool resumeCombatOnLoss = true;
    [TextArea] [SerializeField] private string winLine = "The soldier's breathing steadies. He lowers his weapon.";
    [TextArea] [SerializeField] private string lossLine = "The soldier snaps back into survival panic.";

    [Header("Player Lock (Optional)")]
    [SerializeField] private bool lockPlayerScriptsDuringDebate = false;
    [SerializeField] private MonoBehaviour[] playerScriptsToDisable;

    [Header("AI Timing")]
    [SerializeField] private float aiReplyTimeoutSeconds = 20f;

    private bool isRunning;
    private Transform playerTransform;
    private NavMeshAgent npcAgent;

    private void Awake()
    {
        if (npc == null)
        {
            npc = GetComponent<NPC>();
        }

        if (npc != null)
        {
            npcAgent = npc.GetComponent<NavMeshAgent>();
        }

        CachePlayerTransform();
    }

    /// <summary>
    /// Called by NPC.Interact(). Returns true only when this component actually starts debate.
    /// </summary>
    public bool TryStartDebateFromInteract()
    {
        if (!CanTriggerFromCurrentState())
        {
            return false;
        }

        StartCoroutine(DebateRoutine());
        return true;
    }

    /// <summary>
    /// Read-only check used by interaction UI/scripts.
    /// </summary>
    public bool CanTriggerFromCurrentState()
    {
        if (!enableDebateBattle || isRunning)
        {
            return false;
        }

        if (npc == null || npc.isDead)
        {
            return false;
        }

        bool isFollowing = npc.isFollowing;
        if (isFollowing && !allowWhileFollowing)
        {
            return false;
        }

        CachePlayerTransform();

        if (requiredRoomZone != null && playerTransform != null)
        {
            if (!(isFollowing && debateAnywhereWhileFollowing) && !requiredRoomZone.bounds.Contains(playerTransform.position))
            {
                return false;
            }
        }

        return true;
    }

    private IEnumerator DebateRoutine()
    {
        isRunning = true;

        DialogueManager dialogueManager = DialogueManager.GetInstance();
        if (dialogueManager == null)
        {
            Debug.LogError("NPCDebateBattle: DialogueManager not found.");
            isRunning = false;
            yield break;
        }

        if (!dialogueManager.TryBeginExternalDialogueSession(openingText))
        {
            Debug.LogWarning("NPCDebateBattle: Could not begin external dialogue session.");
            isRunning = false;
            yield break;
        }

        SetPlayerLocked(true);
        SetSoldierMovementLocked(true);

        int calmPoints = 0;
        int insanity = Mathf.Clamp(soldierInsanityStart, 0, 100);
        string lastPlayerLine = "";

        for (int round = 1; round <= rounds; round++)
        {
            string soldierPrompt = BuildSoldierPrompt(round, insanity, lastPlayerLine);
            string soldierLine = string.Empty;
            yield return StartCoroutine(RequestLine("soldier", soldierPrompt, soldierFallbackLine, line => soldierLine = line));

            dialogueManager.SetExternalDialogueText($"Soldier: {soldierLine}\n\nInsanity: {insanity}%");
            yield return WaitForSecondsRealtimeSafe(betweenTurnsDelay);

            string playerLine = string.Empty;
            int turnInsanityDelta = 0;
            int turnCalmDelta = 0;

            if (playerTurnMode == PlayerTurnMode.AiGenerated)
            {
                string playerPrompt = BuildPlayerPrompt(round, insanity, soldierLine);
                yield return StartCoroutine(RequestLine("player", playerPrompt, playerFallbackLine, line => playerLine = line));

                if (IsCalmingResponse(playerLine))
                {
                    turnCalmDelta = 1;
                    turnInsanityDelta = -8;
                }
                else
                {
                    turnInsanityDelta = 8;
                }
            }
            else if (playerTurnMode == PlayerTurnMode.FastPresetLine)
            {
                playerLine = GetFastPresetLine();
                turnInsanityDelta = fastPresetInsanityDelta;
                turnCalmDelta = fastPresetCalmPointDelta;
            }
            else
            {
                DebateChoiceOption[] options = GetChoiceOptions();
                int chosenIndex = 0;
                yield return StartCoroutine(ChoosePlayerLine(dialogueManager, soldierLine, insanity, options, idx => chosenIndex = idx));

                DebateChoiceOption selected = options[Mathf.Clamp(chosenIndex, 0, options.Length - 1)];
                playerLine = string.IsNullOrWhiteSpace(selected.line) ? playerFallbackLine : selected.line.Trim();
                turnInsanityDelta = selected.insanityDelta;
                turnCalmDelta = selected.calmPointDelta;
            }

            calmPoints = Mathf.Max(0, calmPoints + turnCalmDelta);
            insanity = Mathf.Clamp(insanity + soldierInsanityPerRound + turnInsanityDelta, 0, 100);
            lastPlayerLine = playerLine;

            dialogueManager.SetExternalDialogueText(
                $"You: {playerLine}\n\nRound impact: {(turnInsanityDelta >= 0 ? "+" : "")}{turnInsanityDelta} insanity, {(turnCalmDelta >= 0 ? "+" : "")}{turnCalmDelta} calm\n" +
                $"Insanity now: {insanity}%\nCalm points: {calmPoints}/{calmPointsToWin}");

            yield return WaitForSecondsRealtimeSafe(betweenTurnsDelay);
        }

        bool won = calmPoints >= calmPointsToWin;
        dialogueManager.SetExternalDialogueText(won ? winLine : lossLine);

        if (npc != null)
        {
            if (won && makeSoldierFollowOnWin)
            {
                npc.StartFollowing();
            }
            else if (!won && resumeCombatOnLoss)
            {
                npc.ResumeCombat();
            }
        }

        yield return WaitForSecondsRealtimeSafe(endTextHoldSeconds);

        dialogueManager.EndExternalDialogueSession();
        SetPlayerLocked(false);
        SetSoldierMovementLocked(false);
        isRunning = false;
    }

    private IEnumerator ChoosePlayerLine(DialogueManager dialogueManager, string soldierLine, int insanity, DebateChoiceOption[] options, Action<int> onChoice)
    {
        int safeDefault = Mathf.Clamp(defaultChoiceIndex, 0, options.Length - 1);

        dialogueManager.SetExternalDialogueText(
            $"Soldier: {soldierLine}\n\nInsanity: {insanity}%\n\n" +
            $"Choose response: [1] {options[0].title}\n" +
            $"[2] {options[1].title}\n" +
            $"[3] {options[2].title}\n\n" +
            $"(Auto-picks {safeDefault + 1} in {choiceInputTimeoutSeconds:0.#}s)");

        float endTime = Time.realtimeSinceStartup + Mathf.Max(1f, choiceInputTimeoutSeconds);
        int selected = -1;

        while (Time.realtimeSinceStartup < endTime)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) { selected = 0; break; }
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) { selected = 1; break; }
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) { selected = 2; break; }
            yield return null;
        }

        if (selected < 0)
        {
            selected = safeDefault;
        }

        onChoice?.Invoke(selected);
    }

    private DebateChoiceOption[] GetChoiceOptions()
    {
        if (choiceOptions != null && choiceOptions.Length >= 3)
        {
            return choiceOptions;
        }

        return new[]
        {
            new DebateChoiceOption { title = "Ground him", line = "Breathe. You are in this room, not in the battlefield.", insanityDelta = -18, calmPointDelta = 1 },
            new DebateChoiceOption { title = "Question him", line = "If you keep firing, what are you protecting right now?", insanityDelta = 4, calmPointDelta = 0 },
            new DebateChoiceOption { title = "Provoke him", line = "Then prove you are still in control.", insanityDelta = 18, calmPointDelta = -1 },
        };
    }

    private string GetFastPresetLine()
    {
        if (fastPresetPlayerLines == null || fastPresetPlayerLines.Length == 0)
        {
            return playerFallbackLine;
        }

        int index = UnityEngine.Random.Range(0, fastPresetPlayerLines.Length);
        string line = fastPresetPlayerLines[index];
        return string.IsNullOrWhiteSpace(line) ? playerFallbackLine : line.Trim();
    }

    private void CachePlayerTransform()
    {
        if (playerTransform != null)
        {
            return;
        }

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
        }
    }

    private string BuildSoldierPrompt(int round, int insanity, string lastPlayerLine)
    {
        string memory = string.IsNullOrWhiteSpace(lastPlayerLine) ? "none yet" : lastPlayerLine;
        return $"Debate round {round}. You are the soldier. PTSD pressure is {insanity}/100 and rising. Last thing the player said: '{memory}'. Respond as unstable, defensive, vivid, and intense in one short line.";
    }

    private string BuildPlayerPrompt(int round, int insanity, string soldierLine)
    {
        return $"Debate round {round}. You are the player trying to de-escalate the soldier. Soldier just said: '{soldierLine}'. His PTSD pressure is {insanity}/100. Reply with one concise line that grounds, validates, and redirects him away from violence.";
    }

    private IEnumerator RequestLine(string role, string prompt, string fallback, Action<string> onResult)
    {
        TTSRunner runner = TTSRunner.Instance;
        if (runner == null)
        {
            onResult?.Invoke(fallback);
            yield break;
        }

        int beforeRequest = runner.LastCompletedRequestId;
        runner.SpeakAs(role, prompt);

        float timeoutAt = Time.realtimeSinceStartup + Mathf.Max(2f, aiReplyTimeoutSeconds);
        while (Time.realtimeSinceStartup < timeoutAt)
        {
            if (runner.LastCompletedRequestId != beforeRequest)
            {
                break;
            }
            yield return null;
        }

        string text = runner.LastCompletedRequestId != beforeRequest ? runner.LastCompletedReplyText : string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            text = fallback;
        }

        onResult?.Invoke(text.Trim());
    }

    private bool IsCalmingResponse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        string t = line.ToLowerInvariant();
        return t.Contains("breathe")
            || t.Contains("safe")
            || t.Contains("not alone")
            || t.Contains("with you")
            || t.Contains("listen")
            || t.Contains("here")
            || t.Contains("steady")
            || t.Contains("ground");
    }

    private IEnumerator WaitForSecondsRealtimeSafe(float seconds)
    {
        float end = Time.realtimeSinceStartup + Mathf.Max(0f, seconds);
        while (Time.realtimeSinceStartup < end)
        {
            yield return null;
        }
    }

    private void SetSoldierMovementLocked(bool locked)
    {
        if (npc == null)
        {
            return;
        }

        // Keep him from drifting/wandering during the debate scene.
        npc.isCombatActive = false;

        if (npcAgent == null)
        {
            npcAgent = npc.GetComponent<NavMeshAgent>();
        }

        if (npcAgent != null && npcAgent.enabled)
        {
            npcAgent.isStopped = locked;
            if (locked)
            {
                npcAgent.ResetPath();
            }
        }
    }

    private void SetPlayerLocked(bool locked)
    {
        if (!lockPlayerScriptsDuringDebate || playerScriptsToDisable == null)
        {
            return;
        }

        for (int i = 0; i < playerScriptsToDisable.Length; i++)
        {
            MonoBehaviour mb = playerScriptsToDisable[i];
            if (mb == null) continue;
            mb.enabled = !locked;
        }
    }
}
