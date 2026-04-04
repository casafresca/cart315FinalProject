using UnityEngine;

/// <summary>
/// Optional interaction hook for Mad God.
/// Supports:
/// - Interact() via PlayerInteract prompt key
/// - Direct key press talk when player is nearby
/// </summary>
public class MadGodCompanion : Interactable
{
    [Header("Simple Talk Options")]
    [SerializeField] private bool usePreRecordedClips = true;
    [SerializeField] private bool useTTSReply = false;
    [SerializeField] private AudioSource voiceSource;
    [SerializeField] private AudioClip[] talkClips;
    [SerializeField] private string ttsRole = "mad_god";
    [SerializeField, TextArea] private string ttsPrompt = "We are close. Keep moving.";

    [Header("Direct Key Talk (Optional)")]
    [SerializeField] private bool enableDirectKeyTalk = true;
    [SerializeField] private KeyCode directTalkKey = KeyCode.G;
    [SerializeField] private float directTalkRange = 4.5f;
    [SerializeField] private bool requireFacingForDirectTalk = false;
    [SerializeField, Range(-1f, 1f)] private float facingDotThreshold = 0.35f;
    [SerializeField] private float talkCooldownSeconds = 0.5f;

    private int clipIndex;
    private float nextTalkAllowedTime;
    private Transform playerTransform;

    private void Start()
    {
        if (playerTransform == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                playerTransform = playerObj.transform;
            }
        }
    }

    private void Update()
    {
        if (!enableDirectKeyTalk)
        {
            return;
        }

        if (!Input.GetKeyDown(directTalkKey))
        {
            return;
        }

        if (!CanDirectTalkNow())
        {
            return;
        }

        ExecuteTalk();
    }

    protected override void Interact()
    {
        ExecuteTalk();
    }

    private void ExecuteTalk()
    {
        if (Time.time < nextTalkAllowedTime)
        {
            return;
        }

        if (usePreRecordedClips)
        {
            PlayNextClip();
        }

        if (useTTSReply && TTSRunner.Instance != null)
        {
            // Avoid stacking multiple TTS requests at once.
            if (!TTSRunner.Instance.IsSpeaking)
            {
                TTSRunner.Instance.SpeakAs(ttsRole, ttsPrompt);
            }
        }

        nextTalkAllowedTime = Time.time + Mathf.Max(0.1f, talkCooldownSeconds);
    }

    private bool CanDirectTalkNow()
    {
        if (Time.time < nextTalkAllowedTime)
        {
            return false;
        }

        if (playerTransform == null)
        {
            return false;
        }

        float distance = Vector3.Distance(playerTransform.position, transform.position);
        if (distance > directTalkRange)
        {
            return false;
        }

        if (!requireFacingForDirectTalk)
        {
            return true;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            return true;
        }

        Vector3 toMadGod = (transform.position - cam.transform.position).normalized;
        float dot = Vector3.Dot(cam.transform.forward.normalized, toMadGod);
        return dot >= facingDotThreshold;
    }

    private void PlayNextClip()
    {
        if (voiceSource == null || talkClips == null || talkClips.Length == 0)
        {
            return;
        }

        if (voiceSource.isPlaying)
        {
            return;
        }

        AudioClip clip = talkClips[clipIndex % talkClips.Length];
        clipIndex++;

        if (clip == null)
        {
            return;
        }

        voiceSource.clip = clip;
        voiceSource.Play();
    }
}
