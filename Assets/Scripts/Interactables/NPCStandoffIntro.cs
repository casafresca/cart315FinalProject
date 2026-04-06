using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// One-shot opening standoff: the soldier is unstable and about to shoot.
/// Generates an opening line + 4 player reply choices, then branches the soldier's follow-up based on selection.
/// Uses DialogueManager external session + TTSRunner for AI text and speech.
/// </summary>
public class NPCStandoffIntro : MonoBehaviour
{
    [Header("Enable")]
    [SerializeField] private bool enableStandoffIntro = true;
    [SerializeField] private bool autoStart = true;
    [SerializeField] private bool playOnce = true;
    [SerializeField] private float startDelaySeconds = 0.15f;

    [Header("References")]
    [SerializeField] private NPC npc;
    [SerializeField] private DialogueManager dialogueManager;
    [SerializeField] private TTSRunner ttsRunner;

    [Header("Player Lock (Optional)")]
    [SerializeField] private bool lockPlayerScriptsDuringStandoff = true;
    [SerializeField] private MonoBehaviour[] playerScriptsToDisable;

    [Header("AI Settings")]
    [SerializeField, Range(2, 8)] private int playerChoiceCount = 4;
    [SerializeField] private float aiReplyTimeoutSeconds = 25f;

    [Header("Fallback Lines")]
    [TextArea] [SerializeField] private string fallbackSoldierOpening = "Don't move. Hands up. Don't make me do it.";
    [TextArea] [SerializeField] private string fallbackSoldierFollowup = "Say that again. Slow. I can't hear straight.";
    [TextArea] [SerializeField] private string[] fallbackPlayerChoices =
    {
        "Easy—I'm not reaching for anything.",
        "Do it then. See what that gets you.",
        "Please—I'm not your enemy.",
        "Look at me. You're here, not back there."
    };

    [Header("Prompt Seed")]
    [TextArea] [SerializeField] private string sceneSeed =
        "The soldier is inches from firing. He is violent, unstable, and paranoid, aiming at the protagonist at point-blank range.";

    private bool hasPlayed;
    private bool isRunning;
    private NPCShoot npcShoot;
    private string soldierOpeningLine = "";
    private string[] playerChoices = Array.Empty<string>();

    private void Awake()
    {
        if (npc == null)
        {
            npc = GetComponent<NPC>();
        }

        if (dialogueManager == null)
        {
            dialogueManager = DialogueManager.GetInstance();
        }

        if (ttsRunner == null)
        {
            ttsRunner = TTSRunner.Instance;
        }

        if (npc != null)
        {
            npcShoot = npc.GetComponentInChildren<NPCShoot>(true);
        }
    }

    private void Start()
    {
        if (!autoStart)
        {
            return;
        }

        TryStart();
    }

    public void TryStart()
    {
        if (!enableStandoffIntro || isRunning)
        {
            return;
        }

        if (playOnce && hasPlayed)
        {
            return;
        }

        StartCoroutine(StandoffRoutine());
    }

    private IEnumerator StandoffRoutine()
    {
        isRunning = true;
        hasPlayed = true;

        if (startDelaySeconds > 0f)
        {
            yield return new WaitForSeconds(startDelaySeconds);
        }

        if (npc != null)
        {
            npc.isCombatActive = true;
            if (npc.npcGun != null) npc.npcGun.SetActive(true);
        }

        // Keep the threat visible (aiming), but prevent actual firing while the choice UI is up.
        if (npcShoot != null)
        {
            npcShoot.enabled = false;
        }

        SetPlayerLocked(true);

        yield return GenerateSoldierOpeningLine();
        yield return GeneratePlayerChoices();

        if (dialogueManager == null)
        {
            Debug.LogWarning("NPCStandoffIntro: DialogueManager not found; ending standoff intro.");
            CleanupAfterStandoff(keepCombatActive: true);
            yield break;
        }

        if (playerChoices == null || playerChoices.Length == 0)
        {
            playerChoices = fallbackPlayerChoices ?? Array.Empty<string>();
        }

        string openingText = $"Soldier: {soldierOpeningLine}";
        bool started = dialogueManager.TryBeginExternalDialogueSession(openingText, playerChoices, OnPlayerChoiceSelected);
        if (!started)
        {
            Debug.LogWarning("NPCStandoffIntro: Could not start external dialogue session (already playing).");
            CleanupAfterStandoff(keepCombatActive: true);
            yield break;
        }

        // Wait until the callback finishes the sequence.
        while (isRunning)
        {
            yield return null;
        }
    }

    private void OnPlayerChoiceSelected(int index)
    {
        if (!isRunning)
        {
            return;
        }

        if (playerChoices == null || playerChoices.Length == 0)
        {
            playerChoices = fallbackPlayerChoices ?? Array.Empty<string>();
        }

        int safeIndex = Mathf.Clamp(index, 0, playerChoices.Length - 1);
        string chosen = playerChoices[safeIndex] ?? string.Empty;
        StartCoroutine(ResolveBranchRoutine(chosen));
    }

    private IEnumerator ResolveBranchRoutine(string chosenLine)
    {
        string safeChoice = string.IsNullOrWhiteSpace(chosenLine) ? "..." : chosenLine.Trim();

        if (dialogueManager != null)
        {
            dialogueManager.SetExternalDialogueText($"Soldier: {soldierOpeningLine}\n\nYou: {safeChoice}");
        }

        yield return SpeakExactLine("player", safeChoice);

        string followupPrompt = BuildSoldierFollowupPrompt(safeChoice);
        string soldierFollowup = "";
        yield return SpeakAiLine("soldier", followupPrompt, fallbackSoldierFollowup, text => soldierFollowup = text);

        if (dialogueManager != null)
        {
            dialogueManager.SetExternalDialogueText($"Soldier: {soldierOpeningLine}\n\nYou: {safeChoice}\n\nSoldier: {soldierFollowup}");
        }

        bool deescalated = IsDeescalating(safeChoice);
        CleanupAfterStandoff(keepCombatActive: !deescalated);

        // Let the last line sit for a moment, then close.
        yield return new WaitForSeconds(0.9f);
        if (dialogueManager != null)
        {
            dialogueManager.EndExternalDialogueSession();
        }

        isRunning = false;
    }

    private void CleanupAfterStandoff(bool keepCombatActive)
    {
        SetPlayerLocked(false);

        if (npc != null)
        {
            npc.isCombatActive = keepCombatActive;
            if (npc.npcGun != null) npc.npcGun.SetActive(keepCombatActive);
        }

        if (npcShoot != null)
        {
            npcShoot.enabled = keepCombatActive;
        }
    }

    private IEnumerator GenerateSoldierOpeningLine()
    {
        string prompt = BuildSoldierOpeningPrompt();
        string result = "";
        yield return SpeakAiLine("soldier", prompt, fallbackSoldierOpening, text => result = text);
        soldierOpeningLine = string.IsNullOrWhiteSpace(result) ? fallbackSoldierOpening : result.Trim();
    }

    private IEnumerator GeneratePlayerChoices()
    {
        if (ttsRunner == null)
        {
            playerChoices = fallbackPlayerChoices ?? Array.Empty<string>();
            yield break;
        }

        bool done = false;
        string[] choices = Array.Empty<string>();

        string prompt = BuildPlayerChoicesPrompt(soldierOpeningLine);
        ttsRunner.RequestChoices("player", prompt, playerChoiceCount, result =>
        {
            choices = result ?? Array.Empty<string>();
            done = true;
        });

        float timeoutAt = Time.realtimeSinceStartup + Mathf.Max(3f, aiReplyTimeoutSeconds);
        while (!done && Time.realtimeSinceStartup < timeoutAt)
        {
            yield return null;
        }

        if (!done || choices.Length == 0)
        {
            playerChoices = fallbackPlayerChoices ?? Array.Empty<string>();
            yield break;
        }

        playerChoices = choices;
    }

    private IEnumerator SpeakAiLine(string role, string prompt, string fallback, Action<string> onResult)
    {
        if (ttsRunner == null)
        {
            onResult?.Invoke(fallback);
            yield break;
        }

        int beforeRequest = ttsRunner.LastCompletedRequestId;
        ttsRunner.SpeakAs(role, prompt);

        float timeoutAt = Time.realtimeSinceStartup + Mathf.Max(3f, aiReplyTimeoutSeconds);
        while (Time.realtimeSinceStartup < timeoutAt)
        {
            if (ttsRunner.LastCompletedRequestId != beforeRequest)
            {
                break;
            }
            yield return null;
        }

        string text = ttsRunner.LastCompletedRequestId != beforeRequest ? ttsRunner.LastCompletedReplyText : string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            text = fallback;
        }

        onResult?.Invoke(text.Trim());
    }

    private IEnumerator SpeakExactLine(string role, string exactLine)
    {
        if (ttsRunner == null)
        {
            yield break;
        }

        int beforeRequest = ttsRunner.LastCompletedRequestId;
        ttsRunner.SpeakExactAs(role, exactLine);

        float timeoutAt = Time.realtimeSinceStartup + Mathf.Max(3f, aiReplyTimeoutSeconds);
        while (Time.realtimeSinceStartup < timeoutAt)
        {
            if (ttsRunner.LastCompletedRequestId != beforeRequest)
            {
                break;
            }
            yield return null;
        }
    }

    private string BuildSoldierOpeningPrompt()
    {
        return
            $"{sceneSeed}\n\n" +
            "Write the soldier's FIRST spoken line. He is armed, finger on the trigger, shaking with rage and fear.\n" +
            "Constraints: one short sentence or fragment, no stage directions, no parentheses, no quotes.\n" +
            "It should feel like he's about to shoot right now.";
    }

    private string BuildPlayerChoicesPrompt(string soldierLine)
    {
        return
            $"{sceneSeed}\n\n" +
            $"Soldier just said: \"{soldierLine}\".\n\n" +
            $"Generate {playerChoiceCount} DISTINCT player reply options as short spoken lines.\n" +
            "Make them diverse: one calming/grounding, one defiant, one pleading, one tactical/distracting.\n" +
            "No stage directions, no parentheses. Keep them punchy and believable.";
    }

    private string BuildSoldierFollowupPrompt(string playerLine)
    {
        return
            $"{sceneSeed}\n\n" +
            $"Your opening line was: \"{soldierOpeningLine}\".\n" +
            $"The player replied: \"{playerLine}\".\n\n" +
            "Now reply as the soldier in ONE line reacting directly to what the player said.\n" +
            "Constraints: violent, unstable, paranoid; no stage directions; no parentheses; keep it short.";
    }

    private bool IsDeescalating(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        string t = line.ToLowerInvariant();
        return t.Contains("easy")
            || t.Contains("breathe")
            || t.Contains("safe")
            || t.Contains("not reaching")
            || t.Contains("not back there")
            || t.Contains("look at me")
            || t.Contains("i'm not your enemy")
            || t.Contains("please");
    }

    private void SetPlayerLocked(bool locked)
    {
        if (!lockPlayerScriptsDuringStandoff || playerScriptsToDisable == null)
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

