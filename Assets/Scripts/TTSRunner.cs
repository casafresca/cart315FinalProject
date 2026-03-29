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
    // ✅ GLOBAL ACCESS
    public static TTSRunner Instance;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;

    [Header("Input")]
    [SerializeField] private KeyCode triggerKey = KeyCode.E;
    [SerializeField] private string testLine = "Hello from Unity!";

    [Header("Paths")]
    [SerializeField] private string pythonExe = "python"; // Override in inspector if not in PATH
    private string ttsRoot;
    private string scriptPath;
    private string wavDir;

    readonly ConcurrentQueue<string> resultQueue = new ConcurrentQueue<string>();


    private Process process;

    private readonly ConcurrentQueue<string> stdoutQueue = new();
    private readonly ConcurrentQueue<string> stderrQueue = new();

    private bool isReady = false;
    private int nextId = 1;
    private bool isSpeaking = false;

    private int sampleRate = 24000;

    // -------------------------
    // Unity Lifecycle
    // -------------------------
    void Awake()
    {
        if (!audioSource)
            audioSource = gameObject.AddComponent<AudioSource>();
    }

    void Start()
    {

        Debug.Log("[TTS] Start() called on line 54 of TTSRunner.cs");

        ttsRoot = Path.Combine(Application.streamingAssetsPath, "TTS");
        scriptPath = Path.Combine(ttsRoot, "tts_cli_player_basicv3.py");
        pythonExe = Path.Combine(ttsRoot, ".venv", "Scripts", "python.exe");

        Debug.Log($"[TTS] Root: {ttsRoot} on line 60 of TTSRunner.cs");
        Debug.Log($"[TTS] Script: {scriptPath} on line 61 of TTSRunner.cs");

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
        StopPython();
    }

    // -------------------------
    // Python Process
    // -------------------------
    void StartPython()
    {
        Debug.Log("[TTS] Launching Python process... on line 89 of TTSRunner.cs");

        Debug.Log($"[TTS] EXE: {pythonExe} on line 91 of TTSRunner.cs");
        Debug.Log($"[TTS] ARGS: -u \"{scriptPath}\" --out-dir \"{wavDir}\" on line 92 of TTSRunner.cs");
        Debug.Log($"[TTS] WorkingDir: {ttsRoot} on line 93 of TTSRunner.cs");
        if (process != null && !process.HasExited) return;

        Debug.Log("Starting Python TTS... on line 97 of TTSRunner.cs");

        var psi = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = $"-u \"{scriptPath}\"",
            WorkingDirectory = ttsRoot,

            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

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

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
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
        StartCoroutine(SpeakRoutine(text));
    }

    IEnumerator SpeakRoutine(string text)
    {
        isSpeaking = true;

        int id = nextId++;

        string json = $"{{\"id\":{id},\"text\":\"{Escape(text)}\"}}";

        Debug.Log($"[TTS] Sending: {text} on line 120 of TTSRunner.cs");
        SendJson(json);

        float timeout = Time.time + 30f;

        while (Time.time < timeout)
        {
            while (resultQueue.TryDequeue(out string line))
            {
                Debug.Log("[PY STDOUT] " + line + " on line 130 of TTSRunner.cs");

                if (TryHandleReady(line)) continue;

                if (!line.Contains("\"type\"")) continue;

                TTSResponse response;

                Debug.Log($"[TTS] Processing response for ID {id}... on line 141 of TTSRunner.cs");

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
                    Debug.LogWarning("[TTS] Ignoring unrelated message: " + line + " on line 150 of TTSRunner.cs");
                    continue;
                }

                if (response.type == "error")
                {
                    Debug.LogError("[TTS ERROR] " + response.error + " on line 158 of TTSRunner.cs");
                    isSpeaking = false;
                    yield break;
                }

                string fullPath = response.wavPath;

                // ✅ yield OUTSIDE try/catch
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
            Debug.Log("[PY STDOUT] " + line + " on line 165 of TTSRunner.cs");

            // Try READY first
            if (TryHandleReady(line))
                continue;

            // If it's a result message, store it for SpeakRoutine
            if (line.Contains("\"type\""))
            {
                try
                {
                    var response = JsonUtility.FromJson<TTSResponse>(line);
                    if (response != null && response.type == "result")
                    {
                        resultQueue.Enqueue(line);
                    }
                }
                catch { }
            }
        }
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
        public string error;
    }
}