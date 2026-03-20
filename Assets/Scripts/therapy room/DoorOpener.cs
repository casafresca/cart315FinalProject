using UnityEngine;


public class DoorOpener : MonoBehaviour
{
    [Header("Optional Animator")]
    [SerializeField] private Animator doorAnimator;
    [SerializeField] private string openTriggerName = "Door_Open";
    [SerializeField] private string closeTriggerName = "Door_Close";

    [Header("Code Rotation Fallback")]
    [SerializeField] private bool useCodeRotation = true;
    [SerializeField] private float openAngle = 270f;
    [SerializeField] private float openSpeed = 2f;

    private Quaternion closedRotation;
    private Quaternion openRotation;
    private Quaternion targetRotation;
    private bool isMoving;

    void Start()
    {
        if (doorAnimator == null)
        {
            doorAnimator = GetComponent<Animator>();
        }

        closedRotation = transform.rotation;
        openRotation = closedRotation * Quaternion.Euler(0f, openAngle, 0f);
        targetRotation = closedRotation;
    }

    void Update()
    {
        if (!isMoving || !useCodeRotation)
        {
            return;
        }

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            openSpeed * Time.deltaTime
        );

        if (Quaternion.Angle(transform.rotation, targetRotation) < 0.5f)
        {
            transform.rotation = targetRotation;
            isMoving = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player"))
        {
            return;
        }

        OpenDoor();
    }

    public void OpenDoor()
    {
        if (doorAnimator != null && !string.IsNullOrWhiteSpace(openTriggerName))
        {
            doorAnimator.SetTrigger(openTriggerName);
            return;
        }

        targetRotation = openRotation;
        isMoving = true;
    }

    public void CloseDoor()
    {
        if (doorAnimator != null && !string.IsNullOrWhiteSpace(closeTriggerName))
        {
            doorAnimator.SetTrigger(closeTriggerName);
            return;
        }

        targetRotation = closedRotation;
        isMoving = true;
    }
}
