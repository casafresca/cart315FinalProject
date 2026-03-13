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
    public bool dialogueIsPlaying { get; private set; }

    private static DialogueManager instance;

    private void Awake()
    {
        instance = this;
        dialoguePanel.SetActive(false);
    }

    public static DialogueManager GetInstance() => instance;

    
    public void EnterDialogueMode(TextAsset inkJSON, NPC npc)
    {
        currentNPC = npc;
        npcPoints = 0;
        dialogueRounds = 0;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) player.GetComponent<PlayerUI>().UpdateText(string.Empty);

        currentStory = new Story(inkJSON.text);
        dialogueIsPlaying = true;
        dialoguePanel.SetActive(true);
        continueButton.SetActive(false);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        ContinueStory();
    }

    public void OnContinuePressed()
    {
        ContinueStory();
    }

    private void ContinueStory()
    {
        if (currentStory.canContinue)
        {
            dialogueText.text = currentStory.Continue();
            DisplayChoices();

            // RANDOMIZATION LOGIC:
            // Every time new choices appear, pick one randomly to be the "correct" one.
            if (currentStory.currentChoices.Count > 0)
            {
                int randomIndex = Random.Range(0, currentStory.currentChoices.Count);
                pointWinningText = currentStory.currentChoices[randomIndex].text;
                Debug.Log("Round " + (dialogueRounds + 1) + " correct choice is: " + pointWinningText);
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

        // Only show the continue button if there are NO choices left
        continueButton.SetActive(currentChoices.Count == 0);

        for (int i = 0; i < choices.Length; i++)
        {
            if (i < currentChoices.Count)
            {
                // THIS LINE IS KEY: It brings the buttons back for Round 2 and 3
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
        // 1. Check if the text matches the randomized winner
        if (currentStory.currentChoices[choiceIndex].text == pointWinningText)
        {
            npcPoints++;
        }

        currentStory.ChooseChoiceIndex(choiceIndex);
        dialogueRounds++;

        // 2. Hide buttons immediately so they can be refreshed for the next round
        foreach (GameObject choiceButton in choices)
        {
            choiceButton.SetActive(false);
        }

        // 3. If we haven't finished 3 rounds, go back to ContinueStory
        if (dialogueRounds < 3)
        {
            // This picks a NEW winning text and brings the 4 buttons back
            ContinueStory();
        }
        else
        {
            // 4. Show final text and evaluate recruitment
            if (currentStory.canContinue)
            {
                dialogueText.text = currentStory.Continue();
            }

            // Only show the bottom Continue button now
            continueButton.SetActive(true);
            CheckNPCFollowStatus();
        }
    }

    private void ExitDialogueMode()
    {
        dialogueIsPlaying = false;
        dialoguePanel.SetActive(false);

        // If recruitment failed, reset everything for the next E press
        if (npcPoints < 3)
        {
            npcPoints = 0;
            dialogueRounds = 0;
            currentStory = null; // Forces story to start from line 1 next time
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void CheckNPCFollowStatus()
    {
        if (npcPoints >= 3)
        {
            Debug.Log("Recruitment Successful!");

            if (audioSource != null && successSound != null)
            {
                audioSource.PlayOneShot(successSound);
            }

            if (currentNPC != null)
            {
                currentNPC.StartFollowing();
            }
        }
        else
        {
            Debug.Log("Recruitment Failed. Points: " + npcPoints);

            // Play the failure sound if they didn't get enough points
            if (audioSource != null && failureSound != null)
            {
                audioSource.PlayOneShot(failureSound);
            }
        }
    }
}
