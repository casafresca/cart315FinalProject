using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class PhotoCapture : MonoBehaviour
{
    private const string BoardRevealAnimatorStateName = "PhotoFade";

    [Header("Photo Capture Settings")]
    [SerializeField] private Image photoDisplayArea;
    [SerializeField] private GameObject photoFrame;
    [SerializeField] private GameObject cameraUI;
    [SerializeField] private Texture2D photoOverlayTexture;
    [SerializeField] private GameObject returnCountdownPanel;
    [SerializeField] private TextMeshProUGUI returnCountdownText;
    [SerializeField] private float returnDelaySeconds = 5f;
    [SerializeField] private string fallbackReturnSceneName = "3DEnvironmentSceneTest";

    [Header("Flash Effect")]
    [SerializeField] private GameObject cameraFlash;
    [SerializeField] private float flashTime = 0.15f;

    [Header("Photo Fader Effect")]
    [SerializeField] private Animator fadingAnimation;

    [Header("Board Reveal Sequence")]
    [SerializeField] private GameObject boardSequenceRoot;
    [SerializeField] private Image boardBackgroundImage;
    [SerializeField] private Image developingPhotoImage;
    [SerializeField] private CanvasGroup developingPhotoCanvasGroup;
    [SerializeField] private Sprite emptyBoardSprite;
    [SerializeField] private Sprite[] completedBoardSprites;
    [SerializeField] private float photoPreviewSeconds = 1.1f;
    [SerializeField] private float boardAppearDelay = 0.2f;
    [SerializeField] private float photoDevelopDuration = 2.75f;
    [SerializeField] private float completedBoardHoldSeconds = 1.35f;

    [Header("Audio")]
    [SerializeField] private AudioSource cameraAudio;

    private Texture2D screenCapture;
    private Texture2D compositedPhoto;
    private Sprite capturedPhotoSprite;
    private bool isCaptureModeActive;
    private bool viewingPhoto;
    private bool cameraUnlocked;
    private Coroutine returnCountdownRoutine;
    private Coroutine revealSequenceRoutine;

    private void Start()
    {
        screenCapture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
        SetPhotoVisible(false);
        SetCameraUiVisible(false);
        SetReturnCountdownVisible(false);
        SetBoardSequenceVisible(false);
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

        if (Input.GetMouseButtonDown(1) && viewingPhoto && revealSequenceRoutine == null)
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

        Texture2D finalPhoto = BuildFinalPhotoTexture();

        capturedPhotoSprite = Sprite.Create(
            finalPhoto,
            new Rect(0, 0, finalPhoto.width, finalPhoto.height),
            new Vector2(0.5f, 0.5f),
            100.0f
        );

        if (photoDisplayArea != null)
        {
            photoDisplayArea.sprite = capturedPhotoSprite;
            photoDisplayArea.preserveAspect = true;
        }

        isCaptureModeActive = false;
        viewingPhoto = true;
        SetPhotoVisible(true);
        Debug.Log("[PhotoCapture] Captured photo is now displayed and photo frame is visible.");

        if (revealSequenceRoutine != null)
        {
            StopCoroutine(revealSequenceRoutine);
        }

        revealSequenceRoutine = StartCoroutine(PhotoEndingSequence());
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

    private IEnumerator PhotoEndingSequence()
    {
        yield return StartCoroutine(CameraFlashEffect());

        PlayPhotoFadeAnimation();
        yield return new WaitForSeconds(Mathf.Max(0f, photoPreviewSeconds));

        viewingPhoto = false;
        SetPhotoVisible(false);

        if (boardSequenceRoot == null || boardBackgroundImage == null)
        {
            Debug.LogWarning("[PhotoCapture] Board reveal UI is not fully assigned. Falling back to countdown return.");
            StartReturnCountdown();
            revealSequenceRoutine = null;
            yield break;
        }

        yield return new WaitForSeconds(Mathf.Max(0f, boardAppearDelay));

        int capturedCount = TherapySessionState.RegisterCapturedSoldier();
        yield return StartCoroutine(PlayBoardRevealSequence(capturedCount));

        StartReturnCountdown();
        revealSequenceRoutine = null;
    }

    private IEnumerator PlayBoardRevealSequence(int capturedCount)
    {
        SetBoardSequenceVisible(true);

        if (emptyBoardSprite != null)
        {
            boardBackgroundImage.sprite = emptyBoardSprite;
            boardBackgroundImage.preserveAspect = true;
        }

        if (developingPhotoImage != null)
        {
            developingPhotoImage.sprite = capturedPhotoSprite;
            developingPhotoImage.preserveAspect = true;
            developingPhotoImage.enabled = capturedPhotoSprite != null;
            developingPhotoImage.color = new Color(1f, 1f, 1f, 0f);
        }

        if (developingPhotoCanvasGroup != null)
        {
            developingPhotoCanvasGroup.alpha = 0f;
        }

        float duration = Mathf.Max(0.01f, photoDevelopDuration);
        float timer = 0f;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / duration);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            SetDevelopmentVisibility(eased);
            yield return null;
        }

        SetDevelopmentVisibility(1f);

        Sprite completedBoardSprite = GetCompletedBoardSpriteForCount(capturedCount);
        if (completedBoardSprite != null)
        {
            boardBackgroundImage.sprite = completedBoardSprite;
            boardBackgroundImage.preserveAspect = true;
        }

        if (developingPhotoImage != null)
        {
            developingPhotoImage.enabled = false;
        }

        if (developingPhotoCanvasGroup != null)
        {
            developingPhotoCanvasGroup.alpha = 0f;
        }

        yield return new WaitForSeconds(Mathf.Max(0f, completedBoardHoldSeconds));
        SetBoardSequenceVisible(false);
    }

    private void SetDevelopmentVisibility(float normalized)
    {
        float alpha = Mathf.Clamp01(normalized);

        if (developingPhotoCanvasGroup != null)
        {
            developingPhotoCanvasGroup.alpha = alpha;
        }

        if (developingPhotoImage != null)
        {
            Color color = developingPhotoImage.color;
            color.a = alpha;
            developingPhotoImage.color = color;
        }
    }

    private Sprite GetCompletedBoardSpriteForCount(int capturedCount)
    {
        if (completedBoardSprites == null || completedBoardSprites.Length == 0)
        {
            return emptyBoardSprite;
        }

        int index = Mathf.Clamp(capturedCount - 1, 0, completedBoardSprites.Length - 1);
        return completedBoardSprites[index];
    }

    private void PlayPhotoFadeAnimation()
    {
        if (fadingAnimation != null)
        {
            fadingAnimation.Play(BoardRevealAnimatorStateName);
        }
    }

    private void RemovePhoto()
    {
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

    private void SetBoardSequenceVisible(bool isVisible)
    {
        if (boardSequenceRoot != null)
        {
            boardSequenceRoot.SetActive(isVisible);
        }
    }

    private void OnDestroy()
    {
        if (returnCountdownRoutine != null)
        {
            StopCoroutine(returnCountdownRoutine);
        }

        if (revealSequenceRoutine != null)
        {
            StopCoroutine(revealSequenceRoutine);
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
