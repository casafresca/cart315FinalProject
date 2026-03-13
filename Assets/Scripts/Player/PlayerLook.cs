using UnityEngine;

public class PlayerLook : MonoBehaviour
{

    public Camera cam;
    private float xRotation = 0f;

    public float xSensitivity = 30f;
    public float ySensitivity = 30f;

    public void ProcessLook(Vector2 lookInput)
    {
        // If dialogue is playing, don't rotate the camera
        if (DialogueManager.GetInstance().dialogueIsPlaying) return;

        float mouseX = lookInput.x * xSensitivity * Time.deltaTime;
        float mouseY = lookInput.y * ySensitivity * Time.deltaTime;
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);
        cam.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        transform.Rotate(Vector3.up * mouseX);
    }
}
