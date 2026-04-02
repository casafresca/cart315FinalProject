using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Adds stylized hover/selected feedback to a dialogue choice card.
/// Attach this to each choice button/card root.
/// </summary>
public class ChoiceCardHoverFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("References")]
    [SerializeField] private RectTransform targetRect;
    [SerializeField] private Image cardImage;
    [SerializeField] private TextMeshProUGUI cardText;

    [Header("Colors")]
    [SerializeField] private Color idleColor = new Color(0.824f, 0.761f, 0.659f, 1f);
    [SerializeField] private Color hoverColor = new Color(0.890f, 0.843f, 0.765f, 1f);
    [SerializeField] private Color selectedColor = new Color(0.498f, 0.682f, 0.820f, 1f);
    [SerializeField] private Color textIdleColor = new Color(0.122f, 0.090f, 0.071f, 1f);
    [SerializeField] private Color textHoverColor = new Color(0.122f, 0.090f, 0.071f, 1f);

    [Header("Motion")]
    [SerializeField] private float hoverScale = 1.03f;
    [SerializeField] private float selectedScale = 1.05f;
    [SerializeField] private float lerpSpeed = 12f;

    private Vector3 baseScale = Vector3.one;
    private Vector3 targetScale = Vector3.one;
    private bool selected;

    private void Awake()
    {
        if (targetRect == null) targetRect = transform as RectTransform;
        if (targetRect != null)
        {
            baseScale = targetRect.localScale;
            targetScale = baseScale;
        }

        ResetVisualState();
    }

    private void Update()
    {
        if (targetRect == null) return;
        targetRect.localScale = Vector3.Lerp(targetRect.localScale, targetScale, Time.unscaledDeltaTime * lerpSpeed);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (selected) return;
        targetScale = baseScale * hoverScale;
        if (cardImage != null) cardImage.color = hoverColor;
        if (cardText != null) cardText.color = textHoverColor;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (selected) return;
        targetScale = baseScale;
        if (cardImage != null) cardImage.color = idleColor;
        if (cardText != null) cardText.color = textIdleColor;
    }

    // Hook this in Button OnClick for selected feedback.
    public void SetSelectedVisual()
    {
        selected = true;
        targetScale = baseScale * selectedScale;
        if (cardImage != null) cardImage.color = selectedColor;
        if (cardText != null) cardText.color = textHoverColor;
    }

    public void ResetVisualState()
    {
        selected = false;
        targetScale = baseScale;
        if (cardImage != null) cardImage.color = idleColor;
        if (cardText != null) cardText.color = textIdleColor;
    }
}
