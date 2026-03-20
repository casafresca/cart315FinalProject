using TMPro;
using UnityEngine;

public class TherapyOutcomePanel : MonoBehaviour
{
    [SerializeField] private GameObject outcomePanel;
    [SerializeField] private TextMeshProUGUI outcomeText;

    private void Start()
    {
        if (outcomePanel != null)
        {
            outcomePanel.SetActive(false);
        }

        if (!TherapySessionState.HasOutcome)
        {
            return;
        }

        if (outcomePanel != null)
        {
            outcomePanel.SetActive(true);
        }

        if (outcomeText != null)
        {
            outcomeText.text = TherapySessionState.OutcomeMessage;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void CloseOutcomePanel()
    {
        if (outcomePanel != null)
        {
            outcomePanel.SetActive(false);
        }

        TherapySessionState.ClearOutcome();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
