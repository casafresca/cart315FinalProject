using UnityEngine;

public class PlayerInteract : MonoBehaviour
{
    private Camera cam;

    [SerializeField]
    private float interactRange = 3f;

    [SerializeField]
    private LayerMask mask;

    [Header("Interaction Key (Temporary Debug)")]
    [SerializeField] private KeyCode interactKey = KeyCode.F;

    private PlayerUI playerUI;
    private InputManager inputManager;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        PlayerLook playerLook = GetComponent<PlayerLook>();
        if (playerLook != null)
        {
            cam = playerLook.cam;
        }
        playerUI = GetComponent<PlayerUI>();
        inputManager = GetComponent<InputManager>();
    }

    // Update is called once per frame
    void Update()
    {
        if (cam == null)
        {
            PlayerLook playerLook = GetComponent<PlayerLook>();
            if (playerLook != null)
            {
                cam = playerLook.cam;
            }

            if (cam == null)
            {
                return;
            }
        }

        if (playerUI == null)
        {
            playerUI = GetComponent<PlayerUI>();
            if (playerUI == null)
            {
                return;
            }
        }

        // 1. If dialogue is playing, clear the UI and stop looking for interactables
        DialogueManager dialogueManager = DialogueManager.GetInstance();
        if (dialogueManager != null && dialogueManager.dialogueIsPlaying)
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
            // 2. Try to get the Interactable component from the hit object or its parent.
            Interactable interactable = hitInfo.collider.GetComponent<Interactable>();
            if (interactable == null)
            {
                interactable = hitInfo.collider.GetComponentInParent<Interactable>();
            }

            if (interactable != null)
            {
                // 3. Following NPCs are usually hidden from prompt, except when a debate
                // battle can still be triggered from the current state (e.g., upstairs zone).
                if (interactable is NPC npc && npc.isFollowing)
                {
                    if (npc.DebateBattle == null || !npc.DebateBattle.CanTriggerFromCurrentState())
                    {
                        return;
                    }
                }

                // 4. Show prompt and allow interaction.
                // TEMP DEBUG: use F to test if E conflicts with TTS input.
                playerUI.UpdateText(interactable.promptMessage);
                if (Input.GetKeyDown(interactKey))
                {
                    Debug.Log($"Interact pressed on: {interactable.name}");
                    interactable.BasseInteract();
                }
            }
        }
    }
}
