using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class TherapyRoomController : MonoBehaviour
{
    [Header("Question UI")]
    [SerializeField] private GameObject questionPanel;
    [SerializeField] private TextMeshProUGUI questionText;
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private string promptQuestion = "Choose the correct therapy answer by standing in zone A, B, C, or D.";

    [Header("Answer Setup")]
    [SerializeField] private string correctAnswerId = "A";
    [SerializeField] private float answerHoldDuration = 3f;
    [SerializeField] private string returnSceneName = "SampleScene";

    private TherapyAnswerZone activeZone;
    private float currentTimeRemaining;
    private bool challengeStarted;
    private bool answerLocked;

    private void Start()
    {
        TherapySessionState.ClearOutcome();

        if (questionPanel != null)
        {
            questionPanel.SetActive(true);
        }

        if (questionText != null)
        {
            questionText.text = promptQuestion;
        }

        HideTimer();

        Debug.Log("Therapy room correct answer is: " + correctAnswerId);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
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

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void OnClosePressed()
    {
        if (questionPanel != null)
        {
            questionPanel.SetActive(false);
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
            return;
        }

        timerText.text = "Timer: " + Mathf.CeilToInt(answerHoldDuration);
        timerText.gameObject.SetActive(false);
    }
}
