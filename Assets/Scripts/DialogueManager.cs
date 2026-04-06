using UnityEngine;
using TMPro;
using Ink.Runtime;
using System;
using System.Collections.Generic;

public class DialogueManager : MonoBehaviour
{
    [Header("Dialogue UI")]
    [SerializeField] private GameObject dialoguePanel;
    [SerializeField] private TextMeshProUGUI dialogueText;
    [SerializeField] private GameObject continueButton;

    [Header("Choices UI")]
    [SerializeField] private GameObject[] choices;

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
        return TryBeginExternalDialogueSession(openingText, Array.Empty<string>(), null);
    }

    public bool TryBeginExternalDialogueSession(string openingText, IReadOnlyList<string> choiceTexts, Action<int> onChoiceSelected)
    {
        if (dialogueIsPlaying)
        {
            return false;
        }

        dialogueIsPlaying = true;
        externalDialogueSession = true;
        externalChoiceCallback = onChoiceSelected;

        if (dialoguePanel != null) dialoguePanel.SetActive(true);
        if (dialogueText != null) dialogueText.text = openingText ?? string.Empty;
        if (continueButton != null) continueButton.SetActive(false);

        ApplyExternalChoices(choiceTexts);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        return true;
    }

    public void SetExternalDialogueText(string text)
    {
        if (!externalDialogueSession || dialogueText == null)
        {
            return;
        }

        dialogueText.text = text ?? string.Empty;
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

        if (dialoguePanel != null) dialoguePanel.SetActive(false);
        if (dialogueText != null) dialogueText.text = string.Empty;
        if (continueButton != null) continueButton.SetActive(false);

        HideAllChoiceButtons();

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
                if (txt != null) txt.text = currentChoices[i].text;
            }
            else
            {
                choices[i].gameObject.SetActive(false);
            }
        }
    }

    public void MakeChoice(int choiceIndex)
    {
        if (externalDialogueSession)
        {
            HandleExternalChoice(choiceIndex);
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

    private void ExitDialogueMode()
    {
        dialogueIsPlaying = false;
        externalDialogueSession = false;
        externalChoiceCallback = null;
        if (dialoguePanel != null) dialoguePanel.SetActive(false);

        npcPoints = 0;
        dialogueRounds = 0;
        currentStory = null;
        currentNPC = null;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        Debug.Log("Dialogue Mode Exited. Shooting should be re-enabled.");
    }

    private void ApplyExternalChoices(IReadOnlyList<string> choiceTexts)
    {
        if (choices == null)
        {
            return;
        }

        int count = choiceTexts == null ? 0 : choiceTexts.Count;
        for (int i = 0; i < choices.Length; i++)
        {
            if (choices[i] == null) continue;

            if (i < count && !string.IsNullOrWhiteSpace(choiceTexts[i]))
            {
                choices[i].SetActive(true);
                TextMeshProUGUI txt = choices[i].GetComponentInChildren<TextMeshProUGUI>();
                if (txt != null) txt.text = choiceTexts[i];
            }
            else
            {
                choices[i].SetActive(false);
            }
        }
    }

    private void HideAllChoiceButtons()
    {
        if (choices == null) return;
        for (int i = 0; i < choices.Length; i++)
        {
            if (choices[i] != null) choices[i].SetActive(false);
        }
    }

    private void HandleExternalChoice(int choiceIndex)
    {
        int available = 0;
        if (choices != null)
        {
            for (int i = 0; i < choices.Length; i++)
            {
                if (choices[i] != null && choices[i].activeSelf)
                {
                    available++;
                }
            }
        }

        if (available <= 0)
        {
            return;
        }

        int safeIndex = Mathf.Clamp(choiceIndex, 0, available - 1);
        HideAllChoiceButtons();

        Action<int> callback = externalChoiceCallback;
        externalChoiceCallback = null;
        callback?.Invoke(safeIndex);
    }
}




