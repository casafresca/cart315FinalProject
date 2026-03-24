using UnityEngine;
using TMPro;
using System.Collections;
using System;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

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
    [SerializeField] private int maxCharactersPerPage = 180;

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
    private string[] currentPages = Array.Empty<string>();
    private int currentPageIndex;

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
        StartPagedText(lines[index]);
    }

    private void AdvanceDialogue()
    {
        if (dialogueState == DialogueState.IntroLines)
        {
            if (!IsCurrentPageFullyShown())
            {
                FinishTypingCurrentText(GetCurrentPageText());
            }
            else if (HasMorePages())
            {
                ShowNextPage();
            }
            else
            {
                NextLine();
            }
        }
        else if (dialogueState == DialogueState.FollowUpText)
        {
            if (!IsCurrentPageFullyShown())
            {
                FinishTypingCurrentText(GetCurrentPageText());
            }
            else if (HasMorePages())
            {
                ShowNextPage();
            }
            else
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
        }
        else if (dialogueState == DialogueState.QuestionTyping)
        {
            if (!IsCurrentPageFullyShown())
            {
                FinishTypingCurrentText(GetCurrentPageText());
            }
            else if (HasMorePages())
            {
                ShowNextPage();
            }
            else
            {
                ShowOptionsForCurrentQuestion();
            }
        }
    }

    private IEnumerator TypeCurrentPage()
    {
        string pageText = GetCurrentPageText();
        Debug.Log("DialogueBox_TR: typing page " + (currentPageIndex + 1) + "/" + currentPages.Length);

        foreach (char c in pageText.ToCharArray())
        {
            textComponent.text += c;
            yield return new WaitForSeconds(textSpeed);
        }

        typingCoroutine = null;
        Debug.Log("DialogueBox_TR: finished typing page.");
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
        StartPagedText(question.questionText);
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
            StartPagedText(question.correctFollowUpText);
        }
        else
        {
            StartPagedText(question.wrongAnswerText);
            dialogueState = DialogueState.QuestionTyping;
            Debug.Log("DialogueBox_TR: wrong option selected. Waiting for another choice.");
        }
    }

    private void StartPagedText(string content)
    {
        currentPages = SplitIntoPages(content);
        currentPageIndex = 0;
        StartTypingCurrentPage();
    }

    private void StartTypingCurrentPage()
    {
        textComponent.text = string.Empty;

        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
        }

        typingCoroutine = StartCoroutine(TypeCurrentPage());
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

    private bool IsCurrentPageFullyShown()
    {
        return textComponent.text == GetCurrentPageText();
    }

    private bool HasMorePages()
    {
        return currentPageIndex < currentPages.Length - 1;
    }

    private void ShowNextPage()
    {
        currentPageIndex++;
        StartTypingCurrentPage();
    }

    private string GetCurrentPageText()
    {
        if (currentPages == null || currentPages.Length == 0)
        {
            return string.Empty;
        }

        return currentPages[Mathf.Clamp(currentPageIndex, 0, currentPages.Length - 1)];
    }

    private string[] SplitIntoPages(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new[] { string.Empty };
        }

        if (textComponent == null)
        {
            return SplitIntoPagesByCharacterCount(content);
        }

        string normalizedContent = content.Replace("\r\n", "\n");
        List<string> tokens = TokenizeForPaging(normalizedContent);
        List<string> pages = new List<string>();
        string currentPage = string.Empty;

        foreach (string token in tokens)
        {
            string candidate = currentPage + token;

            if (!string.IsNullOrEmpty(currentPage) && WouldOverflowTextBox(candidate))
            {
                pages.Add(currentPage);
                currentPage = token.TrimStart();
            }
            else
            {
                currentPage = candidate;
            }
        }

        if (!string.IsNullOrEmpty(currentPage))
        {
            pages.Add(currentPage);
        }

        if (pages.Count == 0)
        {
            pages.Add(normalizedContent);
        }

        Debug.Log("DialogueBox_TR: split text into " + pages.Count + " page(s).");
        return pages.ToArray();
    }

    private bool WouldOverflowTextBox(string candidateText)
    {
        if (maxCharactersPerPage > 0 && candidateText.Length > maxCharactersPerPage * 2)
        {
            return true;
        }

        textComponent.text = candidateText;
        textComponent.ForceMeshUpdate();
        TMP_TextInfo textInfo = textComponent.textInfo;

        if (textInfo == null)
        {
            return maxCharactersPerPage > 0 && candidateText.Length > maxCharactersPerPage;
        }

        int visibleLineCount = textInfo.lineCount;
        float availableHeight = textComponent.rectTransform.rect.height;
        float preferredHeight = textComponent.preferredHeight;

        bool heightOverflow = preferredHeight > availableHeight + 0.5f;
        bool lineOverflow = visibleLineCount > 2 && availableHeight <= preferredHeight;

        return heightOverflow || lineOverflow;
    }

    private List<string> TokenizeForPaging(string content)
    {
        List<string> tokens = new List<string>();
        int i = 0;

        while (i < content.Length)
        {
            if (content[i] == '\n')
            {
                int newlineCount = 0;
                while (i < content.Length && content[i] == '\n')
                {
                    newlineCount++;
                    i++;
                }

                tokens.Add(new string('\n', newlineCount));
                continue;
            }

            int start = i;
            while (i < content.Length && content[i] != ' ' && content[i] != '\n')
            {
                i++;
            }

            if (start != i)
            {
                tokens.Add(content.Substring(start, i - start));
            }

            while (i < content.Length && content[i] == ' ')
            {
                tokens.Add(" ");
                i++;
            }
        }

        return tokens;
    }

    private string[] SplitIntoPagesByCharacterCount(string content)
    {
        if (maxCharactersPerPage <= 0 || content.Length <= maxCharactersPerPage)
        {
            return new[] { content };
        }

        List<string> pages = new List<string>();
        string[] words = content.Split(' ');
        string currentPage = string.Empty;

        foreach (string word in words)
        {
            string candidate = string.IsNullOrEmpty(currentPage) ? word : currentPage + " " + word;

            if (candidate.Length > maxCharactersPerPage && !string.IsNullOrEmpty(currentPage))
            {
                pages.Add(currentPage);
                currentPage = word;
            }
            else
            {
                currentPage = candidate;
            }
        }

        if (!string.IsNullOrEmpty(currentPage))
        {
            pages.Add(currentPage);
        }

        return pages.ToArray();
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
