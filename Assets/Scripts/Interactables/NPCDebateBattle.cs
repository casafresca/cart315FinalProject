using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Optional AI-vs-AI debate sequence for a soldier NPC.
/// Triggered from NPC.Interact() when this component is present and enabled.
///
/// Flow:
/// - Player presses interact (F) on soldier
/// - Soldier and Player roles alternate AI-generated lines
/// - Soldier insanity pressure rises each round
/// - Simple scoring decides if soldier calms (follow) or relapses (combat)
/// </summary>
public class NPCDebateBattle : MonoBehaviour
{
    [Header("Enable")]
    [SerializeField] private bool enableDebateBattle = true;

    [Header("References")]
    [SerializeField] private NPC npc;
    [Tooltip("Optional room zone. If assigned, debate starts only while player is inside this collider bounds.")]
    [SerializeField] private Collider requiredRoomZone;

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

    private void Awake()
    {
        if (npc == null)
        {
            npc = GetComponent<NPC>();
        }

        if (playerTransform == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                playerTransform = playerObj.transform;
            }
        }
    }

    /// <summary>
    /// Called by NPC.Interact(). Returns true only when this component actually starts debate.
    /// </summary>
    public bool TryStartDebateFromInteract()
    {
        if (!enableDebateBattle || isRunning)
        {
            return false;
        }

        if (requiredRoomZone != null && playerTransform != null)
        {
            if (!requiredRoomZone.bounds.Contains(playerTransform.position))
            {
                return false;
            }
        }

        StartCoroutine(DebateRoutine());
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

            string playerPrompt = BuildPlayerPrompt(round, insanity, soldierLine);
            string playerLine = string.Empty;
            yield return StartCoroutine(RequestLine("player", playerPrompt, playerFallbackLine, line => playerLine = line));

            dialogueManager.SetExternalDialogueText($"You: {playerLine}\n\nCalm points: {calmPoints}/{calmPointsToWin}");

            if (IsCalmingResponse(playerLine))
            {
                calmPoints++;
            }

            lastPlayerLine = playerLine;
            insanity = Mathf.Clamp(insanity + soldierInsanityPerRound, 0, 100);

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
        isRunning = false;
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

