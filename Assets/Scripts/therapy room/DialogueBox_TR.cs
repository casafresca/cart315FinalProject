using UnityEngine;
using TMPro;
using System.Collections;

public class DialogueBox_TR : MonoBehaviour
{
    [Header("Dialogue UI")]
    [SerializeField] private TextMeshProUGUI textComponent;
    [SerializeField] private GameObject dialoguePanelToHide;

    [Header("Dialogue Content")]
    [SerializeField] private string[] lines;
    [SerializeField] private float textSpeed = 0.03f;
    [SerializeField] private KeyCode nextKey = KeyCode.N;

    [Header("Flow")]
    [SerializeField] private TherapyRoomController therapyRoomController;

    private int index;
    private Coroutine typingCoroutine;

    private void Start()
    {
        Debug.Log("DialogueBox_TR Start called");
        if (textComponent == null)
        {
            Debug.LogError("TextMeshProUGUI component not assigned!");
            return;
        }
        if (lines == null || lines.Length == 0)
        {
            Debug.LogError("No dialogue lines assigned!");
            return;
        }

        if (dialoguePanelToHide == null)
        {
            Debug.LogWarning("DialogueBox_TR: dialoguePanelToHide is not assigned. The script will disable its own GameObject at the end, which may hide too much UI.");
        }

        if (therapyRoomController == null)
        {
            Debug.LogError("DialogueBox_TR: therapyRoomController is not assigned. Question and journal UI cannot open.");
        }

        textComponent.text = string.Empty;
        StartDialogue();
    }

    private void Update()
    {
        if (Input.GetKeyDown(nextKey))
        {
            AdvanceDialogue();
        }
    }

    private void StartDialogue()
    {
        Debug.Log("Starting dialogue with " + lines.Length + " lines");
        index = 0;
        StartTypingCurrentLine();
    }

    private void StartTypingCurrentLine()
    {
        textComponent.text = string.Empty;

        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
        }

        typingCoroutine = StartCoroutine(TypeLine());
    }

    private void AdvanceDialogue()
    {
        if (textComponent.text == lines[index])
        {
            NextLine();
        }
        else
        {
            if (typingCoroutine != null)
            {
                StopCoroutine(typingCoroutine);
                typingCoroutine = null;
            }

            textComponent.text = lines[index];
        }
    }

    private IEnumerator TypeLine()
    {
        Debug.Log("Typing line: " + lines[index]);
        foreach (char c in lines[index].ToCharArray())
        {
            textComponent.text += c;
            yield return new WaitForSeconds(textSpeed);
        }

        typingCoroutine = null;
        Debug.Log("Finished typing line");
    }

    private void NextLine()
    {
        if (index < lines.Length - 1)
        {
            index++;
            StartTypingCurrentLine();
        }
        else
        {
            Debug.Log("DialogueBox_TR: final line completed. Attempting to open question and journal UI.");

            if (therapyRoomController != null)
            {
                therapyRoomController.ShowQuestionAndJournal();
            }
            else
            {
                Debug.LogError("DialogueBox_TR: therapyRoomController is missing, so ShowQuestionAndJournal() was not called.");
            }

            HideDialoguePanel();
        }
    }

    private void HideDialoguePanel()
    {
        if (dialoguePanelToHide != null)
        {
            Debug.Log("DialogueBox_TR: hiding assigned dialogue panel only -> " + dialoguePanelToHide.name);
            dialoguePanelToHide.SetActive(false);
            return;
        }

        Debug.LogWarning("DialogueBox_TR: no dialoguePanelToHide assigned. Disabling this GameObject -> " + gameObject.name);
        gameObject.SetActive(false);
    }
}
