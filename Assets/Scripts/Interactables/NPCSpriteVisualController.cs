using UnityEngine;

/// <summary>
/// Controls which sprite is shown for an NPC based on existing gameplay state.
/// Intended for 2D sprite visuals used in a 3D world.
/// </summary>
public class NPCSpriteVisualController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The NPC script that drives combat/following state. If empty, we auto-find one in parent.")]
    [SerializeField] private NPC npc;

    [Tooltip("SpriteRenderer used to display the current sprite.")]
    [SerializeField] private SpriteRenderer targetRenderer;

    [Header("Sprites")]
    [Tooltip("Default sprite (for standing / not attacking).")]
    [SerializeField] private Sprite idleSprite;

    [Tooltip("Sprite shown while NPC is actively in combat.")]
    [SerializeField] private Sprite attackSprite;

    [Header("Behavior")]
    [Tooltip("If true, attack sprite can still show while following. Usually false for teammate behavior.")]
    [SerializeField] private bool showAttackWhenFollowing = false;
    [Tooltip("Print debug logs when sprite switches. Useful for setup troubleshooting.")]
    [SerializeField] private bool debugLogs = false;

    private bool hasWarnedMissingRefs;

    private void Awake()
    {
        // Auto-wire references so setup is easier in Unity.
        if (npc == null)
        {
            npc = GetComponentInParent<NPC>();
        }

        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<SpriteRenderer>();
        }

        // Extra fallback in case the script is on a parent object, not directly on the SpriteRenderer object.
        if (targetRenderer == null)
        {
            targetRenderer = GetComponentInChildren<SpriteRenderer>(true);
        }
    }

    private void LateUpdate()
    {
        if (npc == null || targetRenderer == null)
        {
            if (!hasWarnedMissingRefs)
            {
                hasWarnedMissingRefs = true;
                Debug.LogWarning($"NPCSpriteVisualController on '{name}' is missing references. NPC: {npc != null}, SpriteRenderer: {targetRenderer != null}");
            }
            return;
        }

        // Existing gameplay meaning:
        // - npc.isCombatActive == enemy mode
        // - npc.isFollowing == ally/follow mode
        bool shouldShowAttack = npc.isCombatActive && (showAttackWhenFollowing || !npc.isFollowing);

        // If attack sprite isn't set yet, fall back to idle to avoid invisible visuals.
        Sprite nextSprite = shouldShowAttack && attackSprite != null ? attackSprite : idleSprite;

        if (nextSprite != null && targetRenderer.sprite != nextSprite)
        {
            targetRenderer.sprite = nextSprite;
            if (debugLogs)
            {
                Debug.Log($"NPC sprite switched to: {nextSprite.name}");
            }
        }
    }
}
