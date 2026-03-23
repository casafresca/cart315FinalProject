using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TherapyJournalUI : MonoBehaviour
{
    [Serializable]
    public class JournalEntry
    {
        public string dayLabel = "Day 1";
        [TextArea(4, 12)]
        public string content;
    }

    [Header("UI")]
    [SerializeField] private GameObject journalPanel;
    [SerializeField] private TextMeshProUGUI journalTitleText;
    [SerializeField] private TextMeshProUGUI journalContentText;
    [SerializeField] private Button[] dayButtons;
    [SerializeField] private string journalTitle = "Journal";

    [Header("Entries")]
    [SerializeField] private JournalEntry[] entries;

    private void Start()
    {
        DebugLogAssignments();
        ConfigureButtons();
        RefreshView(0);
    }

    public void OpenJournal()
    {
        if (journalPanel != null)
        {
            Debug.Log("TherapyJournalUI: showing journal panel -> " + journalPanel.name);
            journalPanel.SetActive(true);
        }
        else
        {
            Debug.LogError("TherapyJournalUI: journalPanel is not assigned.");
        }

        RefreshView(0);
    }

    public void HideJournal()
    {
        if (journalPanel != null)
        {
            journalPanel.SetActive(false);
        }
    }

    public void ShowEntry(int index)
    {
        RefreshView(index);
    }

    private void ConfigureButtons()
    {
        if (dayButtons == null)
        {
            return;
        }

        for (int i = 0; i < dayButtons.Length; i++)
        {
            Button button = dayButtons[i];
            if (button == null)
            {
                continue;
            }

            int buttonIndex = i;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => ShowEntry(buttonIndex));

            TextMeshProUGUI buttonText = button.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null && entries != null && buttonIndex < entries.Length)
            {
                buttonText.text = entries[buttonIndex].dayLabel;
            }
        }
    }

    private void RefreshView(int index)
    {
        if (journalTitleText != null)
        {
            journalTitleText.text = journalTitle;
        }

        if (journalContentText == null)
        {
            Debug.LogError("TherapyJournalUI: journalContentText is not assigned.");
            return;
        }

        if (entries == null || entries.Length == 0)
        {
            journalContentText.text = string.Empty;
            return;
        }

        int safeIndex = Mathf.Clamp(index, 0, entries.Length - 1);
        JournalEntry entry = entries[safeIndex];
        journalContentText.text = entry != null ? entry.content : string.Empty;
        Debug.Log("TherapyJournalUI: showing journal entry index " + safeIndex);
    }

    private void DebugLogAssignments()
    {
        Debug.Log("TherapyJournalUI setup:"
            + "\n- journalPanel: " + (journalPanel != null ? journalPanel.name : "MISSING")
            + "\n- journalTitleText: " + (journalTitleText != null ? journalTitleText.name : "MISSING")
            + "\n- journalContentText: " + (journalContentText != null ? journalContentText.name : "MISSING")
            + "\n- dayButtons count: " + (dayButtons != null ? dayButtons.Length : 0)
            + "\n- entries count: " + (entries != null ? entries.Length : 0));
    }
}
