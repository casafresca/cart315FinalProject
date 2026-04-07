using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using TMPro;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

public class TTSRunner : MonoBehaviour
{
    [Serializable]
    private class SpeakerDisplayName
    {
        public string role = "soldier";
        public string displayName = "Soldier";
    }

    // GLOBAL ACCESS
    public static TTSRunner Instance;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;

    [Header("Input")]
    [SerializeField] private KeyCode triggerKey = KeyCode.E;
    [SerializeField] private string testLine = "Hello there";

    [Header("Paths")]
    [SerializeField] private string pythonExe = "python"; // Override in inspector if not in PATH
    private string ttsRoot;
    private string scriptPath;
    private string wavDir;

    [Header("AI Speed Mode")]
    [Tooltip("When enabled, use shorter AI replies and fewer voice references for faster response time.")]
    [SerializeField] private bool fastReplyMode = true;
    [SerializeField] private int fastReplyMaxChars = 220;
    [SerializeField] private int normalReplyMaxChars = 400;
    [SerializeField] private int fastMaxSpeakerReferences = 1;
    [SerializeField] private int normalMaxSpeakerReferences = 2;

    [Header("Subtitles")]
    [SerializeField] private bool enableSubtitles = true;
    [Tooltip("If empty, TTSRunner creates a bottom-screen subtitle UI at runtime.")]
    [SerializeField] private TextMeshProUGUI subtitleSpeakerText;
    [Tooltip("If empty, TTSRunner creates a bottom-screen subtitle UI at runtime.")]
    [SerializeField] private TextMeshProUGUI subtitleBodyText;
    [SerializeField] private bool typewriterSubtitles = true;
    [SerializeField] private float minimumSubtitleCharactersPerSecond = 18f;
    [SerializeField] private float subtitlePadding = 36f;
    [SerializeField] private float subtitleSpeakerFontSize = 20f;
    [SerializeField] private float subtitleBodyFontSize = 26f;
    [SerializeField] private int maxSubtitleCharacters = 140;
    [SerializeField]
    private SpeakerDisplayName[] speakerDisplayNames =
    {
        new SpeakerDisplayName { role = "soldier", displayName = "Soldier" },
        new SpeakerDisplayName { role = "player", displayName = "You" },
        new SpeakerDisplayName { role = "mad_god", displayName = "Mad God" }
    };

    private readonly ConcurrentDictionary<int, string> responseMap = new ConcurrentDictionary<int, string>();

    private Process process;

    private readonly ConcurrentQueue<string> stdoutQueue = new();
    private readonly ConcurrentQueue<string> stderrQueue = new();

    private bool isReady = false;
    private int nextId = 1;
    private bool isSpeaking = false;
    private int lastCompletedRequestId;
    private string lastCompletedReplyText = string.Empty;
    private string lastCompletedRole = string.Empty;
    private Coroutine subtitleCoroutine;
    private GameObject runtimeSubtitleCanvas;
    private GameObject runtimeSubtitlePanel;

    // Read-only state exposed for other gameplay visuals (e.g., talking sprites).
    public bool IsSpeaking => isSpeaking;
    public bool IsReady => isReady;
    public int LastCompletedRequestId => lastCompletedRequestId;
    public string LastCompletedReplyText => lastCompletedReplyText;
    public string LastCompletedRole => lastCompletedRole;

    [Header("Timing")]
    [SerializeField] private float requestTimeoutSeconds = 120f;

    private int sampleRate = 24000;

    // -------------------------
    // Unity Lifecycle
    // -------------------------
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (!audioSource)
            audioSource = gameObject.AddComponent<AudioSource>();
    }

    void Start()
    {
        Debug.Log("[TTS] Start()");

        ttsRoot = Path.Combine(Application.streamingAssetsPath, "TTS");
        scriptPath = Path.Combine(ttsRoot, "tts_cli_player_basicv3.py");
        wavDir = Path.Combine(ttsRoot, "out");
        Directory.CreateDirectory(wavDir);

        // Prefer project-local venv Python if available; otherwise keep inspector/path value.
        string bundledPython = Path.Combine(ttsRoot, ".venv", "Scripts", "python.exe");
        if (File.Exists(bundledPython))
        {
            pythonExe = bundledPython;
        }

        Debug.Log($"[TTS] Root: {ttsRoot}");
        Debug.Log($"[TTS] Script: {scriptPath}");

        EnsureSubtitleUi();
        StartPython();
    }

    public void TriggerSpeak(string textToSay)
    {
        if (!isReady) Debug.LogWarning("[TTS] Not ready yet!");
        else if (isSpeaking) Debug.LogWarning("[TTS] Already speaking!");
        else Speak(textToSay);
    }

    public void GenerateChoiceOptions(string prompt, Action<string[]> callback)
    {
        if (!isReady)
        {
            Debug.LogWarning("[TTS] Not ready yet!");
            callback?.Invoke(null);
        }
        else
        {
            StartCoroutine(GenerateChoiceOptionsRoutine(prompt, callback));
        }
    }

    public void GenerateResponse(string prompt, Action<string> callback)
    {
        if (!isReady)
        {
            Debug.LogWarning("[TTS] Not ready yet!");
            callback?.Invoke(null);
        }
        else
        {
            StartCoroutine(GenerateResponseRoutine(prompt, callback));
        }
    }

    public void GenerateDebateTurn(DebateTurnRequestData request, Action<DebateTurnResultData> callback)
    {
        if (!isReady)
        {
            Debug.LogWarning("[TTS] Not ready yet!");
            callback?.Invoke(null);
        }
        else
        {
            StartCoroutine(GenerateDebateTurnRoutine(request, callback));
        }
    }

    public void GenerateTypedConversationTurn(TypedConversationRequestData request, Action<TypedConversationResultData> callback)
    {
        if (!isReady)
        {
            Debug.LogWarning("[TTS] Not ready yet!");
            callback?.Invoke(null);
        }
        else
        {
            StartCoroutine(GenerateTypedConversationTurnRoutine(request, callback));
        }
    }

    void Update()
    {
        DrainQueues();
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        StopPython();
        ClearSubtitle();
    }

    // -------------------------
    // Python Process
    // -------------------------
    void StartPython()
    {
        Debug.Log("[TTS] Launching Python process...");

        if (!File.Exists(scriptPath)) return;

        // Debug.Log($"[TTS] EXE: {pythonExe}");
        // Debug.Log($"[TTS] ARGS: -u \"{scriptPath}\" --out-dir \"{wavDir}\"");
        // Debug.Log($"[TTS] WorkingDir: {ttsRoot}");
        if (process != null && !process.HasExited) return;

        Debug.Log("Starting Python TTS...");

        var psi = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = $"-u \"{scriptPath}\" --out-dir \"{wavDir}\"",
            WorkingDirectory = ttsRoot,

            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        ApplyRuntimeAiEnv(psi);

        process = new Process { StartInfo = psi };

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                stdoutQueue.Enqueue(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                stderrQueue.Enqueue(e.Data);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            Debug.LogError("[TTS] Failed to start Python process: " + ex.Message);
        }
    }

    private void ApplyRuntimeAiEnv(ProcessStartInfo psi)
    {
        string fastEnabled = fastReplyMode ? "true" : "false";
        string fastChars = Mathf.Max(80, fastReplyMaxChars).ToString();
        string normalChars = Mathf.Max(120, normalReplyMaxChars).ToString();
        int refs = Mathf.Max(1, fastReplyMode ? fastMaxSpeakerReferences : normalMaxSpeakerReferences);

        psi.EnvironmentVariables["FAST_REPLY_ENABLED"] = fastEnabled;
        psi.EnvironmentVariables["FAST_REPLY_MAX_CHARS"] = fastChars;
        psi.EnvironmentVariables["NORMAL_REPLY_MAX_CHARS"] = normalChars;
        psi.EnvironmentVariables["MAX_SPEAKER_REFERENCES"] = refs.ToString();

        Debug.Log($"[TTS] AI Speed Mode: {(fastReplyMode ? "FAST" : "NORMAL")}, chars={(fastReplyMode ? fastChars : normalChars)}, refs={refs}");
    }

    [ContextMenu("Restart Python TTS")]
    public void RestartPythonProcess()
    {
        StopPython();
        isReady = false;
        isSpeaking = false;
        StartPython();
    }

    void StopPython()
    {
        try
        {
            if (process != null && !process.HasExited)
            {
                SendJson("{\"cmd\":\"quit\"}");
                process.Kill();
            }
        }
        catch { }
    }

    // -------------------------
    // Public API
    // -------------------------
    public void Speak(string text)
    {
        SpeakAs("soldier", text);
    }

    public void SpeakAs(string role, string text)
    {
        StartCoroutine(SpeakRoutine(role, text));
    }

    IEnumerator SpeakRoutine(string role, string text)
    {
        isSpeaking = true;

        int id = nextId++;
        string safeRole = string.IsNullOrWhiteSpace(role) ? "soldier" : Escape(role);
        string json = $"{{\"id\":{id},\"requestType\":\"speak\",\"role\":\"{safeRole}\",\"text\":\"{Escape(text)}\"}}";

        Debug.Log($"[TTS] Sending: {text}");
        SendJson(json);

        float timeout = Time.time + requestTimeoutSeconds;
        Debug.Log($"[TTS] Waiting up to {requestTimeoutSeconds} seconds for Python response...");

        while (Time.time < timeout)
        {
            if (responseMap.TryRemove(id, out string line))
            {
                TTSResponse response;
                try { response = JsonUtility.FromJson<TTSResponse>(line); }
                catch { response = null; }

                if (response == null)
                {
                    Debug.LogWarning("[TTS] Invalid response line for ID " + id + ": " + line);
                }
                else if (response.type == "error")
                {
                    Debug.LogError("[TTS ERROR] " + response.error);
                    ClearSubtitle();
                    isSpeaking = false;
                    yield break;
                }
                else
                {
                    Debug.Log("[TTS] Reply text: " + response.replyText);
                    lastCompletedRequestId = response.id;
                    lastCompletedReplyText = response.replyText ?? string.Empty;
                    lastCompletedRole = role;
                    string fullPath = response.wavPath;

                    yield return PlayWav(fullPath, role, lastCompletedReplyText);

                    isSpeaking = false;
                    yield break;
                }
            }

            DrainQueues();
            yield return null;
        }

        Debug.LogError("TTS timeout.");
        ClearSubtitle();
        isSpeaking = false;
    }

    public IEnumerator GenerateChoiceOptionsRoutine(string prompt, Action<string[]> callback)
    {
        int id = nextId++;
        string json = $"{{\"id\":{id},\"requestType\":\"choices\",\"role\":\"player\",\"text\":\"{Escape(prompt)}\"}}";

        Debug.Log($"[TTS] Sending choice generation request: {prompt}");
        SendJson(json);

        float timeout = Time.time + requestTimeoutSeconds;
        Debug.Log($"[TTS] Waiting up to {requestTimeoutSeconds} seconds for choice response...");

        while (Time.time < timeout)
        {
            if (responseMap.TryRemove(id, out string line))
            {
                TTSResponse response;
                try { response = JsonUtility.FromJson<TTSResponse>(line); }
                catch { response = null; }

                if (response == null)
                {
                    Debug.LogWarning("[TTS] Invalid choice response line for ID " + id + ": " + line);
                }
                else if (response.type == "error")
                {
                    Debug.LogError("[TTS ERROR] " + response.error);
                    callback?.Invoke(null);
                    yield break;
                }
                else if (response.type == "choices_result")
                {
                    Debug.Log("[TTS] Choice options received.");
                    callback?.Invoke(response.choices);
                    yield break;
                }
            }

            DrainQueues();
            yield return null;
        }

        Debug.LogError("TTS choice generation timeout.");
        callback?.Invoke(null);
    }

    public IEnumerator GenerateResponseRoutine(string prompt, Action<string> callback)
    {
        int id = nextId++;
        string json = $"{{\"id\":{id},\"requestType\":\"response\",\"role\":\"soldier\",\"text\":\"{Escape(prompt)}\"}}";

        Debug.Log($"[TTS] Sending response generation request: {prompt}");
        SendJson(json);

        float timeout = Time.time + requestTimeoutSeconds;
        Debug.Log($"[TTS] Waiting up to {requestTimeoutSeconds} seconds for response...");

        while (Time.time < timeout)
        {
            if (responseMap.TryRemove(id, out string line))
            {
                TTSResponse response;
                try { response = JsonUtility.FromJson<TTSResponse>(line); }
                catch { response = null; }

                if (response == null)
                {
                    Debug.LogWarning("[TTS] Invalid response line for ID " + id + ": " + line);
                }
                else if (response.type == "error")
                {
                    Debug.LogError("[TTS ERROR] " + response.error);
                    callback?.Invoke(null);
                    yield break;
                }
                else if (response.type == "response_result")
                {
                    Debug.Log("[TTS] Response received.");
                    callback?.Invoke(response.replyText);
                    yield break;
                }
            }

            DrainQueues();
            yield return null;
        }

        Debug.LogError("TTS response generation timeout.");
        callback?.Invoke(null);
    }

    public IEnumerator GenerateDebateTurnRoutine(DebateTurnRequestData request, Action<DebateTurnResultData> callback)
    {
        if (request == null)
        {
            callback?.Invoke(null);
            yield break;
        }

        int id = nextId++;
        DebateTurnMessage message = new DebateTurnMessage
        {
            id = id,
            requestType = "debate_turn",
            role = string.IsNullOrWhiteSpace(request.role) ? "soldier" : request.role,
            soldierName = request.soldierName ?? "Soldier",
            sceneSummary = request.sceneSummary ?? string.Empty,
            formerIdentity = request.formerIdentity ?? string.Empty,
            militaryRole = request.militaryRole ?? string.Empty,
            warTheater = request.warTheater ?? string.Empty,
            definingTrauma = request.definingTrauma ?? string.Empty,
            triggerStimulus = request.triggerStimulus ?? string.Empty,
            identityFracture = request.identityFracture ?? string.Empty,
            physicalTell = request.physicalTell ?? string.Empty,
            tabooTopic = request.tabooTopic ?? string.Empty,
            round = request.round,
            insanity = request.insanity,
            insanityStage = request.insanityStage ?? string.Empty,
            lastPlayerLine = request.lastPlayerLine ?? string.Empty,
            lastSoldierLine = request.lastSoldierLine ?? string.Empty,
            recentTranscript = request.recentTranscript ?? Array.Empty<string>(),
        };

        SendJson(JsonUtility.ToJson(message));

        float timeout = Time.time + requestTimeoutSeconds;
        Debug.Log($"[TTS] Waiting up to {requestTimeoutSeconds} seconds for debate turn...");

        while (Time.time < timeout)
        {
            if (responseMap.TryRemove(id, out string line))
            {
                TTSResponse response;
                try { response = JsonUtility.FromJson<TTSResponse>(line); }
                catch { response = null; }

                if (response == null)
                {
                    Debug.LogWarning("[TTS] Invalid debate response line for ID " + id + ": " + line);
                }
                else if (response.type == "error")
                {
                    Debug.LogError("[TTS ERROR] " + response.error);
                    callback?.Invoke(null);
                    yield break;
                }
                else if (response.type == "debate_turn_result")
                {
                    DebateTurnResultData result = new DebateTurnResultData
                    {
                        soldierReply = string.IsNullOrWhiteSpace(response.soldierReply) ? response.replyText : response.soldierReply,
                        wavPath = response.wavPath ?? string.Empty,
                        insanityStage = response.insanityStage ?? string.Empty,
                        breakReason = response.breakReason ?? string.Empty,
                        temperatureUsed = response.temperatureUsed,
                        peakInsanity = response.peakInsanity,
                        debateChoices = response.debateChoices,
                    };

                    callback?.Invoke(result);
                    yield break;
                }
            }

            DrainQueues();
            yield return null;
        }

        Debug.LogError("TTS debate turn generation timeout.");
        callback?.Invoke(null);
    }

    public IEnumerator GenerateTypedConversationTurnRoutine(TypedConversationRequestData request, Action<TypedConversationResultData> callback)
    {
        if (request == null)
        {
            callback?.Invoke(null);
            yield break;
        }

        int id = nextId++;
        TypedConversationMessage message = new TypedConversationMessage
        {
            id = id,
            requestType = "typed_turn",
            role = string.IsNullOrWhiteSpace(request.role) ? "soldier" : request.role,
            speakerName = request.speakerName ?? "Speaker",
            sceneSummary = request.sceneSummary ?? string.Empty,
            formerIdentity = request.formerIdentity ?? string.Empty,
            militaryRole = request.militaryRole ?? string.Empty,
            warTheater = request.warTheater ?? string.Empty,
            definingTrauma = request.definingTrauma ?? string.Empty,
            triggerStimulus = request.triggerStimulus ?? string.Empty,
            identityFracture = request.identityFracture ?? string.Empty,
            physicalTell = request.physicalTell ?? string.Empty,
            tabooTopic = request.tabooTopic ?? string.Empty,
            round = request.round,
            instability = request.instability,
            stage = request.stage ?? string.Empty,
            requiredWord = request.requiredWord ?? string.Empty,
            playerTypedLine = request.playerTypedLine ?? string.Empty,
            offeredWords = request.offeredWords ?? Array.Empty<string>(),
            detectedTags = request.detectedTags ?? Array.Empty<string>(),
            recentTranscript = request.recentTranscript ?? Array.Empty<string>(),
        };

        SendJson(JsonUtility.ToJson(message));

        float timeout = Time.time + requestTimeoutSeconds;
        Debug.Log($"[TTS] Waiting up to {requestTimeoutSeconds} seconds for typed turn...");

        while (Time.time < timeout)
        {
            if (responseMap.TryRemove(id, out string line))
            {
                TTSResponse response;
                try { response = JsonUtility.FromJson<TTSResponse>(line); }
                catch { response = null; }

                if (response == null)
                {
                    Debug.LogWarning("[TTS] Invalid typed response line for ID " + id + ": " + line);
                }
                else if (response.type == "error")
                {
                    Debug.LogError("[TTS ERROR] " + response.error);
                    callback?.Invoke(null);
                    yield break;
                }
                else if (response.type == "typed_turn_result")
                {
                    TypedConversationResultData result = new TypedConversationResultData
                    {
                        speakerReply = string.IsNullOrWhiteSpace(response.speakerReply) ? response.replyText : response.speakerReply,
                        wavPath = response.wavPath ?? string.Empty,
                        stateHint = response.stateHint ?? string.Empty,
                        backstoryReveal = response.backstoryReveal ?? string.Empty,
                        suggestedWords = response.suggestedWords ?? Array.Empty<string>(),
                        temperatureUsed = response.temperatureUsed,
                        stage = response.stage ?? string.Empty,
                    };

                    callback?.Invoke(result);
                    yield break;
                }
            }

            DrainQueues();
            yield return null;
        }

        Debug.LogError("TTS typed turn generation timeout.");
        callback?.Invoke(null);
    }

    // -------------------------
    // Helpers
    // -------------------------
    void SendJson(string json)
    {
        if (process == null || process.HasExited)
        {
            Debug.LogError("Python process not running.");
            return;
        }

        process.StandardInput.WriteLine(json);
        process.StandardInput.Flush();
    }

    void DrainQueues()
    {
        while (stdoutQueue.TryDequeue(out string line))
        {
            Debug.Log("[PY STDOUT] " + line);

            // Try READY first
            if (TryHandleReady(line)) continue;

            // If it's a result message, store it for the waiting coroutine
            if (line.Contains("\"type\""))
            {
                try
                {
                    var response = JsonUtility.FromJson<TTSResponse>(line);
                    if (response != null && (response.type == "result" || response.type == "choices_result" || response.type == "response_result" || response.type == "debate_turn_result" || response.type == "typed_turn_result" || response.type == "error"))
                        responseMap[response.id] = line;
                }
                catch { }
            }
        }

        DrainStderr();
    }

    void DrainStderr()
    {
        while (stderrQueue.TryDequeue(out string err))
            Debug.LogWarning("[PY STDERR] " + err);
    }

    bool TryHandleReady(string line)
    {
        if (!line.Contains("\"type\"")) return false;

        try
        {
            var msg = JsonUtility.FromJson<TTSReady>(line);
            if (msg != null && msg.type == "ready")
            {
                isReady = true;
                sampleRate = msg.sampleRate;
                Debug.Log($"TTS READY (sr={sampleRate})");
                return true;
            }
        }
        catch { }

        return false;
    }

    public IEnumerator PlayWav(string path, string role, string replyText)
    {
        Debug.Log("[TTS] Loading WAV: " + path);

        if (!File.Exists(path))
        {
            Debug.LogError("[TTS] File missing: " + path);
            ClearSubtitle();
            yield break;
        }

        string url = "file:///" + path.Replace("\\", "/");

        using var req = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("[TTS] Load failed: " + req.error);
            ClearSubtitle();
            yield break;
        }

        var clip = DownloadHandlerAudioClip.GetContent(req);

        if (clip == null)
        {
            Debug.LogError("[TTS] Clip is null!");
            ClearSubtitle();
            yield break;
        }

        Debug.Log($"[TTS] Clip loaded: {clip.length}s");

        audioSource.clip = clip;
        ShowSubtitle(role, replyText, clip.length);
        audioSource.Play();

        Debug.Log("[TTS] Playing audio...");

        while (audioSource.isPlaying)
            yield return null;

        Debug.Log("[TTS] Playback finished");
        ClearSubtitle();

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                Debug.Log("[TTS] Deleted temp WAV: " + path);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[TTS] Failed to delete WAV: " + ex.Message);
        }
    }

    string Escape(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private void EnsureSubtitleUi()
    {
        if (!enableSubtitles)
            return;

        if (subtitleSpeakerText != null && subtitleBodyText != null)
        {
            ApplySubtitleAppearance();
            subtitleSpeakerText.gameObject.SetActive(false);
            subtitleBodyText.gameObject.SetActive(false);
            subtitleSpeakerText.text = string.Empty;
            subtitleBodyText.text = string.Empty;
            return;
        }

        if (runtimeSubtitlePanel != null && subtitleSpeakerText != null && subtitleBodyText != null)
            return;

        Canvas existingCanvas = FindObjectOfType<Canvas>();
        if (existingCanvas == null)
        {
            runtimeSubtitleCanvas = new GameObject("TTS Subtitle Canvas");
            existingCanvas = runtimeSubtitleCanvas.AddComponent<Canvas>();
            existingCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            runtimeSubtitleCanvas.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            runtimeSubtitleCanvas.AddComponent<GraphicRaycaster>();
        }

        runtimeSubtitlePanel = new GameObject("TTS Subtitle Panel");
        runtimeSubtitlePanel.transform.SetParent(existingCanvas.transform, false);

        RectTransform panelRect = runtimeSubtitlePanel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.08f, 0f);
        panelRect.anchorMax = new Vector2(0.92f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, subtitlePadding);
        panelRect.sizeDelta = new Vector2(0f, 132f);

        Image panelImage = runtimeSubtitlePanel.AddComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 0.62f);

        VerticalLayoutGroup layout = runtimeSubtitlePanel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 18, 18);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        ContentSizeFitter fitter = runtimeSubtitlePanel.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        subtitleSpeakerText = CreateSubtitleText("Speaker", runtimeSubtitlePanel.transform, subtitleSpeakerFontSize, FontStyles.Bold);
        subtitleBodyText = CreateSubtitleText("Body", runtimeSubtitlePanel.transform, subtitleBodyFontSize, FontStyles.Normal);
        subtitleBodyText.enableWordWrapping = true;
        ApplySubtitleAppearance();

        subtitleSpeakerText.gameObject.SetActive(false);
        subtitleBodyText.gameObject.SetActive(false);
        runtimeSubtitlePanel.SetActive(false);
    }

    private TextMeshProUGUI CreateSubtitleText(string objectName, Transform parent, float fontSize, FontStyles fontStyle)
    {
        GameObject textObject = new("TTS Subtitle " + objectName);
        textObject.transform.SetParent(parent, false);

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.BottomLeft;
        text.text = string.Empty;

        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = Vector2.zero;

        LayoutElement layoutElement = textObject.AddComponent<LayoutElement>();
        layoutElement.minHeight = fontSize + 8f;

        return text;
    }

    private void ShowSubtitle(string role, string replyText, float clipLength)
    {
        if (!enableSubtitles)
            return;

        EnsureSubtitleUi();
        if (subtitleSpeakerText == null || subtitleBodyText == null)
            return;

        string cleanText = string.IsNullOrWhiteSpace(replyText) ? string.Empty : TrimSubtitleText(replyText);
        if (string.IsNullOrEmpty(cleanText))
        {
            ClearSubtitle();
            return;
        }

        if (subtitleCoroutine != null)
            StopCoroutine(subtitleCoroutine);

        if (runtimeSubtitlePanel != null)
            runtimeSubtitlePanel.SetActive(true);

        subtitleSpeakerText.gameObject.SetActive(true);
        subtitleBodyText.gameObject.SetActive(true);
        ApplySubtitleAppearance();
        subtitleSpeakerText.text = GetSpeakerDisplayName(role);
        subtitleBodyText.text = string.Empty;

        subtitleCoroutine = StartCoroutine(TypeSubtitleRoutine(cleanText, clipLength));
    }

    private IEnumerator TypeSubtitleRoutine(string fullText, float clipLength)
    {
        if (!typewriterSubtitles)
        {
            subtitleBodyText.text = fullText;
            subtitleCoroutine = null;
            yield break;
        }

        float safeClipLength = Mathf.Max(0.01f, clipLength);
        float charactersPerSecondFromClip = fullText.Length / safeClipLength;
        float charactersPerSecond = Mathf.Max(minimumSubtitleCharactersPerSecond, charactersPerSecondFromClip);
        float visibleCharacters = 0f;

        while (visibleCharacters < fullText.Length)
        {
            visibleCharacters += charactersPerSecond * Time.deltaTime;
            int count = Mathf.Clamp(Mathf.FloorToInt(visibleCharacters), 0, fullText.Length);
            subtitleBodyText.text = fullText.Substring(0, count);
            yield return null;
        }

        subtitleBodyText.text = fullText;
        subtitleCoroutine = null;
    }

    private void ClearSubtitle()
    {
        if (subtitleCoroutine != null)
        {
            StopCoroutine(subtitleCoroutine);
            subtitleCoroutine = null;
        }

        if (subtitleSpeakerText != null)
        {
            subtitleSpeakerText.text = string.Empty;
            subtitleSpeakerText.gameObject.SetActive(false);
        }

        if (subtitleBodyText != null)
        {
            subtitleBodyText.text = string.Empty;
            subtitleBodyText.gameObject.SetActive(false);
        }

        if (runtimeSubtitlePanel != null)
            runtimeSubtitlePanel.SetActive(false);
    }

    private string GetSpeakerDisplayName(string role)
    {
        string safeRole = string.IsNullOrWhiteSpace(role) ? "speaker" : role.Trim();

        if (speakerDisplayNames != null)
        {
            for (int i = 0; i < speakerDisplayNames.Length; i++)
            {
                SpeakerDisplayName entry = speakerDisplayNames[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.role)) continue;

                if (string.Equals(entry.role.Trim(), safeRole, StringComparison.OrdinalIgnoreCase))
                    return string.IsNullOrWhiteSpace(entry.displayName) ? HumanizeRoleName(safeRole) : entry.displayName.Trim();
            }
        }

        return HumanizeRoleName(safeRole);
    }

    private string HumanizeRoleName(string role)
    {
        string normalized = role.Replace("_", " ").Replace("-", " ").Trim();
        if (string.IsNullOrEmpty(normalized))
            return "Speaker";

        string[] parts = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string lower = parts[i].ToLowerInvariant();
            parts[i] = char.ToUpperInvariant(lower[0]) + lower.Substring(1);
        }

        return string.Join(" ", parts);
    }

    private void ApplySubtitleAppearance()
    {
        if (subtitleSpeakerText != null)
        {
            subtitleSpeakerText.fontSize = Mathf.Max(8f, subtitleSpeakerFontSize);

            LayoutElement speakerLayout = subtitleSpeakerText.GetComponent<LayoutElement>();
            if (speakerLayout != null)
                speakerLayout.minHeight = subtitleSpeakerText.fontSize + 8f;
        }

        if (subtitleBodyText != null)
        {
            subtitleBodyText.fontSize = Mathf.Max(8f, subtitleBodyFontSize);

            LayoutElement bodyLayout = subtitleBodyText.GetComponent<LayoutElement>();
            if (bodyLayout != null)
                bodyLayout.minHeight = subtitleBodyText.fontSize + 8f;
        }
    }

    private string TrimSubtitleText(string replyText)
    {
        string cleanText = string.IsNullOrWhiteSpace(replyText) ? string.Empty : replyText.Trim();
        int safeMaxCharacters = Mathf.Max(20, maxSubtitleCharacters);

        if (cleanText.Length <= safeMaxCharacters)
            return cleanText;

        int cutoff = cleanText.LastIndexOf(' ', safeMaxCharacters);
        if (cutoff < safeMaxCharacters / 2)
            cutoff = safeMaxCharacters;

        return cleanText.Substring(0, cutoff).TrimEnd() + "...";
    }

    // -------------------------
    // JSON Structs
    // -------------------------
    [Serializable]
    class TTSReady
    {
        public string type;
        public int sampleRate;
    }

    [Serializable]
    class TTSResponse
    {
        public string type;
        public int id;
        public string wavPath;
        public string replyText;
        public string soldierReply;
        public string speakerReply;
        public string error;
        public string[] choices;
        public string insanityStage;
        public string breakReason;
        public string stateHint;
        public string backstoryReveal;
        public string stage;
        public float temperatureUsed;
        public bool peakInsanity;
        public DebateChoiceData[] debateChoices;
        public string[] suggestedWords;
    }

    [Serializable]
    class DebateTurnMessage
    {
        public int id;
        public string requestType;
        public string role;
        public string soldierName;
        public string sceneSummary;
        public string formerIdentity;
        public string militaryRole;
        public string warTheater;
        public string definingTrauma;
        public string triggerStimulus;
        public string identityFracture;
        public string physicalTell;
        public string tabooTopic;
        public int round;
        public int insanity;
        public string insanityStage;
        public string lastPlayerLine;
        public string lastSoldierLine;
        public string[] recentTranscript;
    }

    [Serializable]
    class TypedConversationMessage
    {
        public int id;
        public string requestType;
        public string role;
        public string speakerName;
        public string sceneSummary;
        public string formerIdentity;
        public string militaryRole;
        public string warTheater;
        public string definingTrauma;
        public string triggerStimulus;
        public string identityFracture;
        public string physicalTell;
        public string tabooTopic;
        public int round;
        public int instability;
        public string stage;
        public string requiredWord;
        public string playerTypedLine;
        public string[] offeredWords;
        public string[] detectedTags;
        public string[] recentTranscript;
    }
}

