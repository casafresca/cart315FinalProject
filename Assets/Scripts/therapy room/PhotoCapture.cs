using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class PhotoCapture : MonoBehaviour
{
    [Header("Photo Capture Settings")]
    [SerializeField] private Image photoDisplayArea;
    [SerializeField] private GameObject photoFrame;
    [SerializeField] private GameObject cameraUI;
    [SerializeField] private Texture2D photoOverlayTexture;
    [SerializeField] private bool applyPhotoOverlayTexture = false;
    [SerializeField] private GameObject returnCountdownPanel;
    [SerializeField] private TextMeshProUGUI returnCountdownText;
    [SerializeField] private float returnDelaySeconds = 5f;
    [SerializeField] private string fallbackReturnSceneName = "3DEnvironmentSceneTest";

    [Header("Flash Effect")]
    [SerializeField] private GameObject cameraFlash;
    [SerializeField] private float flashTime = 0.15f;

    [Header("Photo Fader Effect")]
    [SerializeField] private Animator fadingAnimation;

    [Header("Ending Sequence")]
    [SerializeField] private Sprite[] endingPhotoSprites;
    [SerializeField] private Sprite emptyBoardSprite;
    [SerializeField] private Sprite[] completedBoardSprites;
    [SerializeField] private float capturedPhotoHoldSeconds = 1.1f;
    [SerializeField] private float emptyBoardHoldSeconds = 1f;
    [SerializeField] private float photoPlaceDuration = 1f;
    [SerializeField] private float completedBoardHoldSeconds = 1.6f;
    [SerializeField] private Vector2[] boardPhotoTargetPositions;
    [SerializeField] private Vector2[] boardPhotoTargetSizes;
    [SerializeField] private Vector2 boardPhotoStartSize = new Vector2(500f, 620f);

    [Header("Audio")]
    [SerializeField] private AudioSource cameraAudio;

    private Texture2D screenCapture;
    private Texture2D compositedPhoto;
    private Sprite capturedPhotoSprite;
    private Image photoFrameImage;
    private RectTransform photoFrameRect;
    private GameObject photoHolderMask;
    private RectTransform photoHolderMaskRect;
    private GameObject photoDisplayAreaBackground;
    private bool cachedPhotoFrameLayout;
    private Vector2 originalAnchorMin;
    private Vector2 originalAnchorMax;
    private Vector2 originalPivot;
    private Vector2 originalAnchoredPosition;
    private Vector2 originalSizeDelta;
    private bool cachedPhotoHolderLayout;
    private Vector2 originalHolderAnchorMin;
    private Vector2 originalHolderAnchorMax;
    private Vector2 originalHolderPivot;
    private Vector2 originalHolderAnchoredPosition;
    private Vector2 originalHolderSizeDelta;
    private bool originalPhotoBackgroundActive = true;
    private bool isCaptureModeActive;
    private bool viewingPhoto;
    private bool cameraUnlocked;
    private Coroutine returnCountdownRoutine;
    private Coroutine endingSequenceRoutine;

    private void Start()
    {
        screenCapture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
        CachePhotoUiReferences();
        SetPhotoVisible(false);
        SetCameraUiVisible(false);
        SetReturnCountdownVisible(false);
        Debug.Log("[PhotoCapture] Photo capture system initialized. Photo frame and camera UI start hidden.");
    }

    private void Update()
    {
        if (!cameraUnlocked || !isCaptureModeActive)
        {
            return;
        }

        if (Input.GetMouseButtonDown(0) && !viewingPhoto)
        {
            Debug.Log("[PhotoCapture] Left mouse clicked. Starting photo capture.");
            StartCoroutine(CapturePhoto());
        }

        if (Input.GetMouseButtonDown(1) && viewingPhoto && endingSequenceRoutine == null)
        {
            Debug.Log("[PhotoCapture] Right mouse clicked. Closing photo preview.");
            RemovePhoto();
        }
    }

    public void UnlockCamera()
    {
        if (cameraUnlocked)
        {
            Debug.Log("[PhotoCapture] Camera already picked up.");
            return;
        }

        cameraUnlocked = true;
        Debug.Log("[PhotoCapture] Camera picked up. Left click to take a photo.");
        EnableCaptureMode();
    }

    public bool HasCameraUnlocked()
    {
        return cameraUnlocked;
    }

    public void EnableCaptureMode()
    {
        if (!cameraUnlocked)
        {
            Debug.LogWarning("[PhotoCapture] Tried to open camera mode before the camera was picked up.");
            return;
        }

        isCaptureModeActive = true;
        viewingPhoto = false;
        SetPhotoVisible(false);
        SetCameraUiVisible(true);
        Debug.Log("[PhotoCapture] Camera mode opened. Waiting for left click to take photo.");
    }

    private IEnumerator CapturePhoto()
    {
        Debug.Log("[PhotoCapture] Left click detected. Capturing photo from current player view.");
        SetCameraUiVisible(false);
        Debug.Log("[PhotoCapture] Live camera UI hidden for clean capture.");
        yield return null;
        yield return new WaitForEndOfFrame();

        EnsureCaptureTextureMatchesScreen();

        Rect regionToRead = new Rect(0, 0, Screen.width, Screen.height);
        screenCapture.ReadPixels(regionToRead, 0, 0, false);
        screenCapture.Apply();

        Debug.Log("[PhotoCapture] Photo pixels captured from current view.");
        ShowPhoto();
    }

    private void EnsureCaptureTextureMatchesScreen()
    {
        if (screenCapture != null && screenCapture.width == Screen.width && screenCapture.height == Screen.height)
        {
            return;
        }

        if (screenCapture != null)
        {
            Destroy(screenCapture);
        }

        screenCapture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
    }

    private void ShowPhoto()
    {
        if (capturedPhotoSprite != null)
        {
            Destroy(capturedPhotoSprite);
        }

        Sprite endingPhotoSprite = GetEndingPhotoSprite();
        if (endingPhotoSprite != null)
        {
            capturedPhotoSprite = endingPhotoSprite;
        }
        else
        {
            Texture2D finalPhoto = BuildFinalPhotoTexture();

            capturedPhotoSprite = Sprite.Create(
                finalPhoto,
                new Rect(0, 0, finalPhoto.width, finalPhoto.height),
                new Vector2(0.5f, 0.5f),
                100.0f
            );
        }

        ShowCapturedPhotoSprite();

        viewingPhoto = true;
        SetPhotoVisible(true);
        Debug.Log("[PhotoCapture] Captured photo is now displayed and photo frame is visible.");

        if (fadingAnimation != null)
        {
            fadingAnimation.Play("PhotoFade");
        }

        StartCoroutine(CameraFlashEffect());

        if (endingSequenceRoutine != null)
        {
            StopCoroutine(endingSequenceRoutine);
        }

        endingSequenceRoutine = StartCoroutine(PlayEndingSequence());
    }

    private Texture2D BuildFinalPhotoTexture()
    {
        if (compositedPhoto != null)
        {
            Destroy(compositedPhoto);
        }

        compositedPhoto = new Texture2D(screenCapture.width, screenCapture.height, TextureFormat.RGBA32, false);
        compositedPhoto.SetPixels(screenCapture.GetPixels());

        if (photoOverlayTexture == null)
        {
            compositedPhoto.Apply();
            return compositedPhoto;
        }

        if (!applyPhotoOverlayTexture)
        {
            compositedPhoto.Apply();
            return compositedPhoto;
        }

        try
        {
            Color[] basePixels = compositedPhoto.GetPixels();
            Color[] overlayPixels = photoOverlayTexture.GetPixels();
            int width = compositedPhoto.width;
            int height = compositedPhoto.height;

            for (int y = 0; y < height; y++)
            {
                float v = height > 1 ? y / (float)(height - 1) : 0f;
                int overlayY = Mathf.Clamp(Mathf.RoundToInt(v * (photoOverlayTexture.height - 1)), 0, photoOverlayTexture.height - 1);

                for (int x = 0; x < width; x++)
                {
                    float u = width > 1 ? x / (float)(width - 1) : 0f;
                    int overlayX = Mathf.Clamp(Mathf.RoundToInt(u * (photoOverlayTexture.width - 1)), 0, photoOverlayTexture.width - 1);

                    int baseIndex = y * width + x;
                    int overlayIndex = overlayY * photoOverlayTexture.width + overlayX;
                    Color overlayColor = overlayPixels[overlayIndex];
                    basePixels[baseIndex] = Color.Lerp(basePixels[baseIndex], overlayColor, overlayColor.a);
                }
            }

            compositedPhoto.SetPixels(basePixels);
            compositedPhoto.Apply();
            Debug.Log("[PhotoCapture] Overlay texture composited into final photo.");
        }
        catch (UnityException)
        {
            compositedPhoto.Apply();
            Debug.LogWarning("[PhotoCapture] Overlay texture is not readable. Enable Read/Write on solider.png to bake it into the photo.");
        }

        return compositedPhoto;
    }

    private IEnumerator CameraFlashEffect()
    {
        if (cameraAudio != null)
        {
            cameraAudio.Play();
        }

        if (cameraFlash != null)
        {
            cameraFlash.SetActive(true);
        }

        yield return new WaitForSeconds(flashTime);

        if (cameraFlash != null)
        {
            cameraFlash.SetActive(false);
        }
    }

    private IEnumerator PlayEndingSequence()
    {
        yield return new WaitForSeconds(Mathf.Max(0f, capturedPhotoHoldSeconds));

        if (emptyBoardSprite != null)
        {
            ShowBoardSprite(emptyBoardSprite);
            yield return new WaitForSeconds(Mathf.Max(0f, emptyBoardHoldSeconds));
        }

        if (capturedPhotoSprite != null)
        {
            yield return StartCoroutine(AnimatePhotoPlacement());
        }

        Sprite completedBoardSprite = GetCompletedBoardSprite();
        if (completedBoardSprite != null)
        {
            ShowBoardSprite(completedBoardSprite);
            yield return new WaitForSeconds(Mathf.Max(0f, completedBoardHoldSeconds));
        }

        StartReturnCountdown();
        endingSequenceRoutine = null;
    }

    private Sprite GetCompletedBoardSprite()
    {
        if (completedBoardSprites == null || completedBoardSprites.Length == 0)
        {
            return null;
        }

        int capturedCount = TherapySessionState.RegisterCapturedSoldier();
        int index = Mathf.Clamp(capturedCount - 1, 0, completedBoardSprites.Length - 1);
        return completedBoardSprites[index];
    }

    private Sprite GetEndingPhotoSprite()
    {
        if (endingPhotoSprites == null || endingPhotoSprites.Length == 0)
        {
            return null;
        }

        int predictedCount = Mathf.Max(1, TherapySessionState.CapturedSoldierCount + 1);
        int index = Mathf.Clamp(predictedCount - 1, 0, endingPhotoSprites.Length - 1);
        return endingPhotoSprites[index];
    }

    private void RemovePhoto()
    {
        RestoreCapturedPhotoLayout();
        viewingPhoto = false;
        SetPhotoVisible(false);
        SetCameraUiVisible(isCaptureModeActive);
        Debug.Log("[PhotoCapture] Photo preview closed. Camera UI restored.");
    }

    private void StartReturnCountdown()
    {
        if (returnCountdownRoutine != null)
        {
            StopCoroutine(returnCountdownRoutine);
        }

        returnCountdownRoutine = StartCoroutine(ReturnCountdownRoutine());
    }

    private IEnumerator ReturnCountdownRoutine()
    {
        float remainingTime = returnDelaySeconds;
        SetReturnCountdownVisible(true);

        while (remainingTime > 0f)
        {
            if (returnCountdownText != null)
            {
                returnCountdownText.text = Mathf.CeilToInt(remainingTime) + "s";
            }

            yield return null;
            remainingTime -= Time.deltaTime;
        }

        if (returnCountdownText != null)
        {
            returnCountdownText.text = "Returning...";
        }

        string returnSceneName = TherapySessionState.HasReturnPoint
            ? TherapySessionState.ReturnSceneName
            : fallbackReturnSceneName;

        Debug.Log("[PhotoCapture] Countdown finished. Returning to scene: " + returnSceneName);
        SceneManager.LoadScene(returnSceneName);
    }

    private void SetPhotoVisible(bool isVisible)
    {
        if (photoDisplayArea != null)
        {
            photoDisplayArea.enabled = isVisible;
        }

        if (photoFrame != null)
        {
            photoFrame.SetActive(isVisible);
        }
    }

    private void SetCameraUiVisible(bool isVisible)
    {
        if (cameraUI != null)
        {
            cameraUI.SetActive(isVisible);
        }
    }

    private void SetReturnCountdownVisible(bool isVisible)
    {
        if (returnCountdownPanel != null)
        {
            returnCountdownPanel.SetActive(isVisible);
        }
    }

    private void CachePhotoUiReferences()
    {
        if (photoFrame != null)
        {
            photoFrameImage = photoFrame.GetComponent<Image>();
            photoFrameRect = photoFrame.GetComponent<RectTransform>();

            Transform holder = photoFrame.transform.Find("Photo_HolderMask");
            if (holder == null && photoFrame.transform.childCount > 0)
            {
                holder = photoFrame.transform.GetChild(0);
            }

            if (holder != null)
            {
                photoHolderMask = holder.gameObject;
                photoHolderMaskRect = holder.GetComponent<RectTransform>();

                if (holder.childCount > 0)
                {
                    photoDisplayAreaBackground = holder.GetChild(0).gameObject;
                    originalPhotoBackgroundActive = photoDisplayAreaBackground.activeSelf;
                }
            }

            CacheOriginalPhotoFrameLayout();
            CacheOriginalPhotoHolderLayout();
        }
    }

    private void ShowCapturedPhotoSprite()
    {
        RestoreCapturedPhotoLayout();

        if (photoDisplayArea != null)
        {
            photoDisplayArea.sprite = capturedPhotoSprite;
            photoDisplayArea.preserveAspect = true;
            photoDisplayArea.color = Color.white;
        }
    }

    private void ShowBoardSprite(Sprite sprite)
    {
        if (sprite == null)
        {
            return;
        }

        CachePhotoUiReferences();

        if (photoHolderMask != null)
        {
            photoHolderMask.SetActive(false);
        }

        ExpandPhotoFrameFullscreen();

        if (photoFrameImage != null)
        {
            photoFrameImage.sprite = sprite;
            photoFrameImage.preserveAspect = true;
            photoFrameImage.color = Color.white;
        }
        else if (photoDisplayArea != null)
        {
            photoDisplayArea.sprite = sprite;
            photoDisplayArea.preserveAspect = true;
            photoDisplayArea.color = Color.white;
        }
    }

    private IEnumerator AnimatePhotoPlacement()
    {
        CachePhotoUiReferences();

        if (photoHolderMask == null || photoHolderMaskRect == null || photoDisplayArea == null)
        {
            yield break;
        }

        if (photoDisplayAreaBackground != null)
        {
            photoDisplayAreaBackground.SetActive(false);
        }

        photoHolderMask.SetActive(true);
        photoDisplayArea.sprite = capturedPhotoSprite;
        photoDisplayArea.preserveAspect = true;
        photoDisplayArea.color = Color.white;

        photoHolderMaskRect.anchorMin = new Vector2(0.5f, 0.5f);
        photoHolderMaskRect.anchorMax = new Vector2(0.5f, 0.5f);
        photoHolderMaskRect.pivot = new Vector2(0.5f, 0.5f);
        photoHolderMaskRect.anchoredPosition = Vector2.zero;
        photoHolderMaskRect.sizeDelta = boardPhotoStartSize;

        Vector2 targetPosition = GetBoardPhotoTargetPosition();
        Vector2 targetSize = GetBoardPhotoTargetSize();
        float duration = Mathf.Max(0.01f, photoPlaceDuration);
        float timer = 0f;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / duration);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            photoHolderMaskRect.anchoredPosition = Vector2.Lerp(Vector2.zero, targetPosition, eased);
            photoHolderMaskRect.sizeDelta = Vector2.Lerp(boardPhotoStartSize, targetSize, eased);
            yield return null;
        }

        photoHolderMaskRect.anchoredPosition = targetPosition;
        photoHolderMaskRect.sizeDelta = targetSize;
    }

    private void RestoreCapturedPhotoLayout()
    {
        CachePhotoUiReferences();

        if (photoHolderMask != null)
        {
            photoHolderMask.SetActive(true);
        }

        if (photoFrameImage != null)
        {
            photoFrameImage.sprite = null;
            photoFrameImage.preserveAspect = false;
            photoFrameImage.color = Color.white;
        }

        RestoreOriginalPhotoFrameLayout();
        RestoreOriginalPhotoHolderLayout();

        if (photoDisplayAreaBackground != null)
        {
            photoDisplayAreaBackground.SetActive(originalPhotoBackgroundActive);
        }
    }

    private void CacheOriginalPhotoFrameLayout()
    {
        if (cachedPhotoFrameLayout || photoFrameRect == null)
        {
            return;
        }

        originalAnchorMin = photoFrameRect.anchorMin;
        originalAnchorMax = photoFrameRect.anchorMax;
        originalPivot = photoFrameRect.pivot;
        originalAnchoredPosition = photoFrameRect.anchoredPosition;
        originalSizeDelta = photoFrameRect.sizeDelta;
        cachedPhotoFrameLayout = true;
    }

    private void ExpandPhotoFrameFullscreen()
    {
        if (photoFrameRect == null)
        {
            return;
        }

        photoFrameRect.anchorMin = Vector2.zero;
        photoFrameRect.anchorMax = Vector2.one;
        photoFrameRect.pivot = new Vector2(0.5f, 0.5f);
        photoFrameRect.anchoredPosition = Vector2.zero;
        photoFrameRect.sizeDelta = Vector2.zero;
    }

    private void RestoreOriginalPhotoFrameLayout()
    {
        if (!cachedPhotoFrameLayout || photoFrameRect == null)
        {
            return;
        }

        photoFrameRect.anchorMin = originalAnchorMin;
        photoFrameRect.anchorMax = originalAnchorMax;
        photoFrameRect.pivot = originalPivot;
        photoFrameRect.anchoredPosition = originalAnchoredPosition;
        photoFrameRect.sizeDelta = originalSizeDelta;
    }

    private void CacheOriginalPhotoHolderLayout()
    {
        if (cachedPhotoHolderLayout || photoHolderMaskRect == null)
        {
            return;
        }

        originalHolderAnchorMin = photoHolderMaskRect.anchorMin;
        originalHolderAnchorMax = photoHolderMaskRect.anchorMax;
        originalHolderPivot = photoHolderMaskRect.pivot;
        originalHolderAnchoredPosition = photoHolderMaskRect.anchoredPosition;
        originalHolderSizeDelta = photoHolderMaskRect.sizeDelta;
        cachedPhotoHolderLayout = true;
    }

    private void RestoreOriginalPhotoHolderLayout()
    {
        if (!cachedPhotoHolderLayout || photoHolderMaskRect == null)
        {
            return;
        }

        photoHolderMaskRect.anchorMin = originalHolderAnchorMin;
        photoHolderMaskRect.anchorMax = originalHolderAnchorMax;
        photoHolderMaskRect.pivot = originalHolderPivot;
        photoHolderMaskRect.anchoredPosition = originalHolderAnchoredPosition;
        photoHolderMaskRect.sizeDelta = originalHolderSizeDelta;
    }

    private Vector2 GetBoardPhotoTargetPosition()
    {
        if (boardPhotoTargetPositions == null || boardPhotoTargetPositions.Length == 0)
        {
            return new Vector2(-292f, 58f);
        }

        int predictedCount = Mathf.Max(1, TherapySessionState.CapturedSoldierCount + 1);
        int index = Mathf.Clamp(predictedCount - 1, 0, boardPhotoTargetPositions.Length - 1);
        return boardPhotoTargetPositions[index];
    }

    private Vector2 GetBoardPhotoTargetSize()
    {
        if (boardPhotoTargetSizes == null || boardPhotoTargetSizes.Length == 0)
        {
            return new Vector2(180f, 215f);
        }

        int predictedCount = Mathf.Max(1, TherapySessionState.CapturedSoldierCount + 1);
        int index = Mathf.Clamp(predictedCount - 1, 0, boardPhotoTargetSizes.Length - 1);
        return boardPhotoTargetSizes[index];
    }

    private void OnDestroy()
    {
        if (returnCountdownRoutine != null)
        {
            StopCoroutine(returnCountdownRoutine);
        }

        if (endingSequenceRoutine != null)
        {
            StopCoroutine(endingSequenceRoutine);
        }

        if (capturedPhotoSprite != null)
        {
            Destroy(capturedPhotoSprite);
        }

        if (screenCapture != null)
        {
            Destroy(screenCapture);
        }

        if (compositedPhoto != null)
        {
            Destroy(compositedPhoto);
        }
    }
}
