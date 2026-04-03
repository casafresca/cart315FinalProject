using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using Debug = UnityEngine.Debug;

public class TTSRunner : MonoBehaviour
{
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

    readonly ConcurrentQueue<string> resultQueue = new ConcurrentQueue<string>();

    private Process process;

    private readonly ConcurrentQueue<string> stdoutQueue = new();
    private readonly ConcurrentQueue<string> stderrQueue = new();

    private bool isReady = false;
    private int nextId = 1;
    private bool isSpeaking = false;

    // Read-only state exposed for other gameplay visuals (e.g., talking sprites).
    public bool IsSpeaking => isSpeaking;
    public bool IsReady => isReady;

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

        StartPython();
    }

    void Update()
    {
        if (Input.GetKeyDown(triggerKey))
        {
            Debug.Log("[TTS] E pressed");

            if (!isReady)
            {
                Debug.LogWarning("[TTS] Not ready yet!");
            }
            else if (isSpeaking)
            {
                Debug.LogWarning("[TTS] Already speaking!");
            }
            else
            {
                Speak(testLine);
            }
        }

        DrainQueues();
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        StopPython();
    }

    // -------------------------
    // Python Process
    // -------------------------
    void StartPython()
    {
        Debug.Log("[TTS] Launching Python process...");

        if (!File.Exists(scriptPath))
        {
            Debug.LogError("[TTS] Script not found: " + scriptPath);
            return;
        }

        Debug.Log($"[TTS] EXE: {pythonExe}");
        Debug.Log($"[TTS] ARGS: -u \"{scriptPath}\" --out-dir \"{wavDir}\"");
        Debug.Log($"[TTS] WorkingDir: {ttsRoot}");
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

        Debug.Log($"[TTS] AI Speed Mode: {(fastReplyMode ? "FAST" : "NORMAL")}, chars={ (fastReplyMode ? fastChars : normalChars) }, refs={refs}");
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
        string json = $"{{\"id\":{id},\"role\":\"{safeRole}\",\"text\":\"{Escape(text)}\"}}";

        Debug.Log($"[TTS] Sending: {text}");
        SendJson(json);

        float timeout = Time.time + requestTimeoutSeconds;
        Debug.Log($"[TTS] Waiting up to {requestTimeoutSeconds} seconds for Python response...");

        while (Time.time < timeout)
        {
            while (resultQueue.TryDequeue(out string line))
            {
                Debug.Log("[PY STDOUT] " + line);

                if (TryHandleReady(line)) continue;

                if (!line.Contains("\"type\"")) continue;

                TTSResponse response;

                Debug.Log($"[TTS] Processing response for ID {id}...");

                try
                {
                    response = JsonUtility.FromJson<TTSResponse>(line);
                }
                catch
                {
                    continue;
                }

                if (response == null || response.id != id)
                {
                    Debug.LogWarning("[TTS] Ignoring unrelated message: " + line);
                    continue;
                }

                if (response.type == "error")
                {
                    Debug.LogError("[TTS ERROR] " + response.error);
                    isSpeaking = false;
                    yield break;
                }

                Debug.Log("[TTS] Reply text: " + response.replyText);
                string fullPath = response.wavPath;

                yield return PlayWav(fullPath);

                isSpeaking = false;
                yield break;
            }

            DrainQueues();
            yield return null;
        }

        Debug.LogError("TTS timeout.");
        isSpeaking = false;
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
            if (TryHandleReady(line))
                continue;

            // If it's a result message, store it for SpeakRoutine
            if (line.Contains("\"type\""))
            {
                try
                {
                    var response = JsonUtility.FromJson<TTSResponse>(line);
                    if (response != null && (response.type == "result" || response.type == "error"))
                    {
                        resultQueue.Enqueue(line);
                    }
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

    IEnumerator PlayWav(string path)
    {
        Debug.Log("[TTS] Loading WAV: " + path);

        if (!File.Exists(path))
        {
            Debug.LogError("[TTS] File missing: " + path);
            yield break;
        }

        string url = "file:///" + path.Replace("\\", "/");

        using var req = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("[TTS] Load failed: " + req.error);
            yield break;
        }

        var clip = DownloadHandlerAudioClip.GetContent(req);

        if (clip == null)
        {
            Debug.LogError("[TTS] Clip is null!");
            yield break;
        }

        Debug.Log($"[TTS] Clip loaded: {clip.length}s");

        audioSource.clip = clip;
        audioSource.Play();

        Debug.Log("[TTS] Playing audio...");

        while (audioSource.isPlaying)
            yield return null;

        Debug.Log("[TTS] Playback finished");

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
        public string error;
    }
}

