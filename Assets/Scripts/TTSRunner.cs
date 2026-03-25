using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Debug = UnityEngine.Debug;

public class TTSRunner : MonoBehaviour
{
    // ✅ GLOBAL ACCESS
    public static TTSRunner Instance;

    [Header("Audio")]
    [SerializeField] AudioSource audioSource;

    string ttsRoot;
    string pythonPath;
    string scriptPath;

    Process ttsProcess;

    readonly ConcurrentQueue<string> stdoutLines = new();
    readonly ConcurrentQueue<string> stderrLines = new();
    readonly object stdinLock = new();

    readonly Queue<string> requestQueue = new();
    bool isProcessingQueue;

    // ----------------------------------------

    void Awake()
    {
        // Singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // AudioSource safety
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
    }

    void Start()
    {
        ttsRoot = Path.Combine(Application.streamingAssetsPath, "TTS");
        pythonPath = Path.Combine(ttsRoot, ".venv", "Scripts", "python.exe");
        scriptPath = Path.Combine(ttsRoot, "tts_unity_live.py");

        ValidatePaths();

        StartTtsProcess();
    }

    // ----------------------------------------
    // 🧠 PUBLIC API
    // ----------------------------------------

    public void Speak(string text, string character = "narrator")
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Debug.LogWarning("TTS: Empty text ignored.");
            return;
        }

        requestQueue.Enqueue($"{character}:{text}");

        if (!isProcessingQueue)
        {
            isProcessingQueue = true;
            StartCoroutine(ProcessQueue());
        }
    }

    // ----------------------------------------

    IEnumerator ProcessQueue()
    {
        while (requestQueue.Count > 0)
        {
            string line = requestQueue.Dequeue();
            yield return SpeakInternal(line);
        }

        isProcessingQueue = false;
    }

    IEnumerator SpeakInternal(string formattedLine)
    {
        if (!EnsureProcessRunning())
        {
            Debug.LogError("TTS: Process not running.");
            yield break;
        }

        // Send to Python
        try
        {
            lock (stdinLock)
            {
                ttsProcess.StandardInput.WriteLine(formattedLine);
                ttsProcess.StandardInput.Flush();
            }
        }
        catch (Exception e)
        {
            Debug.LogError("TTS write failed: " + e.Message);
            yield break;
        }

        // Wait for "ready"
        float timeout = Time.realtimeSinceStartup + 120f;

        while (Time.realtimeSinceStartup < timeout)
        {
            DrainStderr();

            while (stdoutLines.TryDequeue(out string line))
            {
                if (line.Trim().ToLower() == "ready")
                {
                    string wavPath = Path.Combine(ttsRoot, "output.wav");
                    yield return PlayWav(wavPath);
                    yield break;
                }

                // Debug unexpected output
                Debug.Log("[PYTHON STDOUT] " + line);
            }

            if (ttsProcess == null || ttsProcess.HasExited)
            {
                Debug.LogError("TTS process died during request.");
                yield break;
            }

            yield return null;
        }

        Debug.LogError("TTS request timed out.");
    }

    // ----------------------------------------
    // 🧠 PROCESS MANAGEMENT
    // ----------------------------------------

    void StartTtsProcess()
    {
        if (!EnsureProcessPaths()) return;

        try
        {
            ProcessStartInfo start = new()
            {
                FileName = pythonPath,
                Arguments = $"-u \"{scriptPath}\"",
                WorkingDirectory = ttsRoot,

                UseShellExecute = false,
                CreateNoWindow = true,

                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            ttsProcess = new Process
            {
                StartInfo = start,
                EnableRaisingEvents = true
            };

            ttsProcess.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    stdoutLines.Enqueue(e.Data);
            };

            ttsProcess.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    stderrLines.Enqueue(e.Data);
            };

            ttsProcess.Exited += (_, _) =>
            {
                Debug.LogError("TTS process exited.");
            };

            ttsProcess.Start();
            ttsProcess.BeginOutputReadLine();
            ttsProcess.BeginErrorReadLine();

            Debug.Log("TTS process started.");
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to start TTS process: " + e.Message);
        }
    }

    bool EnsureProcessRunning()
    {
        if (ttsProcess != null && !ttsProcess.HasExited)
            return true;

        Debug.LogWarning("TTS restarting process...");
        StartTtsProcess();

        return ttsProcess != null && !ttsProcess.HasExited;
    }

    // ----------------------------------------
    // 📁 PATH VALIDATION
    // ----------------------------------------

    void ValidatePaths()
    {
        Debug.Log("TTS Root: " + ttsRoot);
        Debug.Log("Python Path: " + pythonPath);
        Debug.Log("Script Path: " + scriptPath);
    }

    bool EnsureProcessPaths()
    {
        if (!File.Exists(pythonPath))
        {
            Debug.LogError("Python not found: " + pythonPath);
            return false;
        }

        if (!File.Exists(scriptPath))
        {
            Debug.LogError("Script not found: " + scriptPath);
            return false;
        }

        return true;
    }

    // ----------------------------------------
    // 🔊 AUDIO
    // ----------------------------------------

    IEnumerator PlayWav(string path)
    {
        if (!File.Exists(path))
        {
            Debug.LogError("WAV missing: " + path);
            yield break;
        }

        string uri = new Uri(path).AbsoluteUri;

        using UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.WAV);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Audio load failed: " + req.error);
            yield break;
        }

        AudioClip clip = DownloadHandlerAudioClip.GetContent(req);

        if (clip == null)
        {
            Debug.LogError("Clip decode failed.");
            yield break;
        }

        audioSource.clip = clip;
        audioSource.Play();

        while (audioSource.isPlaying)
            yield return null;
    }

    // ----------------------------------------
    // 🧹 CLEANUP
    // ----------------------------------------

    void DrainStderr()
    {
        while (stderrLines.TryDequeue(out string line))
            Debug.LogError("[PYTHON ERROR] " + line);
    }

    void OnDestroy()
    {
        try
        {
            if (ttsProcess != null && !ttsProcess.HasExited)
            {
                ttsProcess.Kill();
                Debug.Log("TTS process killed.");
            }
        }
        catch { }
    }
}