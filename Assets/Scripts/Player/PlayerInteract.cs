using UnityEngine;

public class PlayerInteract : MonoBehaviour
{

    private Camera cam;
    
    [SerializeField]
    private float interactRange = 3f;

    [SerializeField]
    private LayerMask mask;
    private PlayerUI playerUI;
    private InputManager inputManager;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        cam = GetComponent<PlayerLook>().cam;
        playerUI = GetComponent<PlayerUI>();
        inputManager = GetComponent<InputManager>();
    }

    // Update is called once per frame
    void Update()
    {
        // 1. If dialogue is playing, clear the UI and stop looking for interactables
        if (DialogueManager.GetInstance().dialogueIsPlaying)
        {
            playerUI.UpdateText(string.Empty);
            return;
        }

        playerUI.UpdateText(string.Empty);

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        Debug.DrawRay(ray.origin, ray.direction * interactRange);
        RaycastHit hitInfo;

        if (Physics.Raycast(ray, out hitInfo, interactRange, mask))
        {
            // 2. Try to get the Interactable component
            if (hitInfo.collider.TryGetComponent(out Interactable interactable))
            {
                // 3. If this is an NPC and they are already following, DO NOT show text
                if (interactable is NPC npc && npc.isFollowing)
                {
                    return; // Skip showing the prompt message
                }

                // 4. Otherwise, show the prompt and allow interaction
                playerUI.UpdateText(interactable.promptMessage);
                if (inputManager.onFoot.Interact.triggered)
                {
                    interactable.BasseInteract();
                }
            }
        }
    }
}
