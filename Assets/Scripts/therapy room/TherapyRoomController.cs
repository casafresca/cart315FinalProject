using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class TherapyRoomController : MonoBehaviour
{
    [Header("Question UI")]
    [SerializeField] private GameObject questionPanel;
    [SerializeField] private TextMeshProUGUI questionText;
    [SerializeField] private TextMeshProUGUI timerText;
    [TextArea(3, 8)]
    [SerializeField] private string promptQuestion = "";
    [SerializeField] private DialogueBox_TR introDialogue;
    [SerializeField] private TherapyJournalUI journalUI;

    [Header("Journal Guide UI")]
    [SerializeField] private GameObject journalGuidePanel;
    [SerializeField] private TextMeshProUGUI journalGuideText;
    [TextArea(3, 8)]
    [SerializeField] private string journalGuideMessage = "Journal Guide:\nRead each day entry and answer the questions. Use the buttons to switch days. You can only move after finishing the journal section.";

    [Header("Player Lock")]
    [SerializeField] private GameObject playerRootForLock;

    [Header("Answer Setup")]
    [SerializeField] private string correctAnswerId = "A";
    [SerializeField] private float answerHoldDuration = 3f;
    [SerializeField] private string returnSceneName = "SampleScene";

    private TherapyAnswerZone activeZone;
    private float currentTimeRemaining;
    private bool challengeStarted;
    private bool answerLocked;
    private Action journalGuideContinue;
    private bool movementLocked;
    private ScriptLockState[] lockedScripts = Array.Empty<ScriptLockState>();

    private struct ScriptLockState
    {
        public MonoBehaviour script;
        public bool wasEnabled;
    }

    private void Start()
    {
        TherapySessionState.ClearOutcome();

        DebugLogAssignments();

        if (questionPanel != null)
        {
            questionPanel.SetActive(false);
        }

        if (questionText != null)
        {
            questionText.text = promptQuestion;
        }

        if (journalUI != null)
        {
            journalUI.HideJournal();
        }

        if (journalGuidePanel != null)
        {
            journalGuidePanel.SetActive(false);
        }

        if (journalGuideText != null)
        {
            journalGuideText.text = journalGuideMessage;
        }

        HideTimer();

        Debug.Log("Therapy room correct answer is: " + correctAnswerId);
        bool hasIntroDialogue = introDialogue != null && introDialogue.gameObject.activeInHierarchy;
        Cursor.lockState = hasIntroDialogue ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = hasIntroDialogue;
    }

    private void Update()
    {
        if (!challengeStarted || answerLocked || activeZone == null)
        {
            return;
        }

        currentTimeRemaining -= Time.deltaTime;

        if (timerText != null)
        {
            timerText.text = "Timer: " + Mathf.CeilToInt(Mathf.Max(currentTimeRemaining, 0f));
        }

        if (currentTimeRemaining <= 0f)
        {
            LockInAnswer();
        }
    }

    public void OnNextPressed()
    {
        challengeStarted = true;

        if (questionPanel != null)
        {
            questionPanel.SetActive(false);
        }

        if (journalUI != null)
        {
            journalUI.HideJournal();
        }

        if (journalGuidePanel != null)
        {
            journalGuidePanel.SetActive(false);
        }

        SetPlayerMovementLocked(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void ShowQuestionAndJournal()
    {
        Debug.Log("TherapyRoomController: ShowQuestionAndJournal() called.");

        if (questionPanel != null)
        {
            Debug.Log("TherapyRoomController: showing question panel -> " + questionPanel.name);
            questionPanel.SetActive(true);
        }
        else
        {
            Debug.LogError("TherapyRoomController: questionPanel is not assigned.");
        }

        if (questionText != null)
        {
            questionText.text = promptQuestion;
        }
        else
        {
            Debug.LogError("TherapyRoomController: questionText is not assigned.");
        }

        if (journalUI != null)
        {
            Debug.Log("TherapyRoomController: opening journal UI -> " + journalUI.name);
            journalUI.OpenJournal();
        }
        else
        {
            Debug.LogError("TherapyRoomController: journalUI is not assigned.");
        }

        SetPlayerMovementLocked(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void ShowQuestionOnly()
    {
        Debug.Log("TherapyRoomController: ShowQuestionOnly() called.");

        if (questionPanel != null)
        {
            Debug.Log("TherapyRoomController: showing question panel only -> " + questionPanel.name);
            questionPanel.SetActive(true);
        }
        else
        {
            Debug.LogError("TherapyRoomController: questionPanel is not assigned.");
        }

        if (questionText != null)
        {
            questionText.text = promptQuestion;
        }
        else
        {
            Debug.LogError("TherapyRoomController: questionText is not assigned.");
        }

        if (journalUI != null)
        {
            journalUI.HideJournal();
        }

        SetPlayerMovementLocked(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void OpenJournalOnly()
    {
        if (journalUI != null)
        {
            Debug.Log("TherapyRoomController: opening journal only -> " + journalUI.name);
            journalUI.OpenJournal();
        }
        else
        {
            Debug.LogError("TherapyRoomController: journalUI is not assigned, so journal cannot open.");
        }

        SetPlayerMovementLocked(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void CloseJournalOnly()
    {
        if (journalUI != null)
        {
            Debug.Log("TherapyRoomController: closing journal only -> " + journalUI.name);
            journalUI.HideJournal();
        }
        else
        {
            Debug.LogWarning("TherapyRoomController: journalUI is not assigned, so there is no journal to close.");
        }
    }

    public void OpenJournalGuide(Action onContinue = null)
    {
        journalGuideContinue = onContinue;

        if (journalGuidePanel != null)
        {
            journalGuidePanel.SetActive(true);
        }

        if (journalGuideText != null)
        {
            journalGuideText.text = journalGuideMessage;
        }

        SetPlayerMovementLocked(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void ToggleJournalGuide()
    {
        if (journalGuidePanel == null)
        {
            Debug.LogWarning("TherapyRoomController: journalGuidePanel is not assigned.");
            return;
        }

        bool willOpen = !journalGuidePanel.activeSelf;

        if (willOpen)
        {
            OpenJournalGuide();
        }
        else
        {
            CloseJournalGuide();
        }
    }

    public void OnJournalGuideContinuePressed()
    {
        if (journalGuidePanel != null)
        {
            journalGuidePanel.SetActive(false);
        }

        Action continueAction = journalGuideContinue;
        journalGuideContinue = null;
        continueAction?.Invoke();
    }

    public void CloseJournalGuide()
    {
        if (journalGuidePanel != null)
        {
            journalGuidePanel.SetActive(false);
        }

        journalGuideContinue = null;
    }

    public void OnClosePressed()
    {
        if (questionPanel != null)
        {
            questionPanel.SetActive(false);
        }

        if (journalUI != null)
        {
            journalUI.HideJournal();
        }

        HideTimer();
    }

    public void EnterAnswerZone(TherapyAnswerZone zone)
    {
        if (!challengeStarted || answerLocked || zone == null)
        {
            return;
        }

        activeZone = zone;
        currentTimeRemaining = answerHoldDuration;

        if (timerText != null)
        {
            timerText.gameObject.SetActive(true);
            timerText.text = "Timer: " + Mathf.CeilToInt(answerHoldDuration);
        }
    }

    public void ExitAnswerZone(TherapyAnswerZone zone)
    {
        if (answerLocked || zone == null || activeZone != zone)
        {
            return;
        }

        activeZone = null;
        HideTimer();
    }

    private void LockInAnswer()
    {
        answerLocked = true;

        string selectedAnswer = activeZone != null ? activeZone.AnswerId : string.Empty;
        bool answerWasCorrect = string.Equals(selectedAnswer, correctAnswerId, System.StringComparison.OrdinalIgnoreCase);

        TherapySessionState.SetOutcome(answerWasCorrect, selectedAnswer);

        Debug.Log("Therapy answer locked: " + selectedAnswer);
        Debug.Log(answerWasCorrect
            ? "Therapy outcome: NPC survived."
            : "Therapy outcome: NPC died.");

        HideTimer();
        SceneManager.LoadScene(returnSceneName);
    }

    private void HideTimer()
    {
        if (timerText == null)
        {
            Debug.LogWarning("TherapyRoomController: timerText is not assigned.");
            return;
        }

        timerText.text = "Timer: " + Mathf.CeilToInt(answerHoldDuration);
        timerText.gameObject.SetActive(false);
    }

    private void DebugLogAssignments()
    {
        Debug.Log("TherapyRoomController setup:"
            + "\n- questionPanel: " + (questionPanel != null ? questionPanel.name : "MISSING")
            + "\n- questionText: " + (questionText != null ? questionText.name : "MISSING")
            + "\n- timerText: " + (timerText != null ? timerText.name : "MISSING")
            + "\n- introDialogue: " + (introDialogue != null ? introDialogue.name : "MISSING")
            + "\n- journalUI: " + (journalUI != null ? journalUI.name : "MISSING")
            + "\n- playerRootForLock: " + (playerRootForLock != null ? playerRootForLock.name : "MISSING")
            + "\n- journalGuidePanel: " + (journalGuidePanel != null ? journalGuidePanel.name : "MISSING"));
    }

    public void LockPlayerMovement()
    {
        SetPlayerMovementLocked(true);
    }

    public void UnlockPlayerMovement()
    {
        SetPlayerMovementLocked(false);
    }

    private void SetPlayerMovementLocked(bool locked)
    {
        if (movementLocked == locked)
        {
            return;
        }

        movementLocked = locked;

        if (locked)
        {
            MonoBehaviour[] scripts = ResolvePlayerScriptsToDisable();
            lockedScripts = new ScriptLockState[scripts.Length];
            for (int i = 0; i < scripts.Length; i++)
            {
                MonoBehaviour script = scripts[i];
                lockedScripts[i] = new ScriptLockState
                {
                    script = script,
                    wasEnabled = script != null && script.enabled
                };

                if (script != null)
                {
                    script.enabled = false;
                }
            }
        }
        else
        {
            foreach (ScriptLockState state in lockedScripts)
            {
                if (state.script != null)
                {
                    state.script.enabled = state.wasEnabled;
                }
            }

            lockedScripts = Array.Empty<ScriptLockState>();
        }
    }

    private MonoBehaviour[] ResolvePlayerScriptsToDisable()
    {
        GameObject player = playerRootForLock != null
            ? playerRootForLock
            : GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            Debug.LogWarning("TherapyRoomController: Player not found. No movement scripts will be locked.");
            return Array.Empty<MonoBehaviour>();
        }

        List<MonoBehaviour> scripts = new List<MonoBehaviour>();
        AddIfPresent(player.GetComponent<InputManager>(), scripts);
        AddIfPresent(player.GetComponent<PlayerLook>(), scripts);
        AddIfPresent(player.GetComponent<PlayerMovementSean>(), scripts);
        AddIfPresent(player.GetComponent<PlayerMotor>(), scripts);
        AddIfPresent(player.GetComponent<PlayerInteract>(), scripts);

        return scripts.ToArray();
    }

    private void AddIfPresent(MonoBehaviour script, List<MonoBehaviour> list)
    {
        if (script != null && !list.Contains(script))
        {
            list.Add(script);
        }
    }
}
