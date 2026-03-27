using UnityEngine;

public class NPCInteract : MonoBehaviour
{
    public float interactDistance = 3f;
    public KeyCode interactKey = KeyCode.E;

    Transform player;

    void Start()
    {
        player = Camera.main.transform; // assuming FPS
    }

    void Update()
    {
        if (Vector3.Distance(player.position, transform.position) <= interactDistance)
        {
            if (Input.GetKeyDown(interactKey))
            {
                Interact();
            }
        }
    }

    void Interact()
    {
        GetComponent<NPCDialogue>()?.TriggerDialogue();
    }
}