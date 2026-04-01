using TMPro;
using UnityEngine;

public class PhotoCaptureTrigger : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PhotoCapture photoCapture;
    [SerializeField] private GameObject promptRoot;
    [SerializeField] private TextMeshProUGUI promptText;

    [Header("Controls")]
    [SerializeField] private KeyCode pickupKey = KeyCode.C;
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private string pickupPrompt = "Press C to pick up the camera.\nLeft click to take photo. Right click to close preview.";

    private bool playerInRange;
    private bool cameraPickedUp;

    private void Start()
    {
        SetPromptVisible(false);
    }

    private void Reset()
    {
        if (photoCapture == null)
        {
            photoCapture = FindObjectOfType<PhotoCapture>();
        }
    }

    private void Update()
    {
        if (!playerInRange || cameraPickedUp || photoCapture == null)
        {
            return;
        }

        if (Input.GetKeyDown(pickupKey))
        {
            cameraPickedUp = true;
            SetPromptVisible(false);
            Debug.Log("[PhotoCaptureTrigger] C key pressed near camera object. Unlocking camera.");
            photoCapture.UnlockCamera();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag) || cameraPickedUp)
        {
            return;
        }

        playerInRange = true;
        SetPromptVisible(true);
        Debug.Log("[PhotoCaptureTrigger] Player entered the camera trigger.");
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag) || cameraPickedUp)
        {
            return;
        }

        playerInRange = false;
        SetPromptVisible(false);
        Debug.Log("[PhotoCaptureTrigger] Player left the camera trigger.");
    }

    private void SetPromptVisible(bool isVisible)
    {
        if (promptRoot != null)
        {
            promptRoot.SetActive(isVisible);
        }

        if (promptText != null)
        {
            promptText.text = isVisible ? pickupPrompt : string.Empty;
        }
    }
}
