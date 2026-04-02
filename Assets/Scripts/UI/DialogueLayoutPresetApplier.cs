using UnityEngine;

/// <summary>
/// Applies a clean dialogue layout preset to existing UI RectTransforms.
/// Visual-only helper: safe for team projects because it does not change gameplay logic.
/// </summary>
public class DialogueLayoutPresetApplier : MonoBehaviour
{
    [Header("Targets")]
    [SerializeField] private RectTransform dialoguePanelRect;
    [SerializeField] private RectTransform notebookBodyRect;
    [SerializeField] private RectTransform dialogueTextRect;
    [SerializeField] private RectTransform choicesContainerRect;
    [SerializeField] private RectTransform continueButtonRect;
    [SerializeField] private RectTransform[] choiceCardRects;

    [Header("Panel Placement")]
    [SerializeField, Range(0f, 1f)] private float panelMinX = 0.05f;
    [SerializeField, Range(0f, 1f)] private float panelMaxX = 0.95f;
    [SerializeField, Range(0f, 1f)] private float panelMinY = 0.05f;
    [SerializeField, Range(0f, 1f)] private float panelMaxY = 0.42f;

    [Header("Notebook Body")]
    [SerializeField, Range(0f, 1f)] private float bodyInsetX = 0.02f;
    [SerializeField, Range(0f, 1f)] private float bodyInsetY = 0.05f;

    [Header("Inner Blocks")]
    [SerializeField] private Vector2 dialogueBlockAnchorMin = new Vector2(0.24f, 0.56f);
    [SerializeField] private Vector2 dialogueBlockAnchorMax = new Vector2(0.96f, 0.93f);
    [SerializeField] private Vector2 choicesBlockAnchorMin = new Vector2(0.24f, 0.14f);
    [SerializeField] private Vector2 choicesBlockAnchorMax = new Vector2(0.96f, 0.52f);
    [SerializeField] private Vector2 continueAnchorMin = new Vector2(0.76f, 0.03f);
    [SerializeField] private Vector2 continueAnchorMax = new Vector2(0.96f, 0.15f);

    [Header("Choice Card Stack")]
    [SerializeField] private bool autoStackChoices = true;
    [SerializeField, Range(0f, 1f)] private float firstCardTop = 0.84f;
    [SerializeField, Range(0.05f, 0.4f)] private float cardHeight = 0.20f;
    [SerializeField, Range(0.02f, 0.4f)] private float cardVerticalStep = 0.25f;

    [ContextMenu("Apply Layout Preset")]
    public void ApplyLayoutPreset()
    {
        if (dialoguePanelRect != null)
        {
            StretchToAnchors(dialoguePanelRect, panelMinX, panelMinY, panelMaxX, panelMaxY);
        }

        if (notebookBodyRect != null)
        {
            StretchToAnchors(notebookBodyRect, bodyInsetX, bodyInsetY, 1f - bodyInsetX, 1f - bodyInsetY);
        }

        if (dialogueTextRect != null)
        {
            StretchToAnchors(dialogueTextRect, dialogueBlockAnchorMin.x, dialogueBlockAnchorMin.y, dialogueBlockAnchorMax.x, dialogueBlockAnchorMax.y);
        }

        if (choicesContainerRect != null)
        {
            StretchToAnchors(choicesContainerRect, choicesBlockAnchorMin.x, choicesBlockAnchorMin.y, choicesBlockAnchorMax.x, choicesBlockAnchorMax.y);
        }

        if (continueButtonRect != null)
        {
            StretchToAnchors(continueButtonRect, continueAnchorMin.x, continueAnchorMin.y, continueAnchorMax.x, continueAnchorMax.y);
        }

        if (autoStackChoices && choiceCardRects != null)
        {
            for (int i = 0; i < choiceCardRects.Length; i++)
            {
                RectTransform rt = choiceCardRects[i];
                if (rt == null) continue;

                float top = firstCardTop - (i * cardVerticalStep);
                float bottom = top - cardHeight;
                StretchToAnchors(rt, 0.02f, bottom, 0.98f, top);
            }
        }
    }

    private static void StretchToAnchors(RectTransform rt, float minX, float minY, float maxX, float maxY)
    {
        rt.anchorMin = new Vector2(minX, minY);
        rt.anchorMax = new Vector2(maxX, maxY);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
    }
}
