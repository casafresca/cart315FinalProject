using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Debug = UnityEngine.Debug;

public class TTSRunner : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] AudioSource audioSource;

    [Header("TTS Process")]
    [SerializeField] bool warmupOnStart = true;
    [SerializeField] bool cacheVoiceLatents = true;

    string ttsRoot;
    string pythonPath;
    string wavCacheDir;

    Process ttsProcess;
    readonly ConcurrentQueue<string> stdoutLines = new();
    readonly ConcurrentQueue<string> stderrLines = new();
    readonly object stdinLock = new();
    readonly Queue<string> pendingTexts = new();
    int nextRequestId = 1;
    bool isReady;
    bool isProcessingQueue;
    int outputSampleRate = 24000;

    [Serializable]
    class TTSReadyMessage
    {
        public string type;
        public int sampleRate;
    }

    [Serializable]
    class TTSRequestMessage
    {
        public int id;
        public string text;
        public string cmd;
    }

    [Serializable]
    class TTSResponseMessage
    {
        public int id;
        public string type;
        public string wavPath;
        public bool cached;
        public int sampleRate;
        public int elapsedMs;
        public string error;
    }

    void Awake()
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
    }

    void Start()
    {
        ttsRoot = Path.Combine(Application.streamingAssetsPath, "TTS");
        pythonPath = Path.Combine(ttsRoot, ".venv", "Scripts", "python.exe");
        wavCacheDir = Path.Combine(ttsRoot, "wavs");

        // Check if virtual environment exists
        if (!File.Exists(pythonPath))
        {
            Debug.Log("TTS environment missing. Running setup.ps1...");
            RunSetup();
        }

        StartTtsServer();
        Speak("Hello from Unity.");
    }

    void RunSetup()
    {
        ProcessStartInfo start = new()
        {
            FileName = "powershell.exe",
            Arguments = "-ExecutionPolicy Bypass -File setup.ps1",

            WorkingDirectory = ttsRoot,

            UseShellExecute = false,
            CreateNoWindow = false
        };

        Process process = Process.Start(start);
        process.WaitForExit();
    }

    void StartTtsServer()
    {
        if (ttsProcess != null && !ttsProcess.HasExited) return;

        isReady = false;
        DrainStdout();
        DrainStderr();

        string scriptPath = Path.Combine(ttsRoot, "tts_cli_player_basicv2.py");
        string args = $"-u \"{scriptPath}\" --stdio --out-dir \"{wavCacheDir}\"";
        if (warmupOnStart)
            args += " --warmup";
        if (!cacheVoiceLatents)
            args += " --no-voice-cache";

        ProcessStartInfo start = new()
        {
            FileName = pythonPath,
            Arguments = args,
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
            if (!string.IsNullOrWhiteSpace(e.Data)) stdoutLines.Enqueue(e.Data);
        };

        ttsProcess.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) stderrLines.Enqueue(e.Data);
        };

        ttsProcess.Exited += (_, _) =>
        {
            isReady = false;
            Debug.LogError("TTS process exited.");
        };

        ttsProcess.Start();
        ttsProcess.BeginOutputReadLine();
        ttsProcess.BeginErrorReadLine();
    }

    public void Speak(string text)
    {
        pendingTexts.Enqueue(text);
        if (!isProcessingQueue)
        {
            isProcessingQueue = true;
            StartCoroutine(ProcessQueue());
        }
    }

    System.Collections.IEnumerator ProcessQueue()
    {
        while (pendingTexts.Count > 0)
        {
            string text = pendingTexts.Dequeue();
            yield return SpeakOnce(text);
        }
        isProcessingQueue = false;
    }

    System.Collections.IEnumerator SpeakOnce(string text)
    {
        if (ttsProcess == null || ttsProcess.HasExited) StartTtsServer();

        yield return WaitForReady();
        if (!isReady)
        {
            Debug.LogError("TTS server not ready.");
            yield break;
        }

        int requestId = nextRequestId++;
        TTSRequestMessage request = new()
        {
            id = requestId,
            text = text
        };

        string json = JsonUtility.ToJson(request);
        lock (stdinLock)
        {
            ttsProcess.StandardInput.WriteLine(json);
            ttsProcess.StandardInput.Flush();
        }

        float timeoutAt = Time.realtimeSinceStartup + 300f;
        while (Time.realtimeSinceStartup < timeoutAt)
        {
            DrainStderr();

            while (stdoutLines.TryDequeue(out string line))
            {
                if (TryHandleReady(line)) continue;

                TTSResponseMessage response;
                try { response = JsonUtility.FromJson<TTSResponseMessage>(line); }
                catch { continue; }

                if (response == null || response.id != requestId)
                    continue;

                if (response.type == "error")
                {
                    Debug.LogError($"TTS error: {response.error}");
                    yield break;
                }

                if (response.type != "result" || string.IsNullOrWhiteSpace(response.wavPath))
                {
                    Debug.LogError($"Unexpected TTS response: {line}");
                    yield break;
                }

                outputSampleRate = response.sampleRate > 0 ? response.sampleRate : outputSampleRate;
                yield return PlayWavFile(response.wavPath);
                yield break;
            }

            if (ttsProcess == null || ttsProcess.HasExited)
            {
                Debug.LogError("TTS process died while waiting for a response.");
                yield break;
            }

            yield return null;
        }

        Debug.LogError("Timed out waiting for TTS response.");
    }

    System.Collections.IEnumerator WaitForReady()
    {
        if (isReady) yield break;

        float timeoutAt = Time.realtimeSinceStartup + 600f;
        while (Time.realtimeSinceStartup < timeoutAt)
        {
            DrainStderr();

            while (stdoutLines.TryDequeue(out string line))
            {
                if (TryHandleReady(line)) yield break;
            }

            if (ttsProcess == null || ttsProcess.HasExited) yield break;

            yield return null;
        }
    }

    bool TryHandleReady(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.Contains("\"type\"")) return false;

        TTSReadyMessage ready;
        try { ready = JsonUtility.FromJson<TTSReadyMessage>(line); }
        catch { return false; }

        if (ready != null && ready.type == "ready")
        {
            isReady = true;
            if (ready.sampleRate > 0) outputSampleRate = ready.sampleRate;
            Debug.Log($"TTS ready (sampleRate={outputSampleRate}).");
            return true;
        }

        return false;
    }

    void DrainStderr()
    {
        while (stderrLines.TryDequeue(out string errLine))
            Debug.Log(errLine);
    }

    void DrainStdout()
    {
        while (stdoutLines.TryDequeue(out _))
        {
        }
    }

    System.Collections.IEnumerator PlayWavFile(string wavPath)
    {
        if (!File.Exists(wavPath))
        {
            Debug.LogError($"WAV not found: {wavPath}");
            yield break;
        }

        string uri = new Uri(wavPath).AbsoluteUri;
        using UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.WAV);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"Failed to load WAV: {req.error}");
            yield break;
        }

        AudioClip clip = DownloadHandlerAudioClip.GetContent(req);
        if (clip == null)
        {
            Debug.LogError("Failed to decode WAV into AudioClip.");
            yield break;
        }

        audioSource.clip = clip;
        audioSource.Play();
        while (audioSource.isPlaying) yield return null;
    }

    void OnDestroy()
    {
        try
        {
            if (ttsProcess != null && !ttsProcess.HasExited)
            {
                TTSRequestMessage shutdown = new() { id = 0, cmd = "quit" };
                string json = JsonUtility.ToJson(shutdown);
                lock (stdinLock)
                {
                    ttsProcess.StandardInput.WriteLine(json);
                    ttsProcess.StandardInput.Flush();
                }

                if (!ttsProcess.WaitForExit(1500)) ttsProcess.Kill();
            }
        }
        catch
        {
            // ignored
        }
    }
}
