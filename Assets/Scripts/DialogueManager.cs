using UnityEngine;
using TMPro;
using Ink.Runtime;
using System.Collections.Generic;
using UnityEngine.UI;

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

        // --- CRITICAL FIX: Ensure this is false when the game starts ---
        dialogueIsPlaying = false;
        dialoguePanel.SetActive(false);
    }

    public static DialogueManager GetInstance() => instance;

    public void EnterDialogueMode(TextAsset inkJSON, NPC npc)
    {
        currentNPC = npc;
        npcPoints = 0;
        dialogueRounds = 0;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null && player.GetComponent<PlayerUI>() != null)
            player.GetComponent<PlayerUI>().UpdateText(string.Empty);

        currentStory = new Story(inkJSON.text);
        dialogueIsPlaying = true; // Block shooting now
        dialoguePanel.SetActive(true);
        continueButton.SetActive(false);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        ContinueStory();
    }

    public void OnContinuePressed()
    {
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
        if (currentStory.canContinue)
        {
            dialogueText.text = currentStory.Continue();
            DisplayChoices();

            if (currentStory.currentChoices.Count > 0)
            {
                int randomIndex = Random.Range(0, currentStory.currentChoices.Count);
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
        List<Choice> currentChoices = currentStory.currentChoices;
        continueButton.SetActive(currentChoices.Count == 0);

        for (int i = 0; i < choices.Length; i++)
        {
            if (i < currentChoices.Count)
            {
                choices[i].gameObject.SetActive(true);
                choices[i].GetComponentInChildren<TextMeshProUGUI>().text = currentChoices[i].text;
            }
            else
            {
                choices[i].gameObject.SetActive(false);
            }
        }
    }

    public void MakeChoice(int choiceIndex)
    {
        if (currentStory.currentChoices[choiceIndex].text == pointWinningText)
        {
            npcPoints++;
        }

        currentStory.ChooseChoiceIndex(choiceIndex);
        dialogueRounds++;

        foreach (GameObject choiceButton in choices)
        {
            choiceButton.SetActive(false);
        }

        if (dialogueRounds < 3)
        {
            ContinueStory();
        }
        else
        {
            if (currentStory.canContinue)
            {
                dialogueText.text = currentStory.Continue();
            }

            continueButton.SetActive(true);
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
        // --- RESET EVERYTHING FOR GAMEPLAY ---
        dialogueIsPlaying = false;
        dialoguePanel.SetActive(false);

        npcPoints = 0;
        dialogueRounds = 0;
        currentStory = null;
        currentNPC = null;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        Debug.Log("Dialogue Mode Exited. Shooting should be re-enabled.");
    }
}
