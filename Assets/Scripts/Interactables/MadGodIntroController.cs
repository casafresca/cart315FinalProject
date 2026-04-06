using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

/// <summary>
/// Intro sequence controller for Mad God in the 3D environment scene.
/// Flow:
/// 1) Wait for trigger condition
/// 2) Mad God rushes toward player
/// 3) Player controls lock
/// 4) Camera reframes to keep Mad God in view
/// 5) Intro dialogue audio plays (or minimum hold time)
/// 6) Mad God retreats to retreat point
/// 7) Player controls restore
/// </summary>
public class MadGodIntroController : MonoBehaviour
{
    private enum IntroTriggerMode
    {
        DistanceFromGate = 0,
        PlayerTravelFromStart = 1
    }

    [Header("Scene References")]
    [SerializeField] private Transform player;
    [SerializeField] private Transform gateTransform;
    [SerializeField] private Transform retreatTarget;

    [Header("Trigger")]
    [SerializeField] private IntroTriggerMode triggerMode = IntroTriggerMode.PlayerTravelFromStart;
    [Tooltip("Used when triggerMode is DistanceFromGate.")]
    [SerializeField] private float triggerDistanceFromGate = 5f;
    [Tooltip("Used when triggerMode is PlayerTravelFromStart.")]
    [SerializeField] private float playerTravelDistanceToTrigger = 5f;

    [Header("Movement")]
    [SerializeField] private float rushSpeed = 16f;
    [SerializeField] private float retreatSpeed = 18f;
    [SerializeField] private float stopDistanceToPlayer = 1.35f;
    [SerializeField] private float stopDistanceToRetreatPoint = 0.75f;
    [SerializeField] private bool keepYPosition = true;
    [Tooltip("If retreatTarget is missing, use Mad God's start position as an auto retreat point.")]
    [SerializeField] private bool useStartPositionAsRetreatIfMissing = true;

    [Header("Camera Framing")]
    [SerializeField] private bool autoFrameMadGodOnArrival = true;
    [SerializeField] private float cameraReframeDuration = 0.35f;
    [SerializeField] private float cameraTurnSpeed = 8f;
    [SerializeField] private Vector3 cameraLookOffset = new Vector3(0f, 0f, 0f);
    [Tooltip("Optional explicit transform to frame. If empty, this object is used.")]
    [SerializeField] private Transform framingTarget;
    [Tooltip("Optional sprite renderer used to frame Mad God by sprite bounds.")]
    [SerializeField] private SpriteRenderer framingSpriteRenderer;
    [Tooltip("If true, camera uses Framing Target first. Disable this to frame directly from sprite bounds.")]
    [SerializeField] private bool preferFramingTarget = true;
    [Tooltip("0 = bottom of sprite, 1 = top. Lower values pan camera lower on the sprite.")]
    [Range(0f, 1f)]
    [SerializeField] private float spriteVerticalFramePoint = 0.42f;

    [Header("Intro Audio (Pre-recorded)")]
    [SerializeField] private AudioSource voiceSource;
    [SerializeField] private AudioClip[] introClips;
    [Tooltip("If introClips is empty, auto-load clips from Resources folder.")]
    [SerializeField] private bool autoLoadClipsFromResources = true;
    [Tooltip("Resources path to load intro clips from (no file extension).")]
    [SerializeField] private string introClipsResourcesPath = "Audio/MadGodIntro";
    [SerializeField] private float delayBeforeFirstLine = 0.15f;
    [SerializeField] private float delayBetweenLines = 0.12f;
    [Tooltip("Even if no clips are available, hold Mad God in front of player for this long.")]
    [SerializeField] private float minimumConfrontationTime = 2.5f;

    [Header("Skip Intro")]
    [SerializeField] private bool allowSkipIntro = true;
    [SerializeField] private KeyCode skipIntroKey = KeyCode.X;
    [Tooltip("Optional quick line played if the player skips the intro.")]
    [SerializeField] private AudioClip skipResponseClip;
    [TextArea]
    [SerializeField] private string skipResponseSubtitle = "Impatient, are you?";
    [SerializeField] private float skipSubtitleSecondsIfNoClip = 1.25f;

    [Header("Optional Subtitles")]
    [SerializeField] private TextMeshProUGUI subtitleText;
    [SerializeField] private string[] subtitleLines;
    [Tooltip("Optional transcript text file. If empty, the controller will try StreamingAssets/TTS/wavs/madGodTranscript.txt.")]
    [SerializeField] private TextAsset subtitleTranscriptAsset;
    [SerializeField] private string subtitleTranscriptStreamingAssetsRelativePath = "TTS/wavs/madGodTranscript.txt";
    [SerializeField] private bool typewriterSubtitles = true;
    [SerializeField] private float minimumSubtitleCharactersPerSecond = 18f;
    [SerializeField] private float subtitleFontSize = 26f;

    [Header("Player Lock")]
    [Tooltip("Scripts disabled while intro is running (InputManager, Weapon, etc).")]
    [SerializeField] private MonoBehaviour[] playerScriptsToDisable;
    [SerializeField] private bool autoFindInputAndWeapon = true;

    [Header("State")]
    [SerializeField] private bool playOnce = true;
    [SerializeField] private bool autoStart = true;

    private bool hasPlayed;
    private bool introRunning;
    private bool skipRequested;
    private Transform runtimeRetreatTarget;
    private Vector3 playerStartPositionAtIntroStart;
    private PlayerLook cachedPlayerLook;
    private GameObject runtimeSubtitleRoot;
    private Coroutine subtitleTypewriterRoutine;

    private void Start()
    {
        if (playOnce && TherapySessionState.HasCompletedMadGodIntro)
        {
            hasPlayed = true;
        }

        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                player = playerObj.transform;
            }
        }

        if (player != null)
        {
            cachedPlayerLook = player.GetComponent<PlayerLook>();
        }

        if (voiceSource == null)
        {
            voiceSource = GetComponent<AudioSource>();
        }

        if (framingSpriteRenderer == null)
        {
            framingSpriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        }

        EnsureSubtitleTextExists();

        if ((introClips == null || introClips.Length == 0) && autoLoadClipsFromResources)
        {
            TryAutoLoadIntroClips();
        }

        if ((subtitleLines == null || subtitleLines.Length == 0))
        {
            subtitleLines = LoadSubtitleLinesFromTranscript();
        }

        if (retreatTarget == null && useStartPositionAsRetreatIfMissing)
        {
            GameObject autoPoint = new GameObject("MadGod_RetreatPoint_Auto");
            autoPoint.transform.position = transform.position;
            runtimeRetreatTarget = autoPoint.transform;
            retreatTarget = runtimeRetreatTarget;
        }

        if (autoFindInputAndWeapon && player != null)
        {
            BuildDefaultDisableList();
        }

        if (subtitleText != null)
        {
            subtitleText.fontSize = subtitleFontSize;
            subtitleText.text = string.Empty;
            subtitleText.gameObject.SetActive(false);
        }

        if (autoStart)
        {
            StartIntro();
        }
    }

    [ContextMenu("Start Intro")]
    public void StartIntro()
    {
        if (introRunning) return;
        if (playOnce && TherapySessionState.HasCompletedMadGodIntro) return;
        if (playOnce && hasPlayed) return;
        StartCoroutine(IntroRoutine());
    }

    private IEnumerator IntroRoutine()
    {
        introRunning = true;
        skipRequested = false;

        if (player == null)
        {
            Debug.LogWarning("MadGodIntroController: Missing player reference. Intro aborted.");
            introRunning = false;
            yield break;
        }

        playerStartPositionAtIntroStart = player.position;

        while (!HasTriggerConditionBeenMet())
        {
            yield return null;
        }

        while (player != null && Vector3.Distance(transform.position, player.position) > stopDistanceToPlayer)
        {
            MoveTowards(player.position, rushSpeed);
            yield return null;
        }

        SetPlayerLocked(true);

        if (autoFrameMadGodOnArrival && cameraReframeDuration > 0f)
        {
            yield return ReframeCameraForDuration(cameraReframeDuration);
        }

        if (delayBeforeFirstLine > 0f)
        {
            float timer = 0f;
            while (timer < delayBeforeFirstLine)
            {
                if (CheckSkipRequested())
                {
                    break;
                }

                UpdateCameraFrameTowardsMadGod();
                timer += Time.deltaTime;
                yield return null;
            }
        }

        float confrontationStartTime = Time.time;
        bool playedAnyClip = false;

        for (int i = 0; i < introClips.Length; i++)
        {
            if (skipRequested)
            {
                break;
            }

            AudioClip clip = introClips[i];
            if (clip == null || voiceSource == null) continue;

            playedAnyClip = true;
            ShowSubtitle(i);

            voiceSource.clip = clip;
            voiceSource.Play();

            while (voiceSource.isPlaying)
            {
                if (CheckSkipRequested())
                {
                    voiceSource.Stop();
                    break;
                }

                UpdateCameraFrameTowardsMadGod();
                yield return null;
            }

            if (skipRequested)
            {
                break;
            }

            if (delayBetweenLines > 0f)
            {
                float interTimer = 0f;
                while (interTimer < delayBetweenLines)
                {
                    if (CheckSkipRequested())
                    {
                        break;
                    }

                    UpdateCameraFrameTowardsMadGod();
                    interTimer += Time.deltaTime;
                    yield return null;
                }
            }
        }

        if (skipRequested)
        {
            yield return PlaySkipResponseThenRetreat();
        }
        else
        {
            float elapsed = Time.time - confrontationStartTime;
            float remainingHold = minimumConfrontationTime - elapsed;
            if (remainingHold > 0f)
            {
                yield return WaitSecondsWithFraming(remainingHold);
            }

            if (!playedAnyClip)
            {
                Debug.LogWarning("MadGodIntroController: No intro clips played. Check introClips or Resources auto-load path.");
            }

            HideSubtitle();
        }

        if (retreatTarget != null)
        {
            while (Vector3.Distance(transform.position, retreatTarget.position) > stopDistanceToRetreatPoint)
            {
                MoveTowards(retreatTarget.position, retreatSpeed);
                yield return null;
            }
        }

        SetPlayerLocked(false);

        hasPlayed = true;
        TherapySessionState.MarkMadGodIntroCompleted();
        introRunning = false;
    }

    private bool CheckSkipRequested()
    {
        if (!allowSkipIntro || skipRequested)
        {
            return skipRequested;
        }

        if (Input.GetKeyDown(skipIntroKey))
        {
            skipRequested = true;
        }

        return skipRequested;
    }

    private IEnumerator PlaySkipResponseThenRetreat()
    {
        if (voiceSource != null && voiceSource.isPlaying)
        {
            voiceSource.Stop();
        }

        if (subtitleText != null)
        {
            subtitleText.gameObject.SetActive(true);
            subtitleText.text = skipResponseSubtitle;
        }

        if (skipResponseClip != null && voiceSource != null)
        {
            voiceSource.clip = skipResponseClip;
            voiceSource.Play();

            while (voiceSource.isPlaying)
            {
                UpdateCameraFrameTowardsMadGod();
                yield return null;
            }
        }
        else
        {
            float timer = 0f;
            float holdTime = Mathf.Max(0f, skipSubtitleSecondsIfNoClip);
            while (timer < holdTime)
            {
                UpdateCameraFrameTowardsMadGod();
                timer += Time.deltaTime;
                yield return null;
            }
        }

        HideSubtitle();
    }

    private bool HasTriggerConditionBeenMet()
    {
        switch (triggerMode)
        {
            case IntroTriggerMode.DistanceFromGate:
                if (gateTransform == null)
                {
                    return false;
                }
                return Vector3.Distance(player.position, gateTransform.position) >= triggerDistanceFromGate;

            case IntroTriggerMode.PlayerTravelFromStart:
            default:
                return Vector3.Distance(player.position, playerStartPositionAtIntroStart) >= playerTravelDistanceToTrigger;
        }
    }

    private void MoveTowards(Vector3 targetWorldPos, float speed)
    {
        Vector3 from = transform.position;
        Vector3 to = targetWorldPos;

        if (keepYPosition)
        {
            to.y = from.y;
        }

        Vector3 delta = to - from;
        if (delta.sqrMagnitude <= 0.000001f)
        {
            return;
        }

        Vector3 next = Vector3.MoveTowards(from, to, speed * Time.deltaTime);
        transform.position = next;

        Vector3 lookDir = delta.normalized;
        if (lookDir.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), Time.deltaTime * 12f);
        }
    }

    private void SetPlayerLocked(bool locked)
    {
        if (playerScriptsToDisable == null) return;

        for (int i = 0; i < playerScriptsToDisable.Length; i++)
        {
            MonoBehaviour script = playerScriptsToDisable[i];
            if (script == null) continue;
            script.enabled = !locked;
        }
    }

    private void ShowSubtitle(int clipIndex)
    {
        if (subtitleText == null) return;

        subtitleText.gameObject.SetActive(true);
        subtitleText.fontSize = subtitleFontSize;

        string line = string.Empty;
        if (subtitleLines != null && clipIndex >= 0 && clipIndex < subtitleLines.Length)
        {
            line = subtitleLines[clipIndex];
        }

        if (subtitleTypewriterRoutine != null)
        {
            StopCoroutine(subtitleTypewriterRoutine);
            subtitleTypewriterRoutine = null;
        }

        subtitleText.text = string.Empty;
        subtitleTypewriterRoutine = StartCoroutine(TypeSubtitleRoutine(line, clipIndex));
    }

    private void HideSubtitle()
    {
        if (subtitleText == null) return;

        if (subtitleTypewriterRoutine != null)
        {
            StopCoroutine(subtitleTypewriterRoutine);
            subtitleTypewriterRoutine = null;
        }

        subtitleText.text = string.Empty;
        subtitleText.gameObject.SetActive(false);
    }

    private void BuildDefaultDisableList()
    {
        if (player == null) return;

        InputManager inputManager = player.GetComponent<InputManager>();
        Weapon weapon = player.GetComponentInChildren<Weapon>(true);

        if (inputManager != null && weapon != null)
        {
            playerScriptsToDisable = new MonoBehaviour[] { inputManager, weapon };
            return;
        }

        if (inputManager != null)
        {
            playerScriptsToDisable = new MonoBehaviour[] { inputManager };
            return;
        }

        if (weapon != null)
        {
            playerScriptsToDisable = new MonoBehaviour[] { weapon };
        }
    }

    private void TryAutoLoadIntroClips()
    {
        AudioClip[] loaded = Resources.LoadAll<AudioClip>(introClipsResourcesPath);
        if (loaded == null || loaded.Length == 0)
        {
            Debug.LogWarning($"MadGodIntroController: No clips found at Resources/{introClipsResourcesPath}");
            return;
        }

        List<AudioClip> ordered = loaded
            .OrderBy(c => ExtractFirstNumber(c != null ? c.name : string.Empty))
            .ThenBy(c => c != null ? c.name : string.Empty)
            .ToList();

        introClips = ordered.ToArray();
        Debug.Log($"MadGodIntroController: Auto-loaded {introClips.Length} intro clips from Resources/{introClipsResourcesPath}");
    }

    private void EnsureSubtitleTextExists()
    {
        if (subtitleText != null)
        {
            return;
        }

        Canvas canvas = FindObjectOfType<Canvas>(true);
        if (canvas == null)
        {
            Debug.LogWarning("MadGodIntroController: No Canvas found for runtime subtitles.");
            return;
        }

        runtimeSubtitleRoot = new GameObject("MadGodIntroSubtitles", typeof(RectTransform));
        runtimeSubtitleRoot.transform.SetParent(canvas.transform, false);

        RectTransform rootRect = runtimeSubtitleRoot.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.1f, 0f);
        rootRect.anchorMax = new Vector2(0.9f, 0f);
        rootRect.pivot = new Vector2(0.5f, 0f);
        rootRect.anchoredPosition = new Vector2(0f, 36f);
        rootRect.sizeDelta = new Vector2(0f, 120f);

        subtitleText = runtimeSubtitleRoot.AddComponent<TextMeshProUGUI>();
        subtitleText.text = string.Empty;
        subtitleText.fontSize = subtitleFontSize;
        subtitleText.alignment = TextAlignmentOptions.Bottom;
        subtitleText.enableWordWrapping = true;
        subtitleText.color = Color.white;
        subtitleText.outlineWidth = 0.2f;
        subtitleText.outlineColor = new Color(0f, 0f, 0f, 1f);
        subtitleText.gameObject.SetActive(false);
    }

    private string[] LoadSubtitleLinesFromTranscript()
    {
        string transcriptText = string.Empty;

        if (subtitleTranscriptAsset != null)
        {
            transcriptText = subtitleTranscriptAsset.text;
        }
        else
        {
            string transcriptPath = Path.Combine(Application.streamingAssetsPath, subtitleTranscriptStreamingAssetsRelativePath);
            if (File.Exists(transcriptPath))
            {
                transcriptText = File.ReadAllText(transcriptPath);
            }
        }

        if (string.IsNullOrWhiteSpace(transcriptText))
        {
            Debug.LogWarning("MadGodIntroController: No transcript file found for intro subtitles.");
            return System.Array.Empty<string>();
        }

        string normalized = transcriptText.Replace("\r\n", "\n").Trim();
        string[] blocks = Regex.Split(normalized, "\n\\s*\n");
        List<string> cleanedLines = new List<string>();

        for (int i = 0; i < blocks.Length; i++)
        {
            string line = blocks[i].Replace("\n", " ").Trim();
            if (!string.IsNullOrWhiteSpace(line))
            {
                cleanedLines.Add(line);
            }
        }

        Debug.Log($"MadGodIntroController: Loaded {cleanedLines.Count} subtitle lines from transcript.");
        return cleanedLines.ToArray();
    }

    private IEnumerator TypeSubtitleRoutine(string fullText, int clipIndex)
    {
        if (string.IsNullOrWhiteSpace(fullText))
        {
            subtitleText.text = string.Empty;
            subtitleTypewriterRoutine = null;
            yield break;
        }

        AudioClip clip = null;
        if (introClips != null && clipIndex >= 0 && clipIndex < introClips.Length)
        {
            clip = introClips[clipIndex];
        }

        if (!typewriterSubtitles)
        {
            subtitleText.text = fullText;
            subtitleTypewriterRoutine = null;
            yield break;
        }

        float clipLength = clip != null ? clip.length : 0f;
        float safeClipLength = Mathf.Max(0.01f, clipLength);
        float charactersPerSecondFromClip = fullText.Length / safeClipLength;
        float charactersPerSecond = Mathf.Max(minimumSubtitleCharactersPerSecond, charactersPerSecondFromClip);
        float visibleCharacters = 0f;

        while (visibleCharacters < fullText.Length)
        {
            visibleCharacters += charactersPerSecond * Time.deltaTime;
            int count = Mathf.Clamp(Mathf.FloorToInt(visibleCharacters), 0, fullText.Length);
            subtitleText.text = fullText.Substring(0, count);
            yield return null;
        }

        subtitleText.text = fullText;
        subtitleTypewriterRoutine = null;
    }

    private static int ExtractFirstNumber(string input)
    {
        Match match = Regex.Match(input ?? string.Empty, "\\d+");
        if (!match.Success) return int.MaxValue;
        if (int.TryParse(match.Value, out int value)) return value;
        return int.MaxValue;
    }

    private IEnumerator ReframeCameraForDuration(float seconds)
    {
        float timer = 0f;
        while (timer < seconds)
        {
            UpdateCameraFrameTowardsMadGod();
            timer += Time.deltaTime;
            yield return null;
        }
    }

    private IEnumerator WaitSecondsWithFraming(float seconds)
    {
        float timer = 0f;
        while (timer < seconds)
        {
            UpdateCameraFrameTowardsMadGod();
            timer += Time.deltaTime;
            yield return null;
        }
    }

    private void UpdateCameraFrameTowardsMadGod()
    {
        if (!autoFrameMadGodOnArrival)
        {
            return;
        }

        Camera cam = null;
        if (cachedPlayerLook != null && cachedPlayerLook.cam != null)
        {
            cam = cachedPlayerLook.cam;
        }
        else
        {
            cam = Camera.main;
        }

        if (cam == null)
        {
            return;
        }

        Vector3 target = GetFramingTargetWorldPoint();
        Vector3 lookDir = target - cam.transform.position;
        if (lookDir.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Quaternion targetRot = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
        cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, targetRot, Time.deltaTime * Mathf.Max(0.1f, cameraTurnSpeed));
    }

    private Vector3 GetFramingTargetWorldPoint()
    {
        if (preferFramingTarget && framingTarget != null)
        {
            return framingTarget.position + cameraLookOffset;
        }

        if (framingSpriteRenderer != null)
        {
            Bounds bounds = framingSpriteRenderer.bounds;
            float normalizedY = Mathf.Clamp01(spriteVerticalFramePoint);
            float y = Mathf.Lerp(bounds.min.y, bounds.max.y, normalizedY);
            Vector3 spritePoint = new Vector3(bounds.center.x, y, bounds.center.z);
            return spritePoint + cameraLookOffset;
        }

        if (framingTarget != null)
        {
            return framingTarget.position + cameraLookOffset;
        }

        return transform.position + cameraLookOffset;
    }
}
