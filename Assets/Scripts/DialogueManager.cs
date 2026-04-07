using System;
using UnityEngine;
using TMPro;
using Ink.Runtime;
using System.Collections.Generic;

public class DialogueManager : MonoBehaviour
{
    [Header("Dialogue UI")]
    [SerializeField] private GameObject dialoguePanel;
    [SerializeField] private TextMeshProUGUI dialogueText;
    [SerializeField] private GameObject continueButton;

    [Header("Choices UI")]
    [SerializeField] private GameObject[] choices;
    [SerializeField] private float externalChoiceVerticalOffset = -70f;

    [Header("NPC Logic")]
    private int npcPoints = 0;
    private int dialogueRounds = 0;
    private string pointWinningText;
    private NPC currentNPC;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip successSound;
    [SerializeField] private AudioClip failureSound;

    private Story currentStory;
    private bool externalDialogueSession;
    private Action<int> externalChoiceCallback;
    private Action<int> externalInlineChoiceCallback;
    private bool externalChoiceShouldEndSession;
    private bool externalGeneratedChoiceSession;
    private Action<int> externalGeneratedChoiceCallback;
    [SerializeField] private GameObject[] generatedChoiceButtons;
    private bool externalDialogueTopLayoutActive;
    private bool cachedDialoguePanelLayout;
    private Vector2 cachedDialogueAnchorMin;
    private Vector2 cachedDialogueAnchorMax;
    private Vector2 cachedDialoguePivot;
    private Vector2 cachedDialogueAnchoredPosition;
    private Vector2 cachedDialogueSizeDelta;
    private float cachedDialogueFontSize;
    private TextAlignmentOptions cachedDialogueAlignment;
    private bool cachedDialogueTextLayout;
    private Vector2 cachedDialogueTextAnchorMin;
    private Vector2 cachedDialogueTextAnchorMax;
    private Vector2 cachedDialogueTextPivot;
    private Vector2 cachedDialogueTextOffsetMin;
    private Vector2 cachedDialogueTextOffsetMax;
    private bool cachedChoiceLayout;
    private Vector2[] cachedChoiceAnchoredPositions;

    // This is the variable your Weapon script checks
    public bool dialogueIsPlaying { get; private set; }

    private static DialogueManager instance;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this.gameObject);
            return;
        }
        instance = this;

        dialogueIsPlaying = false;
        externalDialogueSession = false;
        if (dialoguePanel != null) dialoguePanel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    public static DialogueManager GetInstance() => instance;

    public bool TryBeginExternalDialogueSession(string openingText)
    {
        if (dialogueIsPlaying)
        {
            return false;
        }

        dialogueIsPlaying = true;
        externalDialogueSession = true;
        externalChoiceCallback = null;

        if (dialoguePanel != null) dialoguePanel.SetActive(true);
        if (dialogueText != null) dialogueText.text = openingText ?? string.Empty;
        if (continueButton != null) continueButton.SetActive(false);

        if (choices != null)
        {
            for (int i = 0; i < choices.Length; i++)
            {
                if (choices[i] != null) choices[i].SetActive(false);
            }
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        return true;
    }

    public bool BeginExternalChoiceSession(string prompt, string[] optionTexts, Action<int> choiceCallback)
    {
        if (dialogueIsPlaying)
        {
            return false;
        }

        if (optionTexts == null)
        {
            optionTexts = new string[0];
        }

        dialogueIsPlaying = true;
        externalDialogueSession = true;
        externalChoiceCallback = choiceCallback;
        externalChoiceShouldEndSession = true;

        if (dialoguePanel != null) dialoguePanel.SetActive(true);
        if (dialogueText != null) dialogueText.text = prompt ?? string.Empty;
        if (continueButton != null) continueButton.SetActive(false);

        if (choices != null)
        {
            for (int i = 0; i < choices.Length; i++)
            {
                if (choices[i] == null) continue;
                if (i < optionTexts.Length)
                {
                    choices[i].SetActive(true);
                    TextMeshProUGUI txt = choices[i].GetComponentInChildren<TextMeshProUGUI>();
                    if (txt != null) txt.text = optionTexts[i];
                }
                else
                {
                    choices[i].SetActive(false);
                }
            }
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        return true;
    }

    public bool ShowExternalChoiceOptions(string prompt, string[] optionTexts, Action<int> choiceCallback)
    {
        if (!externalDialogueSession)
        {
            return false;
        }

        externalInlineChoiceCallback = choiceCallback;
        externalChoiceCallback = choiceCallback;
        externalChoiceShouldEndSession = false;

        if (dialoguePanel != null) dialoguePanel.SetActive(true);
        if (dialogueText != null) dialogueText.text = prompt ?? string.Empty;
        if (continueButton != null) continueButton.SetActive(false);

        if (choices != null)
        {
            for (int i = 0; i < choices.Length; i++)
            {
                if (choices[i] == null) continue;

                if (optionTexts != null && i < optionTexts.Length)
                {
                    choices[i].SetActive(true);
                    TextMeshProUGUI txt = choices[i].GetComponentInChildren<TextMeshProUGUI>();
                    if (txt != null)
                    {
                        txt.text = optionTexts[i];
                        ResizeButtonToFitText(choices[i], txt);
                    }
                }
                else
                {
                    choices[i].SetActive(false);
                }
            }
        }

        return true;
    }

    public void HideExternalChoiceOptions()
    {
        externalInlineChoiceCallback = null;
        externalChoiceCallback = null;
        externalChoiceShouldEndSession = false;

        if (choices != null)
        {
            for (int i = 0; i < choices.Length; i++)
            {
                if (choices[i] != null) choices[i].SetActive(false);
            }
        }
    }

    public void SetExternalDialogueText(string text)
    {
        if (!externalDialogueSession || dialogueText == null) return;
        dialogueText.text = text ?? string.Empty;
    }

    public void SetExternalDialogueTopLayout(bool enabled)
    {
        if (dialoguePanel == null || dialogueText == null)
        {
            return;
        }

        RectTransform rect = dialoguePanel.GetComponent<RectTransform>();
        RectTransform textRect = dialogueText.GetComponent<RectTransform>();
        if (rect == null)
        {
            return;
        }

        if (enabled)
        {
            if (!cachedDialoguePanelLayout)
            {
                cachedDialogueAnchorMin = rect.anchorMin;
                cachedDialogueAnchorMax = rect.anchorMax;
                cachedDialoguePivot = rect.pivot;
                cachedDialogueAnchoredPosition = rect.anchoredPosition;
                cachedDialogueSizeDelta = rect.sizeDelta;
                cachedDialogueFontSize = dialogueText.fontSize;
                cachedDialogueAlignment = dialogueText.alignment;
                cachedDialoguePanelLayout = true;
            }

            if (textRect != null && !cachedDialogueTextLayout)
            {
                cachedDialogueTextAnchorMin = textRect.anchorMin;
                cachedDialogueTextAnchorMax = textRect.anchorMax;
                cachedDialogueTextPivot = textRect.pivot;
                cachedDialogueTextOffsetMin = textRect.offsetMin;
                cachedDialogueTextOffsetMax = textRect.offsetMax;
                cachedDialogueTextLayout = true;
            }

            rect.anchorMin = new Vector2(0.08f, 1f);
            rect.anchorMax = new Vector2(0.92f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -28f);
            rect.sizeDelta = new Vector2(0f, 110f);

            if (textRect != null)
            {
                textRect.anchorMin = new Vector2(0f, 0f);
                textRect.anchorMax = new Vector2(1f, 1f);
                textRect.pivot = new Vector2(0.5f, 0.5f);
                textRect.offsetMin = new Vector2(22f, 14f);
                textRect.offsetMax = new Vector2(-22f, -14f);
            }

            dialogueText.fontSize = 18f;
            dialogueText.alignment = TextAlignmentOptions.TopLeft;
            dialogueText.enableWordWrapping = true;
            externalDialogueTopLayoutActive = true;
            return;
        }

        if (!cachedDialoguePanelLayout)
        {
            return;
        }

        rect.anchorMin = cachedDialogueAnchorMin;
        rect.anchorMax = cachedDialogueAnchorMax;
        rect.pivot = cachedDialoguePivot;
        rect.anchoredPosition = cachedDialogueAnchoredPosition;
        rect.sizeDelta = cachedDialogueSizeDelta;
        dialogueText.fontSize = cachedDialogueFontSize;
        dialogueText.alignment = cachedDialogueAlignment;

        if (textRect != null && cachedDialogueTextLayout)
        {
            textRect.anchorMin = cachedDialogueTextAnchorMin;
            textRect.anchorMax = cachedDialogueTextAnchorMax;
            textRect.pivot = cachedDialogueTextPivot;
            textRect.offsetMin = cachedDialogueTextOffsetMin;
            textRect.offsetMax = cachedDialogueTextOffsetMax;
        }

        externalDialogueTopLayoutActive = false;
    }

    public bool BeginGeneratedChoiceSession(string prompt, string[] optionTexts, Action<int> choiceCallback)
    {
        if (dialogueIsPlaying) return false;
        if (optionTexts == null) optionTexts = new string[0];

        dialogueIsPlaying = true;
        externalGeneratedChoiceSession = true;
        externalGeneratedChoiceCallback = choiceCallback;

        if (dialoguePanel != null) dialoguePanel.SetActive(true);
        if (dialogueText != null) dialogueText.text = prompt ?? string.Empty;
        if (continueButton != null) continueButton.SetActive(false);

        if (choices != null)
            for (int i = 0; i < choices.Length; i++)
                if (choices[i] != null) choices[i].SetActive(false);

        if (choices != null && choices.Length > 0)
        {
            for (int i = 0; i < choices.Length; i++)
            {
                if (choices[i] == null) continue;
                if (i < optionTexts.Length)
                {
                    choices[i].SetActive(true);
                    TextMeshProUGUI txt = choices[i].GetComponentInChildren<TextMeshProUGUI>();
                    if (txt != null)
                    {
                        txt.text = optionTexts[i];
                        ResizeButtonToFitText(choices[i], txt);
                    }
                }
                else
                {
                    choices[i].SetActive(false);
                }
            }
        }
        else if (generatedChoiceButtons != null && generatedChoiceButtons.Length > 0)
        {
            for (int i = 0; i < generatedChoiceButtons.Length; i++)
            {
                if (generatedChoiceButtons[i] == null) continue;
                if (i < optionTexts.Length)
                {
                    generatedChoiceButtons[i].SetActive(true);
                    TextMeshProUGUI txt = generatedChoiceButtons[i].GetComponentInChildren<TextMeshProUGUI>();
                    if (txt != null) 
                    {
                        txt.text = optionTexts[i];
                        ResizeButtonToFitText(generatedChoiceButtons[i], txt);
                    }
                }
                else
                {
                    generatedChoiceButtons[i].SetActive(false);
                }
            }
        }

        ApplyExternalChoiceOffset();
        ApplyExternalChoiceOffset();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        ApplyExternalChoiceOffset();
        return true;
    }

    public void EndExternalDialogueSession()
    {
        if (!externalDialogueSession)
        {
            return;
        }

        externalDialogueSession = false;
        dialogueIsPlaying = false;
        externalChoiceCallback = null;
        externalInlineChoiceCallback = null;
        externalChoiceShouldEndSession = false;

        if (externalDialogueTopLayoutActive)
        {
            SetExternalDialogueTopLayout(false);
        }

        if (dialoguePanel != null) dialoguePanel.SetActive(false);
        if (dialogueText != null) dialogueText.text = string.Empty;
        if (continueButton != null) continueButton.SetActive(false);

        if (choices != null)
        {
            for (int i = 0; i < choices.Length; i++)
            {
                if (choices[i] != null) choices[i].SetActive(false);
            }
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void EndGeneratedChoiceSession()
    {
        if (!externalGeneratedChoiceSession)
        {
            return;
        }

        externalGeneratedChoiceSession = false;
        dialogueIsPlaying = false;
        externalGeneratedChoiceCallback = null;

        if (dialoguePanel != null) dialoguePanel.SetActive(false);
        if (dialogueText != null) dialogueText.text = string.Empty;
        if (continueButton != null) continueButton.SetActive(false);

        if (generatedChoiceButtons != null)
        {
            for (int i = 0; i < generatedChoiceButtons.Length; i++)
            {
                if (generatedChoiceButtons[i] != null) generatedChoiceButtons[i].SetActive(false);
            }
        }

        if (choices != null)
        {
            for (int i = 0; i < choices.Length; i++)
            {
                if (choices[i] != null) choices[i].SetActive(false);
            }
        }

        RestoreChoiceLayout();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void EnterDialogueMode(TextAsset inkJSON, NPC npc)
    {
        if (inkJSON == null)
        {
            Debug.LogError("DialogueManager: Cannot enter dialogue mode because inkJSON is missing.");
            return;
        }

        if (npc == null)
        {
            Debug.LogError("DialogueManager: Cannot enter dialogue mode because NPC reference is missing.");
            return;
        }

        if (dialogueIsPlaying)
        {
            return;
        }

        externalDialogueSession = false;
        currentNPC = npc;
        npcPoints = 0;
        dialogueRounds = 0;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null && player.GetComponent<PlayerUI>() != null)
            player.GetComponent<PlayerUI>().UpdateText(string.Empty);

        currentStory = new Story(inkJSON.text);
        dialogueIsPlaying = true;
        if (dialoguePanel != null) dialoguePanel.SetActive(true);
        if (continueButton != null) continueButton.SetActive(false);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        ContinueStory();
    }

    public void OnContinuePressed()
    {
        if (externalDialogueSession || currentStory == null)
        {
            return;
        }

        if (currentStory.canContinue)
        {
            ContinueStory();
        }
        else
        {
            ExitDialogueMode();
        }

        RestoreChoiceLayout();
    }

    private void ContinueStory()
    {
        if (currentStory == null)
        {
            return;
        }

        if (currentStory.canContinue)
        {
            if (dialogueText != null) dialogueText.text = currentStory.Continue();
            DisplayChoices();

            if (currentStory.currentChoices.Count > 0)
            {
                int randomIndex = UnityEngine.Random.Range(0, currentStory.currentChoices.Count);
                pointWinningText = currentStory.currentChoices[randomIndex].text;
                Debug.Log($"Round {dialogueRounds + 1} correct choice: {pointWinningText}");
            }
        }
        else if (currentStory.currentChoices.Count == 0)
        {
            ExitDialogueMode();
        }
    }

    private void DisplayChoices()
    {
        if (currentStory == null)
        {
            return;
        }

        List<Choice> currentChoices = currentStory.currentChoices;
        if (continueButton != null) continueButton.SetActive(currentChoices.Count == 0);

        if (choices == null)
        {
            return;
        }

        for (int i = 0; i < choices.Length; i++)
        {
            if (choices[i] == null) continue;

            if (i < currentChoices.Count)
            {
                choices[i].gameObject.SetActive(true);
                TextMeshProUGUI txt = choices[i].GetComponentInChildren<TextMeshProUGUI>();
                if (txt != null) 
                {
                    txt.text = currentChoices[i].text;
                    ResizeButtonToFitText(choices[i], txt);
                }
            }
            else
            {
                choices[i].gameObject.SetActive(false);
            }
        }
    }

    public void MakeChoice(int choiceIndex)
    {
        if (externalInlineChoiceCallback != null)
        {
            Action<int> callback = externalInlineChoiceCallback;
            externalInlineChoiceCallback = null;
            externalChoiceCallback = null;
            externalChoiceShouldEndSession = false;
            HideExternalChoiceOptions();
            callback?.Invoke(choiceIndex);
            return;
        }

        if (externalGeneratedChoiceSession)
        {
            if (externalGeneratedChoiceCallback != null)
            {
                externalGeneratedChoiceCallback(choiceIndex);
                externalGeneratedChoiceCallback = null;
            }
            EndGeneratedChoiceSession();
            return;
        }

        if (externalDialogueSession)
        {
            if (externalChoiceCallback != null)
            {
                Action<int> callback = externalChoiceCallback;
                externalChoiceCallback = null;
                bool shouldEndSession = externalChoiceShouldEndSession;
                externalChoiceShouldEndSession = false;
                callback(choiceIndex);

                if (shouldEndSession)
                {
                    EndExternalDialogueSession();
                }
                else
                {
                    HideExternalChoiceOptions();
                }
                return;
            }

            return;
        }

        if (currentStory == null)
        {
            return;
        }

        int choiceCount = currentStory.currentChoices.Count;
        Debug.Log($"DialogueManager.MakeChoice called with index={choiceIndex}, availableChoices={choiceCount}");
        if (choiceCount <= 0)
        {
            Debug.LogWarning("DialogueManager.MakeChoice: No choices available, exiting dialogue mode.");
            ExitDialogueMode();
            return;
        }

        if (choiceIndex < 0 || choiceIndex >= choiceCount)
        {
            Debug.LogWarning($"DialogueManager.MakeChoice: Invalid choiceIndex={choiceIndex} for {choiceCount} choices. Falling back to index 0.");
            choiceIndex = 0;
        }

        if (currentStory.currentChoices[choiceIndex].text == pointWinningText)
        {
            npcPoints++;
        }

        currentStory.ChooseChoiceIndex(choiceIndex);
        dialogueRounds++;

        if (choices != null)
        {
            foreach (GameObject choiceButton in choices)
            {
                if (choiceButton != null) choiceButton.SetActive(false);
            }
        }

        if (dialogueRounds < 3)
        {
            ContinueStory();
        }
        else
        {
            if (currentStory.canContinue && dialogueText != null)
            {
                dialogueText.text = currentStory.Continue();
            }

            if (continueButton != null) continueButton.SetActive(true);
            CheckNPCFollowStatus();
        }
    }

    private void CheckNPCFollowStatus()
    {
        if (currentNPC == null) return;

        if (npcPoints >= 3)
        {
            Debug.Log($"NPC dialogue result: SUCCESS ({npcPoints}/3). NPC should follow now.");
            if (audioSource != null && successSound != null) audioSource.PlayOneShot(successSound);
            currentNPC.StartFollowing();
        }
        else
        {
            Debug.Log($"NPC dialogue result: FAIL ({npcPoints}/3). NPC resumes combat.");
            if (audioSource != null && failureSound != null) audioSource.PlayOneShot(failureSound);
            currentNPC.ResumeCombat();
        }
    }

    public void ExitDialogueMode()
    {
        dialogueIsPlaying = false;
        externalDialogueSession = false;
        externalGeneratedChoiceSession = false;
        externalChoiceCallback = null;
        externalInlineChoiceCallback = null;
        externalGeneratedChoiceCallback = null;
        if (dialoguePanel != null) dialoguePanel.SetActive(false);

        npcPoints = 0;
        dialogueRounds = 0;
        currentStory = null;
        currentNPC = null;

        RestoreChoiceLayout();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        Debug.Log("Dialogue Mode Exited. Shooting should be re-enabled.");
    }

    private void ResizeButtonToFitText(GameObject button, TextMeshProUGUI text)
    {
        if (button == null || text == null) return;

        RectTransform rt = button.GetComponent<RectTransform>();
        if (rt == null) return;

        text.enableWordWrapping = true;

        float paddingX = 20f;
        float paddingY = 14f;
        float maxWidth = 280f;

        RectTransform textRect = text.GetComponent<RectTransform>();
        if (textRect != null)
        {
            textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, maxWidth - paddingX);
        }

        float preferredWidth = Mathf.Min(text.preferredWidth + paddingX, maxWidth);
        float preferredHeight = Mathf.Max(48f, text.preferredHeight + paddingY);

        rt.sizeDelta = new Vector2(preferredWidth, preferredHeight);
    }

    private void ApplyExternalChoiceOffset()
    {
        if (choices == null || choices.Length == 0)
        {
            return;
        }

        if (!cachedChoiceLayout)
        {
            cachedChoiceAnchoredPositions = new Vector2[choices.Length];
            for (int i = 0; i < choices.Length; i++)
            {
                RectTransform rect = choices[i] != null ? choices[i].GetComponent<RectTransform>() : null;
                cachedChoiceAnchoredPositions[i] = rect != null ? rect.anchoredPosition : Vector2.zero;
            }
            cachedChoiceLayout = true;
        }

        for (int i = 0; i < choices.Length; i++)
        {
            RectTransform rect = choices[i] != null ? choices[i].GetComponent<RectTransform>() : null;
            if (rect == null)
            {
                continue;
            }

            Vector2 basePosition = cachedChoiceAnchoredPositions[i];
            rect.anchoredPosition = new Vector2(basePosition.x, basePosition.y + externalChoiceVerticalOffset);
        }
    }

    private void RestoreChoiceLayout()
    {
        if (!cachedChoiceLayout || choices == null || cachedChoiceAnchoredPositions == null)
        {
            return;
        }

        for (int i = 0; i < choices.Length && i < cachedChoiceAnchoredPositions.Length; i++)
        {
            RectTransform rect = choices[i] != null ? choices[i].GetComponent<RectTransform>() : null;
            if (rect == null)
            {
                continue;
            }

            rect.anchoredPosition = cachedChoiceAnchoredPositions[i];
        }
    }
}




