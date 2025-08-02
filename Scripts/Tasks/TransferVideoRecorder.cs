using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System;

public class TransferVideoRecorder : MonoBehaviour
{
    public Camera recordCam;
    public int frameRate = 15;
    public int width = 1080;
    public int height = 720;

    private RenderTexture rt;
    private Texture2D tex;
    private List<string> framePaths = new List<string>();
    private string outputDir;
    private bool isRecording = false;
    private int frameIndex = 0;

    public void BeginRecording()
    {
        string basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "rcare_workspace/dataset/transferring/videos");
        if (!Directory.Exists(basePath)) Directory.CreateDirectory(basePath);

        int idx = 1;
        do
        {
            outputDir = Path.Combine(basePath, $"transfer_{idx:000}");
            idx++;
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
        isRecording = false;
        recordCam.targetTexture = null;
        RenderTexture.active = null;

        UnityEngine.Debug.Log($"🎞️ Recording stopped. {frameIndex} frames saved.");

        StartCoroutine(EncodeAndCleanUp());
    }

    IEnumerator EncodeAndCleanUp()
    {
        yield return new WaitForSeconds(0.5f); // ensure last frame is flushed

        string mp4Path = Path.Combine(outputDir, "transfer.mp4");

        Process ffmpeg = new Process();
        ffmpeg.StartInfo.FileName = "ffmpeg";
        ffmpeg.StartInfo.Arguments = $"-y -framerate {frameRate} -i \"{Path.Combine(outputDir, "frame_%04d.png")}\" -c:v libx264 -pix_fmt yuv420p \"{mp4Path}\"";
        ffmpeg.StartInfo.UseShellExecute = false;
        ffmpeg.StartInfo.RedirectStandardOutput = true;
        ffmpeg.StartInfo.RedirectStandardError = true;
        ffmpeg.StartInfo.CreateNoWindow = true;

        ffmpeg.Start();
        string output = ffmpeg.StandardError.ReadToEnd(); // helpful if you want to debug
        ffmpeg.WaitForExit();

        if (File.Exists(mp4Path))
        {
            UnityEngine.Debug.Log($"✅ Video saved to {mp4Path}, cleaning up PNGs...");

            // Delete all PNGs
            foreach (string path in Directory.GetFiles(outputDir, "frame_*.png"))
                File.Delete(path);
        }
        else
        {
            UnityEngine.Debug.LogError("❌ ffmpeg failed to generate video. Check ffmpeg installation or error log.");
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
            framePaths.Add(path);
            frameIndex++;
        }
    }
}
