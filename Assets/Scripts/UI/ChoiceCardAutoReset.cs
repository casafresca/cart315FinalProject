using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Optional helper: automatically resets choice card visuals when a choice button gets re-enabled.
/// Add to each choice button if you want zero manual reset wiring.
/// </summary>
public class ChoiceCardAutoReset : MonoBehaviour
{
    [SerializeField] private ChoiceCardHoverFX hoverFX;
    [SerializeField] private Button button;

    private bool wasActive;

    private void Awake()
    {
        if (hoverFX == null) hoverFX = GetComponent<ChoiceCardHoverFX>();
        if (button == null) button = GetComponent<Button>();
        wasActive = gameObject.activeInHierarchy;
    }

    private void Update()
    {
        bool isActive = gameObject.activeInHierarchy;

        // If this choice just became active again (next dialogue round), reset visual state.
        if (isActive && !wasActive && hoverFX != null)
        {
            hoverFX.ResetVisualState();
        }

        wasActive = isActive;
    }
}
