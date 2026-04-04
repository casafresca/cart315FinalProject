using UnityEngine;

/// <summary>
/// Swaps Mad God's mouth sprites while he is talking.
/// Supports both pre-recorded WAV playback (AudioSource) and AI TTS playback (TTSRunner).
/// Can also billboard the sprite to always face the camera.
/// </summary>
public class MadGodTalkSpriteAnimator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SpriteRenderer targetRenderer;
    [SerializeField] private AudioSource voiceSource;
    [SerializeField] private TTSRunner ttsRunner;

    [Header("Mouth Sprites")]
    [SerializeField] private Sprite mouthClosedSprite;
    [SerializeField] private Sprite mouthOpenSprite;

    [Header("Talking Detection")]
    [Tooltip("Animate while local voice source is playing (pre-recorded WAV).")]
    [SerializeField] private bool listenToVoiceSource = true;
    [Tooltip("Animate while TTSRunner is speaking (AI-generated speech).")]
    [SerializeField] private bool listenToTTSRunner = true;

    [Header("Animation")]
    [SerializeField] private float switchInterval = 0.42f;
    [SerializeField] private bool randomizeInterval = true;
    [SerializeField] private Vector2 randomIntervalRange = new Vector2(0.36f, 0.62f);

    [Header("Facing")]
    [SerializeField] private bool alwaysFaceCamera = true;
    [SerializeField] private bool yOnlyFacing = true;
    [SerializeField] private bool reverseForward = true;

    private float nextSwitchTime;
    private bool mouthOpen;
    private bool forceTalking;

    private void Awake()
    {
        if (targetRenderer == null)
        {
            targetRenderer = GetComponentInChildren<SpriteRenderer>(true);
        }

        if (voiceSource == null)
        {
            voiceSource = GetComponent<AudioSource>();
        }

        if (ttsRunner == null)
        {
            ttsRunner = TTSRunner.Instance;
        }

        ApplyClosedMouth();
    }

    private void LateUpdate()
    {
        UpdateFacing();
    }

    private void Update()
    {
        if (targetRenderer == null)
        {
            return;
        }

        bool isTalking = forceTalking || IsVoiceSourceTalking() || IsTtsTalking();

        if (!isTalking)
        {
            ApplyClosedMouth();
            return;
        }

        if (Time.unscaledTime >= nextSwitchTime)
        {
            mouthOpen = !mouthOpen;
            targetRenderer.sprite = mouthOpen && mouthOpenSprite != null ? mouthOpenSprite : mouthClosedSprite;
            ScheduleNextSwitch();
        }
    }

    /// <summary>
    /// Optional manual override for cutscenes/custom dialogue systems.
    /// </summary>
    public void SetForceTalking(bool talking)
    {
        forceTalking = talking;
        if (!forceTalking)
        {
            ApplyClosedMouth();
        }
    }

    private bool IsVoiceSourceTalking()
    {
        return listenToVoiceSource && voiceSource != null && voiceSource.isPlaying;
    }

    private bool IsTtsTalking()
    {
        if (!listenToTTSRunner)
        {
            return false;
        }

        if (ttsRunner == null)
        {
            ttsRunner = TTSRunner.Instance;
        }

        return ttsRunner != null && ttsRunner.IsSpeaking;
    }

    private void ApplyClosedMouth()
    {
        mouthOpen = false;
        if (targetRenderer != null && mouthClosedSprite != null)
        {
            targetRenderer.sprite = mouthClosedSprite;
        }

        ScheduleNextSwitch();
    }

    private void ScheduleNextSwitch()
    {
        float interval = switchInterval;
        if (randomizeInterval)
        {
            float min = Mathf.Max(0.24f, randomIntervalRange.x);
            float max = Mathf.Max(min, randomIntervalRange.y);
            interval = Random.Range(min, max);
        }
        else
        {
            interval = Mathf.Max(0.24f, switchInterval);
        }

        nextSwitchTime = Time.unscaledTime + interval;
    }

    private void UpdateFacing()
    {
        if (!alwaysFaceCamera)
        {
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        Vector3 lookDirection = cam.transform.position - transform.position;
        if (yOnlyFacing)
        {
            lookDirection.y = 0f;
        }

        if (lookDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        Quaternion lookRot = Quaternion.LookRotation((reverseForward ? -lookDirection : lookDirection).normalized, Vector3.up);
        transform.rotation = lookRot;
    }
}
