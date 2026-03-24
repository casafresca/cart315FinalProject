using UnityEngine;
using TMPro;
using System.Collections;
using System;
using UnityEngine.UI;

public class DialogueBox_TR : MonoBehaviour
{
    [Serializable]
    public class DialogueQuestion
    {
        [TextArea(2, 6)]
        public string questionText;
        public string[] options = new string[4];
        [Range(0, 3)] public int correctOptionIndex;
        [TextArea(2, 6)]
        public string correctFollowUpText;
        [TextArea(2, 6)]
        public string wrongAnswerText = "That is not the right answer. Try again.";
    }

    private enum DialogueState
    {
        IntroLines,
        QuestionTyping,
        QuestionChoice,
        FollowUpText,
        Completed
    }

    [Header("Dialogue UI")]
    [SerializeField] private TextMeshProUGUI textComponent;
    [SerializeField] private GameObject dialoguePanelToHide;

    [Header("Dialogue Content")]
    [SerializeField] private string[] lines;
    [SerializeField] private float textSpeed = 0.03f;
    [SerializeField] private KeyCode nextKey = KeyCode.N;

    [Header("Flow")]
    [SerializeField] private TherapyRoomController therapyRoomController;

    [Header("Questions")]
    [SerializeField] private GameObject questionOptionsPanel;
    [SerializeField] private Button[] optionButtons;
    [SerializeField] private DialogueQuestion[] questions;

    private int index;
    private int currentQuestionIndex;
    private Coroutine typingCoroutine;
    private DialogueState dialogueState = DialogueState.IntroLines;

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

        ConfigureQuestionButtons();
        HideQuestionOptions();

        textComponent.text = string.Empty;
        StartDialogue();
    }

    private void Update()
    {
        if (Input.GetKeyDown(nextKey) && dialogueState != DialogueState.QuestionChoice)
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
        if (dialogueState == DialogueState.IntroLines)
        {
            if (textComponent.text == lines[index])
            {
                NextLine();
            }
            else
            {
                FinishTypingCurrentText(lines[index]);
            }
        }
        else if (dialogueState == DialogueState.FollowUpText)
        {
            DialogueQuestion currentQuestion = questions[currentQuestionIndex];
            string followUpText = currentQuestion.correctFollowUpText;

            if (textComponent.text == followUpText)
            {
                currentQuestionIndex++;

                if (currentQuestionIndex < questions.Length)
                {
                    ShowCurrentQuestion();
                }
                else
                {
                    FinishDialogueSequence();
                }
            }
            else
            {
                FinishTypingCurrentText(followUpText);
            }
        }
        else if (dialogueState == DialogueState.QuestionTyping)
        {
            DialogueQuestion currentQuestion = questions[currentQuestionIndex];
            FinishTypingCurrentText(currentQuestion.questionText);
            ShowOptionsForCurrentQuestion();
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
            if (questions != null && questions.Length > 0)
            {
                currentQuestionIndex = 0;
                ShowCurrentQuestion();
            }
            else
            {
                FinishDialogueSequence();
            }
        }
    }

    private void ConfigureQuestionButtons()
    {
        if (optionButtons == null || optionButtons.Length == 0)
        {
            Debug.LogWarning("DialogueBox_TR: no option buttons assigned for dialogue questions.");
            return;
        }

        for (int i = 0; i < optionButtons.Length; i++)
        {
            Button button = optionButtons[i];
            if (button == null)
            {
                continue;
            }

            int optionIndex = i;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => OnOptionSelected(optionIndex));
        }
    }

    private void ShowCurrentQuestion()
    {
        if (questions == null || currentQuestionIndex >= questions.Length)
        {
            FinishDialogueSequence();
            return;
        }

        dialogueState = DialogueState.QuestionTyping;

        DialogueQuestion question = questions[currentQuestionIndex];
        Debug.Log("DialogueBox_TR: showing question " + (currentQuestionIndex + 1));

        if (therapyRoomController != null)
        {
            therapyRoomController.OpenJournalOnly();
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        HideQuestionOptions();
        textComponent.text = string.Empty;

        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
        }

        typingCoroutine = StartCoroutine(TypeQuestionText(question.questionText));
    }

    private IEnumerator TypeQuestionText(string content)
    {
        foreach (char c in content.ToCharArray())
        {
            textComponent.text += c;
            yield return new WaitForSeconds(textSpeed);
        }

        typingCoroutine = null;
        ShowOptionsForCurrentQuestion();
    }

    private void ShowOptionsForCurrentQuestion()
    {
        if (questions == null || currentQuestionIndex >= questions.Length)
        {
            return;
        }

        dialogueState = DialogueState.QuestionChoice;

        DialogueQuestion question = questions[currentQuestionIndex];

        if (questionOptionsPanel != null)
        {
            questionOptionsPanel.SetActive(true);
        }
        else
        {
            Debug.LogError("DialogueBox_TR: questionOptionsPanel is not assigned.");
        }

        for (int i = 0; i < optionButtons.Length; i++)
        {
            Button button = optionButtons[i];
            if (button == null)
            {
                continue;
            }

            bool hasOption = question.options != null && i < question.options.Length;
            button.gameObject.SetActive(hasOption);
            button.interactable = hasOption;

            if (!hasOption)
            {
                continue;
            }

            TextMeshProUGUI buttonText = button.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.text = question.options[i];
            }
        }

        Debug.Log("DialogueBox_TR: question options are now active and clickable.");
    }

    private void OnOptionSelected(int optionIndex)
    {
        if (dialogueState != DialogueState.QuestionChoice || questions == null || currentQuestionIndex >= questions.Length)
        {
            return;
        }

        DialogueQuestion question = questions[currentQuestionIndex];
        bool isCorrect = optionIndex == question.correctOptionIndex;

        Debug.Log("DialogueBox_TR: selected option " + optionIndex + " for question " + (currentQuestionIndex + 1) + ". Correct: " + isCorrect);

        if (isCorrect)
        {
            HideQuestionOptions();
            dialogueState = DialogueState.FollowUpText;
            textComponent.text = string.Empty;

            if (typingCoroutine != null)
            {
                StopCoroutine(typingCoroutine);
            }

            typingCoroutine = StartCoroutine(TypeCustomText(question.correctFollowUpText));
        }
        else
        {
            textComponent.text = question.wrongAnswerText;
            Debug.Log("DialogueBox_TR: wrong option selected. Waiting for another choice.");
        }
    }

    private IEnumerator TypeCustomText(string content)
    {
        foreach (char c in content.ToCharArray())
        {
            textComponent.text += c;
            yield return new WaitForSeconds(textSpeed);
        }

        typingCoroutine = null;
        Debug.Log("DialogueBox_TR: follow-up text finished. Press " + nextKey + " to continue.");
    }

    private void FinishTypingCurrentText(string fullText)
    {
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }

        textComponent.text = fullText;
    }

    private void HideQuestionOptions()
    {
        if (questionOptionsPanel != null)
        {
            questionOptionsPanel.SetActive(false);
        }
    }

    private void FinishDialogueSequence()
    {
        dialogueState = DialogueState.Completed;
        HideQuestionOptions();

        if (therapyRoomController != null)
        {
            Debug.Log("DialogueBox_TR: final follow-up completed. Closing journal and opening final question panel.");
            therapyRoomController.CloseJournalOnly();
            therapyRoomController.ShowQuestionOnly();
        }
        else
        {
            Debug.LogError("DialogueBox_TR: therapyRoomController is missing, so ShowQuestionOnly() was not called.");
        }

        HideDialoguePanel();
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
