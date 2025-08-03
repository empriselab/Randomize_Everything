using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System;

public class VideoRecorder : MonoBehaviour
{
    public Camera recordCam;
    public int frameRate = 15;
    public int width = 1080;
    public int height = 720;
    public string[] taskControllerNames = { "TransferController", "FeedingController", "BathingController" };

    [System.Serializable]
    public struct CameraPose
    {
        public Vector3 position;
        public Vector3 rotation; // Euler
    }
    public List<CameraPose> cameraPoses = new List<CameraPose>();

    private RenderTexture rt;
    private Texture2D tex;
    private string outputDir;
    private bool isRecording = false;
    private int frameIndex = 0;

    private MonoBehaviour activeTaskController;

    void Start()
    {
        StartCoroutine(CheckAndRecord());
    }

    IEnumerator CheckAndRecord()
    {
        yield return new WaitForSeconds(0.5f); // 等场景初始化
        activeTaskController = FindActiveTaskController();

        if (activeTaskController != null)
        {
            UnityEngine.Debug.Log($"🎯 Found active task: {activeTaskController.GetType().Name}");
            BeginRecording();
        }
        else
        {
            UnityEngine.Debug.Log("⚠️ No active task controller found, not recording.");
        }
    }

    MonoBehaviour FindActiveTaskController()
    {
        foreach (string name in taskControllerNames)
        {
            var type = Type.GetType(name);
            if (type == null) continue;

            var controller = FindObjectOfType(type) as MonoBehaviour;
            if (controller != null)
                return controller;
        }
        return null;
    }

    public void BeginRecording()
    {
        if (cameraPoses.Count > 0)
        {
            int idx = UnityEngine.Random.Range(0, cameraPoses.Count);
            recordCam.transform.position = cameraPoses[idx].position;
            recordCam.transform.rotation = Quaternion.Euler(cameraPoses[idx].rotation);
            UnityEngine.Debug.Log($"🎥 Camera moved to preset {idx + 1}");
        }

        string taskName = activeTaskController.GetType().Name.Replace("Controller", "");
        string basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            taskName, "Videos"
        );
        if (!Directory.Exists(basePath))
            Directory.CreateDirectory(basePath);

        int idxFolder = 1;
        do
        {
            outputDir = Path.Combine(basePath, $"{taskName.ToLower()}_{idxFolder:000}");
            idxFolder++;
        } while (Directory.Exists(outputDir));
        Directory.CreateDirectory(outputDir);

        Application.targetFrameRate = frameRate;
        rt = new RenderTexture(width, height, 24);
        tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        recordCam.targetTexture = rt;

        isRecording = true;
        StartCoroutine(CaptureFrames());
        UnityEngine.Debug.Log($"🎥 Recording started to: {outputDir}");
    }

    public void StopRecording()
    {
        if (!isRecording) return;
        isRecording = false;
        recordCam.targetTexture = null;
        RenderTexture.active = null;

        UnityEngine.Debug.Log($"🎞️ Recording stopped. {frameIndex} frames saved.");
        StartCoroutine(EncodeAndCleanUp());
    }

    IEnumerator EncodeAndCleanUp()
    {
        yield return new WaitForSeconds(0.5f);
        string mp4Path = Path.Combine(outputDir, "task.mp4");

        string[] frameFiles = Directory.GetFiles(outputDir, "frame_*.png");
        if (frameFiles.Length == 0)
        {
            UnityEngine.Debug.LogError("❌ No PNG frames found, cannot encode video.");
            yield break;
        }

        int minFrameNum = int.MaxValue;
        foreach (var file in frameFiles)
        {
            string name = Path.GetFileNameWithoutExtension(file); // frame_0038
            string numPart = name.Replace("frame_", ""); // 0038
            if (int.TryParse(numPart, out int num))
            {
                if (num < minFrameNum)
                    minFrameNum = num;
            }
        }

        Process ffmpeg = new Process();
        ffmpeg.StartInfo.FileName = "ffmpeg";
        ffmpeg.StartInfo.Arguments =
            $"-y -framerate {frameRate} -start_number {minFrameNum} -i \"{Path.Combine(outputDir, "frame_%04d.png")}\" -c:v libx264 -pix_fmt yuv420p \"{mp4Path}\"";
        ffmpeg.StartInfo.UseShellExecute = false;
        ffmpeg.StartInfo.RedirectStandardOutput = true;
        ffmpeg.StartInfo.RedirectStandardError = true;
        ffmpeg.StartInfo.CreateNoWindow = true;

        ffmpeg.Start();
        string output = ffmpeg.StandardError.ReadToEnd();
        ffmpeg.WaitForExit();

        if (File.Exists(mp4Path))
        {
            UnityEngine.Debug.Log($"✅ Video saved to {mp4Path}, cleaning up PNGs...");
            foreach (string path in frameFiles)
                File.Delete(path);
        }
        else
        {
            UnityEngine.Debug.LogError("❌ ffmpeg failed to generate video.\n" + output);
        }
    }

    IEnumerator CaptureFrames()
    {
        while (isRecording)
        {
            yield return new WaitForEndOfFrame();
            RenderTexture.active = rt;
            recordCam.Render();
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            string path = Path.Combine(outputDir, $"frame_{frameIndex:D04}.png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            frameIndex++;
        }
    }
}