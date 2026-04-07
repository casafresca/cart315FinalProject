using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

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
    public bool IsDebateBattleEnabled => enableDebateBattle;

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
    [Tooltip("If enabled, the battle can auto-pick a response after the timeout. Leave this off to keep options on screen until the player clicks.")]
    [SerializeField] private bool autoPickChoiceOnTimeout = false;
    [Tooltip("How long to wait before auto-picking when timeout auto-pick is enabled. Set to 0 or less to keep choices on screen until the player clicks.")]
    [SerializeField] private float choiceInputTimeoutSeconds = 0f;
    [Tooltip("If a timeout is enabled and no choice is pressed in time, this option index is used (0-based).")]
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
    [SerializeField, Min(1)] private int rounds = 4;
    [SerializeField, Min(1)] private int calmPointsToWin = 2;
    [SerializeField, Min(1)] private int manualChoicesRequiredToWin = 3;
    [SerializeField] private float betweenTurnsDelay = 0.3f;
    [SerializeField] private float endTextHoldSeconds = 1.2f;

    [Header("Soldier Insanity")]
    [SerializeField, Range(0, 100)] private int soldierInsanityStart = 35;
    [SerializeField, Range(0, 100)] private int soldierInsanityPerRound = 12;
    [SerializeField, Range(50, 100)] private int peakInsanityThreshold = 100;

    [Header("Scene Framing")]
    [SerializeField] private string soldierDisplayName = "Soldier";
    [TextArea] [SerializeField] private string sceneSummary = "A de-escalation debate with a war-broken soldier whose identity is splintering under PTSD and moral injury.";

    [Header("Soldier Identity")]
    [SerializeField] private string militaryRole = "Former infantry rifleman";
    [SerializeField] private string formerIdentity = "He used to be a careful older brother who fixed engines and wrote letters home.";
    [SerializeField] private string warTheater = "A brutal winter campaign in a fractured border region";
    [TextArea] [SerializeField] private string definingTrauma = "He survived atrocities, watched civilians and squadmates die, and now cannot separate survival reflex from ordinary life.";
    [TextArea] [SerializeField] private string triggerStimulus = "A metallic bang, a camera shutter, static, or a sudden order can send him back into combat mode.";
    [TextArea] [SerializeField] private string identityFracture = "He no longer knows whether he is still a soldier, a survivor, or just a weapon that kept moving.";
    [TextArea] [SerializeField] private string physicalTell = "He scans corners before speaking, loses track of who he is addressing, and talks like he is hearing the past on top of the present.";
    [TextArea] [SerializeField] private string tabooTopic = "He cannot bear being called a hero or being told the war made him stronger.";

    [Header("Prompt Seeds")]
    [TextArea] [SerializeField] private string openingText = "The air tightens. This will be a battle of words.";
    [TextArea] [SerializeField] private string soldierFallbackLine = "You don't understand what I saw.";
    [TextArea] [SerializeField] private string playerFallbackLine = "I hear you. You're not alone right now.";

    [Header("Outcome")]
    [SerializeField] private bool makeSoldierFollowOnWin = true;
    [SerializeField] private bool resumeCombatOnLoss = true;
    [SerializeField] private bool sendToTherapyRoomOnPeakInsanity = true;
    [SerializeField] private string rehabilitationSceneName = "therapy room";
    [TextArea] [SerializeField] private string winLine = "The soldier's breathing steadies. He lowers his weapon.";
    [TextArea] [SerializeField] private string lossLine = "The soldier snaps back into survival panic.";
    [TextArea] [SerializeField] private string peakInsanityLine = "The soldier breaks apart under the pressure. He needs rehabilitation now.";

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

        if (!isFollowing && !npc.IsReadyForDialogueInteraction)
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

        dialogueManager.SetExternalDialogueTopLayout(true);
        dialogueManager.SetExternalDialogueText(
            $"{openingText}\n\n{soldierDisplayName}\n{militaryRole}\n\nTrigger: {triggerStimulus}");

        SetPlayerLocked(true);
        SetSoldierMovementLocked(true);

        int calmPoints = 0;
        int manualChoicesMade = 0;
        int insanity = Mathf.Clamp(soldierInsanityStart, 0, 100);
        string lastPlayerLine = "";
        string lastSoldierLine = "";
        List<string> recentTranscript = new List<string>();
        bool triggeredRehabilitation = false;

        for (int round = 1; round <= rounds; round++)
        {
            DebateTurnResultData debateTurn = null;
            yield return StartCoroutine(RequestDebateTurn(round, insanity, lastPlayerLine, lastSoldierLine, recentTranscript, result => debateTurn = result));

            string soldierLine = debateTurn != null && !string.IsNullOrWhiteSpace(debateTurn.soldierReply)
                ? debateTurn.soldierReply.Trim()
                : soldierFallbackLine;

            dialogueManager.SetExternalDialogueText($"Soldier: {soldierLine}\n\nStage: {GetInsanityStage(insanity)}\nInsanity: {insanity}%");

            if (debateTurn != null && TTSRunner.Instance != null && !string.IsNullOrWhiteSpace(debateTurn.wavPath))
            {
                yield return StartCoroutine(TTSRunner.Instance.PlayWav(debateTurn.wavPath, "soldier", soldierLine));
            }
            else
            {
                yield return WaitForSecondsRealtimeSafe(betweenTurnsDelay);
            }

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
                DebateChoiceData[] options = GetDynamicChoiceOptions(debateTurn);
                int chosenIndex = 0;
                bool wasManualChoice = false;
                yield return StartCoroutine(ChoosePlayerLine(dialogueManager, soldierLine, insanity, options, (idx, manual) =>
                {
                    chosenIndex = idx;
                    wasManualChoice = manual;
                }));

                DebateChoiceData selected = options[Mathf.Clamp(chosenIndex, 0, options.Length - 1)];
                playerLine = string.IsNullOrWhiteSpace(selected.text) ? playerFallbackLine : selected.text.Trim();
                turnInsanityDelta = selected.insanityDelta;
                turnCalmDelta = selected.calmDelta;

                if (wasManualChoice)
                {
                    manualChoicesMade++;
                }
                else
                {
                    turnCalmDelta = Mathf.Min(0, turnCalmDelta);
                }
            }

            calmPoints = Mathf.Max(0, calmPoints + turnCalmDelta);
            insanity = Mathf.Clamp(insanity + soldierInsanityPerRound + turnInsanityDelta, 0, 100);
            lastPlayerLine = playerLine;
            lastSoldierLine = soldierLine;
            recentTranscript.Add($"Soldier: {soldierLine}");
            recentTranscript.Add($"Player: {playerLine}");
            TrimRecentTranscript(recentTranscript);

            dialogueManager.SetExternalDialogueText(
                $"You: {playerLine}\n\nRound impact: {(turnInsanityDelta >= 0 ? "+" : "")}{turnInsanityDelta} insanity, {(turnCalmDelta >= 0 ? "+" : "")}{turnCalmDelta} calm\n" +
                $"Stage now: {GetInsanityStage(insanity)}\nInsanity now: {insanity}%\nCalm points: {calmPoints}/{calmPointsToWin}\nManual choices: {manualChoicesMade}/{manualChoicesRequiredToWin}");

            yield return WaitForSecondsRealtimeSafe(betweenTurnsDelay);

            bool reachedPeak = insanity >= peakInsanityThreshold || (debateTurn != null && debateTurn.peakInsanity);
            if (reachedPeak)
            {
                dialogueManager.SetExternalDialogueText(peakInsanityLine);
                yield return WaitForSecondsRealtimeSafe(endTextHoldSeconds);
                triggeredRehabilitation = TryEnterRehabilitation();
                break;
            }

            if (calmPoints >= calmPointsToWin && manualChoicesMade >= manualChoicesRequiredToWin)
            {
                break;
            }
        }

        if (!triggeredRehabilitation)
        {
            bool won = calmPoints >= calmPointsToWin && manualChoicesMade >= manualChoicesRequiredToWin;
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
        }

        dialogueManager.EndExternalDialogueSession();
        SetPlayerLocked(false);
        SetSoldierMovementLocked(false);
        isRunning = false;
    }

    private IEnumerator ChoosePlayerLine(DialogueManager dialogueManager, string soldierLine, int insanity, DebateChoiceData[] options, Action<int, bool> onChoice)
    {
        int safeDefault = Mathf.Clamp(defaultChoiceIndex, 0, options.Length - 1);
        int selected = -1;
        bool callbackReceived = false;
        bool manualChoice = false;

        string[] optionTexts = new string[options.Length];
        for (int i = 0; i < options.Length; i++)
        {
            string title = string.IsNullOrWhiteSpace(options[i].title) ? "Response" : options[i].title.Trim();
            string text = string.IsNullOrWhiteSpace(options[i].text) ? string.Empty : options[i].text.Trim();
            optionTexts[i] = string.IsNullOrWhiteSpace(text) ? title : $"{title}: {text}";
        }

        dialogueManager.ShowExternalChoiceOptions(
            $"Soldier: {soldierLine}\n\nStage: {GetInsanityStage(insanity)}\nInsanity: {insanity}%\nChoose your response:",
            optionTexts,
            idx =>
            {
                selected = idx;
                callbackReceived = true;
                manualChoice = true;
            });

        bool useTimeout = autoPickChoiceOnTimeout && choiceInputTimeoutSeconds > 0f;
        if (!useTimeout)
        {
            while (!callbackReceived)
            {
                yield return null;
            }
        }
        else
        {
            float endTime = Time.realtimeSinceStartup + Mathf.Max(1f, choiceInputTimeoutSeconds);
            while (Time.realtimeSinceStartup < endTime && !callbackReceived)
            {
                yield return null;
            }

            if (!callbackReceived)
            {
                selected = safeDefault;
                dialogueManager.HideExternalChoiceOptions();
            }
        }

        onChoice?.Invoke(selected, manualChoice);
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

    private DebateChoiceData[] GetDynamicChoiceOptions(DebateTurnResultData debateTurn)
    {
        if (debateTurn != null && debateTurn.debateChoices != null && debateTurn.debateChoices.Length >= 3)
        {
            return debateTurn.debateChoices;
        }

        DebateChoiceOption[] fallback = GetChoiceOptions();
        DebateChoiceData[] converted = new DebateChoiceData[fallback.Length];
        for (int i = 0; i < fallback.Length; i++)
        {
            converted[i] = new DebateChoiceData
            {
                title = fallback[i].title,
                text = fallback[i].line,
                tone = "fallback",
                insanityDelta = fallback[i].insanityDelta,
                calmDelta = fallback[i].calmPointDelta,
            };
        }

        return converted;
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

    private IEnumerator RequestDebateTurn(int round, int insanity, string lastPlayerLine, string lastSoldierLine, List<string> recentTranscript, Action<DebateTurnResultData> onResult)
    {
        TTSRunner runner = TTSRunner.Instance;
        if (runner == null)
        {
            onResult?.Invoke(null);
            yield break;
        }

        DebateTurnRequestData request = new DebateTurnRequestData
        {
            role = "soldier",
            soldierName = string.IsNullOrWhiteSpace(soldierDisplayName) ? "Soldier" : soldierDisplayName.Trim(),
            sceneSummary = sceneSummary ?? string.Empty,
            formerIdentity = formerIdentity ?? string.Empty,
            militaryRole = militaryRole ?? string.Empty,
            warTheater = warTheater ?? string.Empty,
            definingTrauma = definingTrauma ?? string.Empty,
            triggerStimulus = triggerStimulus ?? string.Empty,
            identityFracture = identityFracture ?? string.Empty,
            physicalTell = physicalTell ?? string.Empty,
            tabooTopic = tabooTopic ?? string.Empty,
            round = round,
            insanity = insanity,
            insanityStage = GetInsanityStage(insanity),
            lastPlayerLine = lastPlayerLine ?? string.Empty,
            lastSoldierLine = lastSoldierLine ?? string.Empty,
            recentTranscript = recentTranscript != null ? recentTranscript.ToArray() : Array.Empty<string>(),
        };

        bool done = false;
        DebateTurnResultData result = null;
        runner.GenerateDebateTurn(request, response =>
        {
            result = response;
            done = true;
        });

        float timeoutAt = Time.realtimeSinceStartup + Mathf.Max(2f, aiReplyTimeoutSeconds);
        while (!done && Time.realtimeSinceStartup < timeoutAt)
        {
            yield return null;
        }

        onResult?.Invoke(result);
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

    private string GetInsanityStage(int insanity)
    {
        if (insanity >= 85) return "breaking";
        if (insanity >= 65) return "delusional";
        if (insanity >= 40) return "unstable";
        if (insanity >= 20) return "frayed";
        return "guarded";
    }

    private void TrimRecentTranscript(List<string> recentTranscript)
    {
        if (recentTranscript == null)
        {
            return;
        }

        while (recentTranscript.Count > 8)
        {
            recentTranscript.RemoveAt(0);
        }
    }

    private bool TryEnterRehabilitation()
    {
        if (!sendToTherapyRoomOnPeakInsanity || string.IsNullOrWhiteSpace(rehabilitationSceneName))
        {
            return false;
        }

        CachePlayerTransform();
        if (playerTransform != null)
        {
            TherapySessionState.SetReturnPoint(
                SceneManager.GetActiveScene().name,
                playerTransform.position,
                playerTransform.rotation);
        }

        SceneManager.LoadScene(rehabilitationSceneName);
        return true;
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
