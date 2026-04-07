using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Separate constrained free-text conversation mode.
/// Lets the player type a sentence using a suggested word bank while the AI
/// reacts to the actual typed line plus simple local analysis tags.
/// </summary>
public class AITypedConversation : MonoBehaviour
{
    private struct InputAnalysis
    {
        public string[] tags;
        public int rapportDelta;
        public int instabilityDelta;
        public bool usedRequiredWord;
    }

    private sealed class WordButtonBinding
    {
        public Button button;
        public TextMeshProUGUI label;
    }

    [Header("Enable")]
    [SerializeField] private bool enableTypedConversation = true;
    public bool IsTypedConversationEnabled => enableTypedConversation;

    [Header("References")]
    [SerializeField] private NPC linkedNpc;
    [SerializeField] private bool allowWhileFollowing = true;

    [Header("Speaker")]
    [SerializeField] private string aiRole = "soldier";
    [SerializeField] private string speakerDisplayName = "Soldier";
    [TextArea] [SerializeField] private string openingLine = "Say it plain. Use your own words. I want to hear what you think I am now.";
    [TextArea] [SerializeField] private string fallbackReply = "That is not nothing. I heard it.";

    [Header("Conversation Progress")]
    [SerializeField, Min(1)] private int maxTurns = 4;
    [SerializeField, Min(1)] private int rapportToResolve = 3;
    [SerializeField, Min(1)] private int minimumTurnsBeforeResolve = 3;
    [SerializeField, Range(0, 100)] private int startingInstability = 28;
    [SerializeField, Range(0, 100)] private int instabilityRisePerTurn = 8;
    [SerializeField, Range(1, 100)] private int failureInstability = 90;
    [SerializeField] private float betweenTurnsDelay = 0.25f;
    [SerializeField] private float endTextHoldSeconds = 1.25f;

    [Header("Identity")]
    [TextArea] [SerializeField] private string sceneSummary = "A tense AI conversation with a soldier whose war trauma and fractured identity keep bleeding into the present.";
    [SerializeField] private string militaryRole = "Former infantry rifleman";
    [TextArea] [SerializeField] private string formerIdentity = "He used to be gentle, practical, and protective before the war hollowed him out.";
    [TextArea] [SerializeField] private string warTheater = "A brutal winter campaign in a fractured border region";
    [TextArea] [SerializeField] private string definingTrauma = "He carries civilians, dead squadmates, and morally unforgivable moments that he cannot file away as history.";
    [TextArea] [SerializeField] private string triggerStimulus = "Static, flash, orders, shutter clicks, and sudden metallic sounds can collapse the present into combat memory.";
    [TextArea] [SerializeField] private string identityFracture = "He cannot tell whether he is a survivor, a weapon, or the man he was before the war.";
    [TextArea] [SerializeField] private string physicalTell = "He checks exits, stares past faces, and sometimes answers the dead instead of the person in front of him.";
    [TextArea] [SerializeField] private string tabooTopic = "Being called a hero, clean, or saved makes him recoil.";

    [Header("Outcome")]
    [SerializeField] private bool makeNpcFollowOnResolve = true;
    [SerializeField] private bool resumeCombatOnFailure = true;
    [TextArea] [SerializeField] private string resolveLine = "Something in him loosens. He is still damaged, but he stops fighting you.";
    [TextArea] [SerializeField] private string failureLine = "The exchange curdles. He is too flooded to stay in the room with you.";
    [TextArea] [SerializeField] private string neutralEndLine = "The conversation thins out, unresolved, but you leave with more of who he is than before.";

    [Header("Typed Input Rules")]
    [SerializeField, Min(4)] private int minimumTypedCharacters = 10;
    [SerializeField] private bool requireAnchorWord = false;
    [SerializeField] private int anchorWordRapportBonus = 0;
    [SerializeField] private int wordButtonCount = 8;
    [SerializeField] private string[] groundingWords = { "breathe", "here", "safe", "steady", "now" };
    [SerializeField] private string[] memoryWords = { "name", "home", "letter", "mother", "brother", "before" };
    [SerializeField] private string[] commandWords = { "stand down", "report", "listen", "order", "focus" };
    [SerializeField] private string[] guiltWords = { "blood", "burned", "body", "grave", "shame" };
    [SerializeField] private string[] relationWords = { "friend", "son", "human", "witness", "remember" };

    [Header("Player Lock (Optional)")]
    [SerializeField] private bool lockPlayerScriptsDuringConversation = false;
    [SerializeField] private MonoBehaviour[] playerScriptsToDisable;

    [Header("AI Timing")]
    [SerializeField] private float aiReplyTimeoutSeconds = 20f;

    private bool isRunning;
    private Transform playerTransform;

    private GameObject runtimeRoot;
    private TextMeshProUGUI runtimeTitleText;
    private TextMeshProUGUI runtimeHintText;
    private TMP_InputField runtimeInputField;
    private Button runtimeSubmitButton;
    private Button runtimeClearButton;
    private readonly List<WordButtonBinding> runtimeWordButtons = new List<WordButtonBinding>();

    private bool submitRequested;
    private string submittedText = string.Empty;
    private string[] carrySuggestedWords = Array.Empty<string>();

    private void Awake()
    {
        if (linkedNpc == null)
        {
            linkedNpc = GetComponent<NPC>();
        }

        CachePlayerTransform();
    }

    private void Update()
    {
        if (!isRunning || runtimeRoot == null || !runtimeRoot.activeSelf || runtimeInputField == null)
        {
            return;
        }

        if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) && runtimeInputField.isFocused)
        {
            SubmitTypedLine();
        }
    }

    public bool TryStartTypedConversation()
    {
        if (!enableTypedConversation || isRunning)
        {
            return false;
        }

        if (linkedNpc != null)
        {
            if (linkedNpc.isDead)
            {
                return false;
            }

            if (linkedNpc.isFollowing && !allowWhileFollowing)
            {
                return false;
            }

            if (!linkedNpc.isFollowing && !linkedNpc.IsReadyForDialogueInteraction)
            {
                return false;
            }
        }

        StartCoroutine(TypedConversationRoutine());
        return true;
    }

    private IEnumerator TypedConversationRoutine()
    {
        isRunning = true;

        DialogueManager dialogueManager = DialogueManager.GetInstance();
        if (dialogueManager == null)
        {
            Debug.LogError("AITypedConversation: DialogueManager not found.");
            isRunning = false;
            yield break;
        }

        if (!dialogueManager.TryBeginExternalDialogueSession(openingLine))
        {
            Debug.LogWarning("AITypedConversation: Could not begin external dialogue session.");
            isRunning = false;
            yield break;
        }

        dialogueManager.SetExternalDialogueTopLayout(true);
        EnsureRuntimeUi();
        SetConversationUiVisible(true);
        SetPlayerLocked(true);

        int rapport = 0;
        int instability = Mathf.Clamp(startingInstability, 0, 100);
        string currentAiLine = string.IsNullOrWhiteSpace(openingLine) ? fallbackReply : openingLine.Trim();
        List<string> recentTranscript = new List<string> { $"{speakerDisplayName}: {currentAiLine}" };
        bool resolved = false;
        bool failed = false;

        for (int round = 1; round <= maxTurns; round++)
        {
            string requiredWord = ChooseRequiredWord(round, instability);
            string[] offeredWords = BuildWordBank(requiredWord);

            dialogueManager.SetExternalDialogueText(BuildDialogueText(currentAiLine, rapport, instability, round, requiredWord, string.Empty, string.Empty));

            string playerLine = null;
            yield return StartCoroutine(CollectTypedLine(requiredWord, offeredWords, line => playerLine = line));
            if (string.IsNullOrWhiteSpace(playerLine))
            {
                playerLine = "...";
            }

            InputAnalysis analysis = AnalyzePlayerLine(playerLine, requiredWord);
            rapport = Mathf.Max(0, rapport + analysis.rapportDelta);
            instability = Mathf.Clamp(instability + instabilityRisePerTurn + analysis.instabilityDelta, 0, 100);

            recentTranscript.Add($"Player: {playerLine}");
            TrimRecentTranscript(recentTranscript);

            string analysisText = analysis.tags != null && analysis.tags.Length > 0
                ? string.Join(", ", analysis.tags)
                : "unguarded";

            dialogueManager.SetExternalDialogueText(
                $"You: {playerLine}\n\nRead as: {analysisText}\nRapport: {rapport}/{rapportToResolve}\nInstability: {instability}%");

            yield return WaitForSecondsRealtimeSafe(betweenTurnsDelay);

            TypedConversationResultData typedResult = null;
            yield return StartCoroutine(RequestTypedTurn(
                round,
                instability,
                requiredWord,
                playerLine,
                offeredWords,
                analysis.tags,
                recentTranscript,
                result => typedResult = result));

            currentAiLine = typedResult != null && !string.IsNullOrWhiteSpace(typedResult.speakerReply)
                ? typedResult.speakerReply.Trim()
                : fallbackReply;

            carrySuggestedWords = typedResult != null && typedResult.suggestedWords != null
                ? typedResult.suggestedWords
                : Array.Empty<string>();

            recentTranscript.Add($"{speakerDisplayName}: {currentAiLine}");
            if (typedResult != null && !string.IsNullOrWhiteSpace(typedResult.backstoryReveal))
            {
                recentTranscript.Add($"Memory: {typedResult.backstoryReveal.Trim()}");
            }
            TrimRecentTranscript(recentTranscript);

            string stateHint = typedResult != null ? typedResult.stateHint : string.Empty;
            string backstoryReveal = typedResult != null ? typedResult.backstoryReveal : string.Empty;
            dialogueManager.SetExternalDialogueText(BuildDialogueText(currentAiLine, rapport, instability, round, requiredWord, stateHint, backstoryReveal));

            if (typedResult != null && TTSRunner.Instance != null && !string.IsNullOrWhiteSpace(typedResult.wavPath))
            {
                SetConversationUiVisible(false);
                yield return StartCoroutine(TTSRunner.Instance.PlayWav(typedResult.wavPath, aiRole, currentAiLine));
                SetConversationUiVisible(true);
            }
            else
            {
                yield return WaitForSecondsRealtimeSafe(betweenTurnsDelay);
            }

            if (round >= minimumTurnsBeforeResolve && rapport >= rapportToResolve)
            {
                resolved = true;
                break;
            }

            if (instability >= failureInstability)
            {
                failed = true;
                break;
            }
        }

        SetConversationUiVisible(false);

        if (resolved)
        {
            dialogueManager.SetExternalDialogueText(resolveLine);
            if (linkedNpc != null && makeNpcFollowOnResolve)
            {
                linkedNpc.StartFollowing();
            }
        }
        else if (failed)
        {
            dialogueManager.SetExternalDialogueText(failureLine);
            if (linkedNpc != null && resumeCombatOnFailure)
            {
                linkedNpc.ResumeCombat();
            }
        }
        else
        {
            dialogueManager.SetExternalDialogueText(neutralEndLine);
        }

        yield return WaitForSecondsRealtimeSafe(endTextHoldSeconds);

        dialogueManager.EndExternalDialogueSession();
        SetPlayerLocked(false);
        isRunning = false;
    }

    private IEnumerator CollectTypedLine(string requiredWord, string[] offeredWords, Action<string> onComplete)
    {
        submittedText = string.Empty;
        submitRequested = false;

        if (runtimeInputField != null)
        {
            runtimeInputField.text = string.Empty;
        }

        RefreshWordButtons(offeredWords);
        UpdateRuntimeHeader(requiredWord, offeredWords);
        SetConversationUiVisible(true);

        if (EventSystem.current != null && runtimeInputField != null)
        {
            EventSystem.current.SetSelectedGameObject(runtimeInputField.gameObject);
            runtimeInputField.ActivateInputField();
        }

        while (!submitRequested)
        {
            yield return null;
        }

        onComplete?.Invoke(submittedText);
    }

    private IEnumerator RequestTypedTurn(
        int round,
        int instability,
        string requiredWord,
        string playerLine,
        string[] offeredWords,
        string[] detectedTags,
        List<string> recentTranscript,
        Action<TypedConversationResultData> onResult)
    {
        TTSRunner runner = TTSRunner.Instance;
        if (runner == null)
        {
            onResult?.Invoke(null);
            yield break;
        }

        TypedConversationRequestData request = new TypedConversationRequestData
        {
            role = aiRole,
            speakerName = string.IsNullOrWhiteSpace(speakerDisplayName) ? "Speaker" : speakerDisplayName.Trim(),
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
            instability = instability,
            stage = GetConversationStage(instability),
            requiredWord = requiredWord ?? string.Empty,
            playerTypedLine = playerLine ?? string.Empty,
            offeredWords = offeredWords ?? Array.Empty<string>(),
            detectedTags = detectedTags ?? Array.Empty<string>(),
            recentTranscript = recentTranscript != null ? recentTranscript.ToArray() : Array.Empty<string>(),
        };

        bool done = false;
        TypedConversationResultData result = null;
        runner.GenerateTypedConversationTurn(request, response =>
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

    private InputAnalysis AnalyzePlayerLine(string playerLine, string requiredWord)
    {
        InputAnalysis result = new InputAnalysis
        {
            tags = Array.Empty<string>(),
            rapportDelta = 0,
            instabilityDelta = 0,
            usedRequiredWord = false,
        };

        if (string.IsNullOrWhiteSpace(playerLine))
        {
            result.tags = new[] { "empty" };
            result.instabilityDelta = 8;
            return result;
        }

        string lower = playerLine.ToLowerInvariant();
        List<string> tags = new List<string>();

        bool grounding = ContainsAny(lower, groundingWords);
        bool memory = ContainsAny(lower, memoryWords);
        bool command = ContainsAny(lower, commandWords);
        bool guilt = ContainsAny(lower, guiltWords);
        bool relation = ContainsAny(lower, relationWords);
        bool validation = lower.Contains("hear") || lower.Contains("understand") || lower.Contains("sorry") || lower.Contains("believe") || lower.Contains("with you");
        bool accusation = lower.Contains("monster") || lower.Contains("coward") || lower.Contains("liar") || lower.Contains("murderer") || lower.Contains("weak")
            || lower.Contains("stupid") || lower.Contains("idiot") || lower.Contains("dumb") || lower.Contains("pathetic") || lower.Contains("crazy");
        bool asksQuestion = playerLine.Contains("?") || lower.StartsWith("who ") || lower.StartsWith("what ") || lower.StartsWith("why ") || lower.StartsWith("how ") || lower.StartsWith("where ");
        bool nameProbe = lower.Contains("your name") || lower.Contains("who are you") || lower.Contains("what are you") || lower.Contains("who were you");

        if (grounding)
        {
            tags.Add("grounding");
            result.rapportDelta += 1;
            result.instabilityDelta -= 10;
        }

        if (validation)
        {
            tags.Add("validation");
            result.rapportDelta += 1;
            result.instabilityDelta -= 8;
        }

        if (memory)
        {
            tags.Add("memory");
            result.rapportDelta += 1;
            result.instabilityDelta -= 4;
        }

        if (relation)
        {
            tags.Add("identity");
            result.rapportDelta += 1;
            result.instabilityDelta -= 2;
        }

        if (command)
        {
            tags.Add("command");
            result.instabilityDelta += 6;
        }

        if (guilt)
        {
            tags.Add("guilt");
            result.instabilityDelta += 8;
        }

        if (accusation)
        {
            tags.Add("insult");
            result.rapportDelta -= 1;
            result.instabilityDelta += 12;
        }

        if (asksQuestion)
        {
            tags.Add("question");
        }

        if (nameProbe)
        {
            tags.Add("identity_probe");
            result.instabilityDelta += 4;
        }

        if (!string.IsNullOrWhiteSpace(requiredWord) && lower.Contains(requiredWord.ToLowerInvariant()))
        {
            tags.Add("trigger_word_used");
            result.usedRequiredWord = true;
            result.instabilityDelta += 6;
        }

        if (playerLine.Trim().Length < minimumTypedCharacters)
        {
            tags.Add("short_line");
            result.instabilityDelta += 4;
        }

        if (tags.Count == 0)
        {
            tags.Add("unguarded");
            result.instabilityDelta += 6;
        }

        result.rapportDelta = Mathf.Clamp(result.rapportDelta, -1, 2);
        result.tags = tags.ToArray();
        return result;
    }

    private string BuildDialogueText(string aiLine, int rapport, int instability, int round, string requiredWord, string stateHint, string backstoryReveal)
    {
        string text = $"{speakerDisplayName}: {aiLine}\n\nTurn {round}/{maxTurns}\nRapport: {rapport}/{rapportToResolve}\nInstability: {instability}%";

        if (!string.IsNullOrWhiteSpace(requiredWord))
        {
            text += $"\nOptional trigger word: {requiredWord}";
        }

        if (!string.IsNullOrWhiteSpace(stateHint))
        {
            text += $"\nMood: {stateHint}";
        }

        if (!string.IsNullOrWhiteSpace(backstoryReveal))
        {
            text += $"\nShard: {backstoryReveal}";
        }

        return text;
    }

    private string ChooseRequiredWord(int round, int instability)
    {
        if (carrySuggestedWords != null && carrySuggestedWords.Length > 0)
        {
            return carrySuggestedWords[Mathf.Clamp(round - 1, 0, carrySuggestedWords.Length - 1)];
        }

        if (instability >= 60 && guiltWords.Length > 0)
        {
            return guiltWords[(round - 1) % guiltWords.Length];
        }

        if (commandWords.Length > 0)
        {
            return commandWords[(round - 1) % commandWords.Length];
        }

        if (memoryWords.Length > 0)
        {
            return memoryWords[(round - 1) % memoryWords.Length];
        }

        return "order";
    }

    private string[] BuildWordBank(string requiredWord)
    {
        List<string> words = new List<string>();
        AddWord(words, requiredWord);

        AddWords(words, carrySuggestedWords, 3);
        AddWords(words, groundingWords, 2);
        AddWords(words, memoryWords, 2);
        AddWords(words, relationWords, 2);

        if (words.Count < wordButtonCount)
        {
            AddWords(words, commandWords, 2);
        }

        if (words.Count < wordButtonCount)
        {
            AddWords(words, guiltWords, 2);
        }

        while (words.Count > Mathf.Max(4, wordButtonCount))
        {
            words.RemoveAt(words.Count - 1);
        }

        return words.ToArray();
    }

    private void EnsureRuntimeUi()
    {
        if (runtimeRoot != null)
        {
            return;
        }

        Canvas parentCanvas = FindObjectOfType<Canvas>();
        if (parentCanvas == null)
        {
            GameObject canvasObject = new GameObject("Typed Conversation Canvas");
            parentCanvas = canvasObject.AddComponent<Canvas>();
            parentCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObject.AddComponent<GraphicRaycaster>();
        }

        if (EventSystem.current == null)
        {
            GameObject eventSystemObject = new GameObject("Typed Conversation EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<StandaloneInputModule>();
        }

        runtimeRoot = new GameObject("Typed Conversation Overlay");
        runtimeRoot.transform.SetParent(parentCanvas.transform, false);
        Image backdrop = runtimeRoot.AddComponent<Image>();
        backdrop.color = new Color(0f, 0f, 0f, 0.25f);

        RectTransform rootRect = runtimeRoot.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0f, 0f);
        rootRect.anchorMax = new Vector2(1f, 0f);
        rootRect.pivot = new Vector2(0.5f, 0f);
        rootRect.sizeDelta = new Vector2(0f, 330f);
        rootRect.anchoredPosition = new Vector2(0f, 18f);

        GameObject panel = new GameObject("Panel");
        panel.transform.SetParent(runtimeRoot.transform, false);
        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = new Color(0.08f, 0.08f, 0.08f, 0.92f);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.08f, 0f);
        panelRect.anchorMax = new Vector2(0.92f, 1f);
        panelRect.offsetMin = new Vector2(0f, 18f);
        panelRect.offsetMax = new Vector2(0f, -18f);

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 16, 16);
        layout.spacing = 12f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        runtimeTitleText = CreateText(panel.transform, "TypedTitle", 22f, FontStyles.Bold);
        AddPreferredHeight(runtimeTitleText.gameObject, 28f);
        runtimeHintText = CreateText(panel.transform, "TypedHint", 17f, FontStyles.Normal);
        runtimeHintText.enableWordWrapping = true;
        AddPreferredHeight(runtimeHintText.gameObject, 40f);

        GameObject gridObject = new GameObject("WordGrid");
        gridObject.transform.SetParent(panel.transform, false);
        GridLayoutGroup grid = gridObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(150f, 32f);
        grid.spacing = new Vector2(8f, 8f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;
        LayoutElement gridLayout = gridObject.AddComponent<LayoutElement>();
        gridLayout.preferredHeight = 78f;

        int safeButtonCount = Mathf.Max(4, wordButtonCount);
        for (int i = 0; i < safeButtonCount; i++)
        {
            WordButtonBinding binding = CreateWordButton(gridObject.transform);
            runtimeWordButtons.Add(binding);
        }

        runtimeInputField = CreateInputField(panel.transform);

        GameObject buttonRow = new GameObject("Buttons");
        buttonRow.transform.SetParent(panel.transform, false);
        HorizontalLayoutGroup buttonLayout = buttonRow.AddComponent<HorizontalLayoutGroup>();
        buttonLayout.spacing = 12f;
        buttonLayout.childAlignment = TextAnchor.MiddleRight;
        buttonLayout.childControlHeight = false;
        buttonLayout.childControlWidth = false;
        buttonLayout.childForceExpandHeight = false;
        buttonLayout.childForceExpandWidth = false;
        AddPreferredHeight(buttonRow, 44f);

        runtimeClearButton = CreateActionButton(buttonRow.transform, "Clear", () =>
        {
            if (runtimeInputField != null)
            {
                runtimeInputField.text = string.Empty;
                runtimeInputField.ActivateInputField();
            }
        });

        runtimeSubmitButton = CreateActionButton(buttonRow.transform, "Submit", SubmitTypedLine);
        runtimeRoot.SetActive(false);
    }

    private void UpdateRuntimeHeader(string requiredWord, string[] offeredWords)
    {
        if (runtimeTitleText != null)
        {
            runtimeTitleText.text = $"Build your reply. Optional trigger word: {requiredWord}";
        }

        if (runtimeHintText != null)
        {
            runtimeHintText.text = $"Type your own line first. Use the word bank only if you want to pressure, trigger, or rattle him: {string.Join(", ", offeredWords ?? Array.Empty<string>())}";
        }
    }

    private void RefreshWordButtons(string[] offeredWords)
    {
        for (int i = 0; i < runtimeWordButtons.Count; i++)
        {
            bool show = offeredWords != null && i < offeredWords.Length && !string.IsNullOrWhiteSpace(offeredWords[i]);
            runtimeWordButtons[i].button.gameObject.SetActive(show);
            if (!show)
            {
                continue;
            }

            string word = offeredWords[i].Trim();
            runtimeWordButtons[i].label.text = word;
            runtimeWordButtons[i].button.onClick.RemoveAllListeners();
            runtimeWordButtons[i].button.onClick.AddListener(() => AppendWordToInput(word));
        }
    }

    private void AppendWordToInput(string word)
    {
        if (runtimeInputField == null || string.IsNullOrWhiteSpace(word))
        {
            return;
        }

        string existing = runtimeInputField.text ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(existing) && !existing.EndsWith(" "))
        {
            existing += " ";
        }

        runtimeInputField.text = existing + word;
        runtimeInputField.caretPosition = runtimeInputField.text.Length;
        runtimeInputField.ActivateInputField();
    }

    private void SubmitTypedLine()
    {
        if (runtimeInputField == null)
        {
            return;
        }

        string line = (runtimeInputField.text ?? string.Empty).Trim();
        if (line.Length < 1)
        {
            return;
        }

        submittedText = line;
        submitRequested = true;
    }

    private void SetConversationUiVisible(bool visible)
    {
        if (runtimeRoot != null)
        {
            runtimeRoot.SetActive(visible);
        }
    }

    private Button CreateActionButton(Transform parent, string label, Action onClick)
    {
        GameObject buttonObject = new GameObject(label + "Button");
        buttonObject.transform.SetParent(parent, false);
        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.18f, 0.18f, 0.18f, 1f);

        Button button = buttonObject.AddComponent<Button>();
        button.onClick.AddListener(() => onClick?.Invoke());

        LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
        layout.preferredWidth = label == "Submit" ? 132f : 100f;
        layout.preferredHeight = 38f;

        TextMeshProUGUI text = CreateText(buttonObject.transform, label + "Label", 18f, FontStyles.Bold);
        text.alignment = TextAlignmentOptions.Center;
        RectTransform rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        text.text = label;

        return button;
    }

    private WordButtonBinding CreateWordButton(Transform parent)
    {
        GameObject buttonObject = new GameObject("WordButton");
        buttonObject.transform.SetParent(parent, false);
        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.14f, 0.14f, 0.14f, 1f);

        Button button = buttonObject.AddComponent<Button>();
        TextMeshProUGUI label = CreateText(buttonObject.transform, "Label", 16f, FontStyles.Normal);
        label.alignment = TextAlignmentOptions.Center;
        RectTransform rect = label.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        return new WordButtonBinding
        {
            button = button,
            label = label,
        };
    }

    private TMP_InputField CreateInputField(Transform parent)
    {
        GameObject root = new GameObject("TypedInputField");
        root.transform.SetParent(parent, false);
        Image image = root.AddComponent<Image>();
        image.color = new Color(0.18f, 0.18f, 0.18f, 1f);
        Outline outline = root.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.18f);
        outline.effectDistance = new Vector2(1f, -1f);
        LayoutElement layout = root.AddComponent<LayoutElement>();
        layout.preferredHeight = 72f;
        layout.minHeight = 72f;

        TMP_InputField inputField = root.AddComponent<TMP_InputField>();

        GameObject viewport = new GameObject("Viewport");
        viewport.transform.SetParent(root.transform, false);
        RectTransform viewportRect = viewport.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(14f, 10f);
        viewportRect.offsetMax = new Vector2(-14f, -10f);
        viewport.AddComponent<RectMask2D>();

        TextMeshProUGUI text = CreateText(viewport.transform, "Text", 20f, FontStyles.Normal);
        text.enableWordWrapping = true;
        text.alignment = TextAlignmentOptions.Left;
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        TextMeshProUGUI placeholder = CreateText(viewport.transform, "Placeholder", 20f, FontStyles.Italic);
        placeholder.color = new Color(1f, 1f, 1f, 0.34f);
        placeholder.text = "Type your sentence here...";
        RectTransform placeholderRect = placeholder.rectTransform;
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = Vector2.zero;
        placeholderRect.offsetMax = Vector2.zero;

        inputField.textViewport = viewportRect;
        inputField.textComponent = text;
        inputField.placeholder = placeholder;
        inputField.lineType = TMP_InputField.LineType.SingleLine;
        inputField.richText = false;
        inputField.resetOnDeActivation = false;
        inputField.caretColor = Color.white;
        inputField.customCaretColor = true;
        inputField.onSubmit.AddListener(_ => SubmitTypedLine());
        return inputField;
    }

    private TextMeshProUGUI CreateText(Transform parent, string objectName, float fontSize, FontStyles style)
    {
        GameObject textObject = new GameObject(objectName);
        textObject.transform.SetParent(parent, false);

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Left;
        text.text = string.Empty;

        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        return text;
    }

    private void SetPlayerLocked(bool locked)
    {
        if (!lockPlayerScriptsDuringConversation || playerScriptsToDisable == null)
        {
            return;
        }

        for (int i = 0; i < playerScriptsToDisable.Length; i++)
        {
            if (playerScriptsToDisable[i] != null)
            {
                playerScriptsToDisable[i].enabled = !locked;
            }
        }
    }

    private void CachePlayerTransform()
    {
        if (playerTransform != null)
        {
            return;
        }

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            playerTransform = playerObject.transform;
        }
    }

    private string GetConversationStage(int instability)
    {
        if (instability >= 80) return "breaking";
        if (instability >= 60) return "fractured";
        if (instability >= 35) return "volatile";
        return "guarded";
    }

    private bool ContainsAny(string text, string[] words)
    {
        if (string.IsNullOrWhiteSpace(text) || words == null)
        {
            return false;
        }

        for (int i = 0; i < words.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(words[i]) && text.Contains(words[i].ToLowerInvariant()))
            {
                return true;
            }
        }

        return false;
    }

    private void AddWords(List<string> destination, string[] source, int maxCount)
    {
        if (destination == null || source == null || maxCount <= 0)
        {
            return;
        }

        for (int i = 0; i < source.Length && maxCount > 0; i++)
        {
            if (AddWord(destination, source[i]))
            {
                maxCount--;
            }
        }
    }

    private bool AddWord(List<string> destination, string word)
    {
        if (destination == null || string.IsNullOrWhiteSpace(word))
        {
            return false;
        }

        string trimmed = word.Trim();
        for (int i = 0; i < destination.Count; i++)
        {
            if (string.Equals(destination[i], trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        destination.Add(trimmed);
        return true;
    }

    private void TrimRecentTranscript(List<string> transcript)
    {
        while (transcript != null && transcript.Count > 10)
        {
            transcript.RemoveAt(0);
        }
    }

    private void AddPreferredHeight(GameObject target, float preferredHeight)
    {
        if (target == null)
        {
            return;
        }

        LayoutElement layout = target.GetComponent<LayoutElement>();
        if (layout == null)
        {
            layout = target.AddComponent<LayoutElement>();
        }

        layout.preferredHeight = preferredHeight;
        layout.minHeight = preferredHeight;
    }

    private IEnumerator WaitForSecondsRealtimeSafe(float seconds)
    {
        float endAt = Time.realtimeSinceStartup + Mathf.Max(0f, seconds);
        while (Time.realtimeSinceStartup < endAt)
        {
            yield return null;
        }
    }
}
