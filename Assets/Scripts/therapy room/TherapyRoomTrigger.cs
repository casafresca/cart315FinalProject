using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class TherapyRoomTrigger : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject therapyRoomPanel;
    [SerializeField] private TextMeshProUGUI instructionText;
    [SerializeField] private string panelMessage = "The patient and therapist are both in the room. Continue to the therapy room scene.";

    [Header("Scene")]
    [SerializeField] private string therapySceneName = "therapy room";

    [Header("Door")]
    [SerializeField] private DoorOpener doorOpener;

    private bool playerInRoom;
    private int npcCountInRoom;
    private bool panelShown;

    private void Start()
    {
        if (therapyRoomPanel != null)
        {
            therapyRoomPanel.SetActive(false);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRoom = true;
            RefreshPanel();
            return;
        }

        if (other.CompareTag("Target"))
        {
            npcCountInRoom++;
            RefreshPanel();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRoom = false;
            HidePanel();
            return;
        }

        if (other.CompareTag("Target"))
        {
            npcCountInRoom = Mathf.Max(0, npcCountInRoom - 1);

            if (npcCountInRoom == 0)
            {
                HidePanel();
            }
        }
    }

    private void RefreshPanel()
    {
        bool shouldShow = playerInRoom && npcCountInRoom > 0;

        if (!shouldShow)
        {
            HidePanel();
            return;
        }

        panelShown = true;

        if (therapyRoomPanel != null)
        {
            therapyRoomPanel.SetActive(true);
        }

        if (instructionText != null)
        {
            instructionText.text = panelMessage;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void HidePanel()
    {
        panelShown = false;

        if (therapyRoomPanel != null)
        {
            therapyRoomPanel.SetActive(false);
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void OnNextButtonPressed()
    {
        if (!panelShown)
        {
            return;
        }

        if (doorOpener != null)
        {
            doorOpener.CloseDoor();
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            TherapySessionState.SetReturnPoint(
                SceneManager.GetActiveScene().name,
                player.transform.position,
                player.transform.rotation);

            Debug.Log("Saved therapy return point at: " + player.transform.position);
        }
        else
        {
            Debug.LogWarning("TherapyRoomTrigger: Player was not found, so no return point was saved.");
        }

        Debug.Log("Loading therapy room scene..."+therapySceneName);
        SceneManager.LoadScene(therapySceneName);
    }
}
