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
    private int originalCaptureFramerate;

    /// <summary>
    /// 开始录制
    /// </summary>
    public void BeginRecording(string taskName = "")
    {
        if (isRecording) return;

        // 随机摄像机预设
        if (cameraPoses.Count > 0)
        {
            int idx = UnityEngine.Random.Range(0, cameraPoses.Count);
            recordCam.transform.position = cameraPoses[idx].position;
            recordCam.transform.rotation = Quaternion.Euler(cameraPoses[idx].rotation);
            UnityEngine.Debug.Log($"🎥 Camera moved to preset {idx + 1}");
        }

        // 构建输出目录
        string basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            taskName, "Videos"
        );
        Directory.CreateDirectory(basePath);

        int idxFolder = 1;
        do
        {
            outputDir = Path.Combine(basePath, $"{taskName.ToLower()}_{idxFolder:000}");
            idxFolder++;
        } while (Directory.Exists(outputDir));
        Directory.CreateDirectory(outputDir);

        // 保存并设置固定捕捉帧率
        originalCaptureFramerate = Time.captureFramerate;
        Time.captureFramerate = frameRate;

        // 配置渲染目标
        rt = new RenderTexture(width, height, 24);
        tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        recordCam.targetTexture = rt;

        frameIndex = 0;
        isRecording = true;
        StartCoroutine(CaptureFrames());
        UnityEngine.Debug.Log($"🎥 Recording started to: {outputDir}");
    }

    /// <summary>
    /// 停止录制并编码
    /// </summary>
    public void StopRecording()
    {
        if (!isRecording) return;
        isRecording = false;
        recordCam.targetTexture = null;
        RenderTexture.active = null;

        // 恢复默认帧率
        Time.captureFramerate = originalCaptureFramerate;

        UnityEngine.Debug.Log($"🎞️ Recording stopped. {frameIndex} frames saved.");
        StartCoroutine(EncodeAndCleanUp());
    }

    IEnumerator CaptureFrames()
    {
        // 每一帧都捕捉
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

    IEnumerator EncodeAndCleanUp()
    {
        // 等待文件写入稳定
        yield return new WaitForSeconds(0.5f);

        string mp4Path = Path.Combine(outputDir, "task.mp4");
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-y -framerate {frameRate} -i \"{Path.Combine(outputDir, "frame_%04d.png")}\" -c:v libx264 -pix_fmt yuv420p \"{mp4Path}\"",
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        var ffmpeg = Process.Start(psi);
        string ffOut = ffmpeg.StandardError.ReadToEnd();
        ffmpeg.WaitForExit();

        if (File.Exists(mp4Path))
        {
            UnityEngine.Debug.Log($"✅ Video saved to {mp4Path}, cleaning up PNGs...");
            foreach (var file in Directory.GetFiles(outputDir, "frame_*.png"))
                File.Delete(file);
        }
        else
        {
            UnityEngine.Debug.LogError("❌ ffmpeg failed to generate video:\n" + ffOut);
        }
    }
}
