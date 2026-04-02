using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Stylizes an existing dialogue UI using a field notebook + polaroid look.
/// This script is visual-only and does not change dialogue gameplay logic.
/// You can art-direct most of the look from Inspector (colors + texture sprites).
/// </summary>
public class DialogueThemeStyler : MonoBehaviour
{
    [Header("Panel Images")]
    [SerializeField] private Image dimBackdrop;
    [SerializeField] private Image dialoguePanel;
    [SerializeField] private Image notebookBody;
    [SerializeField] private Image polaroidFrame;
    [SerializeField] private Image[] choiceCards;

    [Header("Optional Texture Layers")]
    [Tooltip("Base paper texture used on notebook body.")]
    [SerializeField] private Sprite notebookPaperSprite;
    [Tooltip("Old photo frame texture used on the polaroid frame.")]
    [SerializeField] private Sprite polaroidFrameSprite;
    [Tooltip("Card paper texture for choice cards.")]
    [SerializeField] private Sprite choiceCardSprite;
    [Tooltip("Transparent scratches texture. Good for worn/aged look.")]
    [SerializeField] private Sprite scratchOverlaySprite;
    [Tooltip("Transparent grain/noise texture for film/photo feeling.")]
    [SerializeField] private Sprite grainOverlaySprite;
    [Tooltip("UI Image used as scratches overlay. Place above notebook/polaroid in hierarchy.")]
    [SerializeField] private Image scratchOverlayImage;
    [Tooltip("UI Image used as grain overlay. Place above notebook/polaroid in hierarchy.")]
    [SerializeField] private Image grainOverlayImage;

    [Header("Overlay Opacity")]
    [SerializeField, Range(0f, 1f)] private float scratchOpacity = 0.25f;
    [SerializeField, Range(0f, 1f)] private float grainOpacity = 0.18f;

    [Header("Text")]
    [SerializeField] private TextMeshProUGUI dialogueText;
    [SerializeField] private TextMeshProUGUI[] choiceTexts;
    [SerializeField] private TextMeshProUGUI[] labelTexts;

    [Header("Palette")]
    [SerializeField] private Color dimColor = new Color(0.055f, 0.039f, 0.031f, 0.58f);      // #0E0A08 @58%
    [SerializeField] private Color paperBase = new Color(0.824f, 0.761f, 0.659f, 1f);        // #D2C2A8
    [SerializeField] private Color paperDark = new Color(0.698f, 0.604f, 0.498f, 1f);        // #B29A7F
    [SerializeField] private Color rustBrown = new Color(0.416f, 0.294f, 0.227f, 1f);        // #6A4B3A
    [SerializeField] private Color driedBlood = new Color(0.478f, 0.180f, 0.165f, 1f);       // #7A2E2A
    [SerializeField] private Color inkText = new Color(0.122f, 0.090f, 0.071f, 1f);          // #1F1712

    [Header("Typography")]
    [SerializeField] private float dialogueFontSize = 34f;
    [SerializeField] private float choiceFontSize = 29f;
    [SerializeField] private float labelFontSize = 19f;
    [SerializeField] private FontStyles labelStyle = FontStyles.UpperCase;

    [Header("Card Style")]
    [SerializeField] private bool addChoiceNumbers = true;
    [SerializeField] private bool applyCardTilt = true;
    [SerializeField] private float[] cardTiltZ = new float[] { -1.2f, 0.7f, -0.8f, 1.1f };

    [Header("Quick Actions")]
    [Tooltip("Check this in Inspector to apply the theme immediately.")]
    [SerializeField] private bool applyNow;

    private void Awake()
    {
        ApplyTheme();
    }

    private void OnValidate()
    {
        if (applyNow)
        {
            applyNow = false;
            ApplyTheme();
            return;
        }

        ApplyTheme();
    }

    [ContextMenu("Apply Theme Now")]
    public void ApplyTheme()
    {
        ApplyPanelColors();
        ApplyPanelSpritesAndTextures();
        ApplyTextStyles();
        ApplyChoiceCards();
        ApplyOverlayLayers();
    }

    private void ApplyPanelColors()
    {
        if (dimBackdrop != null) dimBackdrop.color = dimColor;
        if (dialoguePanel != null) dialoguePanel.color = dimColor;

        if (notebookBody != null)
        {
            notebookBody.color = paperBase;
            EnsureOutline(notebookBody, rustBrown, new Vector2(1f, -1f));
            EnsureShadow(notebookBody, new Color(0.05f, 0.03f, 0.02f, 0.45f), new Vector2(3f, -3f));
        }

        if (polaroidFrame != null)
        {
            polaroidFrame.color = paperDark;
            EnsureOutline(polaroidFrame, rustBrown, new Vector2(1f, -1f));
        }
    }

    private void ApplyPanelSpritesAndTextures()
    {
        // Assign optional sprites so UI can use real paper/photo texture art instead of flat fills.
        ApplySpriteIfSet(notebookBody, notebookPaperSprite, Image.Type.Sliced);
        ApplySpriteIfSet(polaroidFrame, polaroidFrameSprite, Image.Type.Sliced);
    }

    private void ApplyOverlayLayers()
    {
        if (scratchOverlayImage != null)
        {
            ConfigureOverlayImage(scratchOverlayImage, scratchOverlaySprite, scratchOpacity);
        }

        if (grainOverlayImage != null)
        {
            ConfigureOverlayImage(grainOverlayImage, grainOverlaySprite, grainOpacity);
        }
    }

    private void ApplyTextStyles()
    {
        if (dialogueText != null)
        {
            dialogueText.color = inkText;
            dialogueText.fontSize = dialogueFontSize;
            dialogueText.alignment = TextAlignmentOptions.TopLeft;
            dialogueText.enableWordWrapping = true;
            dialogueText.margin = new Vector4(12f, 6f, 12f, 6f);
        }

        if (choiceTexts != null)
        {
            for (int i = 0; i < choiceTexts.Length; i++)
            {
                TextMeshProUGUI text = choiceTexts[i];
                if (text == null) continue;

                text.color = inkText;
                text.fontSize = choiceFontSize;
                text.alignment = TextAlignmentOptions.MidlineLeft;
                text.margin = new Vector4(16f, 0f, 8f, 0f);

                if (addChoiceNumbers)
                {
                    // Prevent duplicated numbering if theme gets re-applied multiple times.
                    string raw = text.text ?? string.Empty;
                    raw = Regex.Replace(raw, "^\\s*\\d+\\.\\s+", string.Empty);
                    text.text = (i + 1) + ". " + raw;
                }
            }
        }

        if (labelTexts != null)
        {
            foreach (TextMeshProUGUI label in labelTexts)
            {
                if (label == null) continue;
                label.color = driedBlood;
                label.fontSize = labelFontSize;
                label.fontStyle = labelStyle;
            }
        }
    }

    private void ApplyChoiceCards()
    {
        if (choiceCards == null) return;

        for (int i = 0; i < choiceCards.Length; i++)
        {
            Image card = choiceCards[i];
            if (card == null) continue;

            card.color = (i % 2 == 0) ? paperBase : paperDark;
            ApplySpriteIfSet(card, choiceCardSprite, Image.Type.Sliced);
            EnsureOutline(card, rustBrown, new Vector2(1f, -1f));

            if (applyCardTilt)
            {
                RectTransform rt = card.transform as RectTransform;
                if (rt != null)
                {
                    float z = (cardTiltZ != null && i < cardTiltZ.Length) ? cardTiltZ[i] : 0f;
                    rt.localRotation = Quaternion.Euler(0f, 0f, z);
                }
            }
        }
    }

    private static void ApplySpriteIfSet(Image target, Sprite sprite, Image.Type preferredType)
    {
        if (target == null || sprite == null) return;
        target.sprite = sprite;
        target.type = preferredType;
        target.preserveAspect = false;
    }

    private static void ConfigureOverlayImage(Image target, Sprite sprite, float opacity)
    {
        if (target == null) return;

        target.enabled = sprite != null && opacity > 0.001f;
        if (!target.enabled) return;

        target.sprite = sprite;
        target.type = Image.Type.Sliced;

        Color c = target.color;
        c.r = 1f;
        c.g = 1f;
        c.b = 1f;
        c.a = Mathf.Clamp01(opacity);
        target.color = c;
    }

    private static void EnsureOutline(Image img, Color color, Vector2 distance)
    {
        Outline outline = img.GetComponent<Outline>();
        if (outline == null) outline = img.gameObject.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = distance;
        outline.useGraphicAlpha = true;
    }

    private static void EnsureShadow(Image img, Color color, Vector2 distance)
    {
        Shadow shadow = img.GetComponent<Shadow>();
        if (shadow == null) shadow = img.gameObject.AddComponent<Shadow>();
        shadow.effectColor = color;
        shadow.effectDistance = distance;
        shadow.useGraphicAlpha = true;
    }
}
