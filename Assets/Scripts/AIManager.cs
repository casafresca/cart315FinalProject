using UnityEngine;
using System.Diagnostics;
using System.IO;

public class AIManager : MonoBehaviour
{
    string ttsRoot = "AI/TTS";
    string venvPython;

    void Start()
    {
        venvPython = Path.Combine(ttsRoot, ".venv", "Scripts", "python.exe");

        if (!File.Exists(venvPython))
        {
            UnityEngine.Debug.Log("TTS environment missing. Running setup...");
            RunSetup();
        }

        RunTTS("Hello from Unity.");
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

    void RunTTS(string text)
    {
        ProcessStartInfo start = new()
        {
            FileName = venvPython,
            Arguments = $"tts_cli_player_basic.py \"{text}\"",
            WorkingDirectory = ttsRoot,

            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process.Start(start);
    }
}
