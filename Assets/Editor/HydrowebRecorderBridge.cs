// HydrowebRecorderBridge.cs — the Editor half of Hydroweb.HydrowebRecordTrigger (2026-10-05).
// Starts / stops a Unity Recorder movie of the trigger's RenderTexture when a test script asks over ROS.
// Output: <SMARCUnity>/Recordings/<yyyyMMdd_HHmmss>_<tag>.mp4 (H.264, medium quality).
// Frame rate playback is VARIABLE and not capped, so the recorder never takes over Time.captureFramerate:
// the simulation runs exactly as it would without a recording (a constant-rate recorder would slow or speed
// sim time to its frame rate, which would change the very test being recorded).

using System;
using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

[InitializeOnLoad]
public static class HydrowebRecorderBridge
{
    static RecorderController controller;
    static string currentPath = "";

    static HydrowebRecorderBridge()
    {
        Hydroweb.HydrowebRecordTrigger.OnStart = Start;
        Hydroweb.HydrowebRecordTrigger.OnStop = Stop;
        EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.ExitingPlayMode) Stop(); };
    }

    static string Start(string tag, RenderTexture[] rts, string[] names)
    {
        try
        {
            if (controller != null && controller.IsRecording()) Stop();
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings"));
            Directory.CreateDirectory(dir);
            string stem = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd_HHmmss}_{tag}");

            var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            var files = new System.Collections.Generic.List<string>();
            for (int i = 0; i < rts.Length; i++)       // one movie per camera, all in ONE controller (one shared clock)
            {
                string suffix = string.IsNullOrEmpty(names[i]) ? "" : "_" + names[i];
                var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
                movie.name = "hydroweb_" + tag + suffix;
                movie.Enabled = true;
                movie.EncoderSettings = new CoreEncoderSettings
                {
                    Codec = CoreEncoderSettings.OutputCodec.MP4,
                    EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.Medium,
                };
                movie.ImageInputSettings = new RenderTextureInputSettings { RenderTexture = rts[i] };
                movie.CaptureAudio = false;
                movie.OutputFile = stem + suffix;
                settings.AddRecorderSettings(movie);
                files.Add(stem + suffix + ".mp4");
            }
            settings.SetRecordModeToManual();
            settings.FrameRatePlayback = FrameRatePlayback.Variable;
            settings.FrameRate = 30f;
            settings.CapFrameRate = false;

            controller = new RecorderController(settings);
            controller.PrepareRecording();
            if (!controller.StartRecording()) return "ERROR Unity Recorder refused to start";
            currentPath = string.Join(" ; ", files);
            return currentPath;
        }
        catch (Exception e)
        {
            return "ERROR " + e.Message;
        }
    }

    static string Stop()
    {
        if (controller == null) return "not recording";
        try
        {
            if (controller.IsRecording()) controller.StopRecording();
        }
        catch (Exception e)
        {
            return "ERROR " + e.Message;
        }
        controller = null;
        return currentPath;
    }
}
