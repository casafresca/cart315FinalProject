using UnityEngine;

public class NPCDialogue : MonoBehaviour
{
    public void TriggerDialogue()
    {
        if (TTSRunner.Instance == null)
        {
            Debug.LogError("TTSRunner instance not found in scene!");
            return;
        }

        TTSRunner.Instance.Speak("Welcome to my shop...");
    }
}