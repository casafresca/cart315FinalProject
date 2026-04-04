using UnityEngine;
using UnityEngine.EventSystems;

public class MouseLock : MonoBehaviour
{
    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        DialogueManager dm = DialogueManager.GetInstance();
        bool dialogueOpen = dm != null && dm.dialogueIsPlaying;

        // While dialogue UI is open, keep cursor unlocked for button clicks.
        if (dialogueOpen)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        // Press Escape to unlock (for testing)
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // Click to re-lock, but don't steal clicks from UI.
        if (Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
