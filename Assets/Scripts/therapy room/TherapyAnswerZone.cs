using UnityEngine;

public class TherapyAnswerZone : MonoBehaviour
{
    [SerializeField] private string answerId = "A";
    [SerializeField] private TherapyRoomController roomController;

    public string AnswerId => answerId;

    private void Reset()
    {
        roomController = FindFirstObjectByType<TherapyRoomController>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player") || roomController == null)
        {
            return;
        }

        roomController.EnterAnswerZone(this);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player") || roomController == null)
        {
            return;
        }

        roomController.ExitAnswerZone(this);
    }
}
