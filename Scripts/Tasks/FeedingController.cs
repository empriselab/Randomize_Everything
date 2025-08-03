using UnityEngine;
using System.Collections;
using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;

public class FeedStepLog
{
    public string BeginTime;
    public string EndTime;
    public string Duration;
    public string Task;
}

public class FeedingController : MonoBehaviour
{
    // .json log & Video
    private float globalStartTime;
    private List<FeedStepLog> stepLogs = new List<FeedStepLog>();
    private DateTime sequenceStartTime;
    VideoRecorder recorder;
    private FloorRandomizer floorRandomizer;
    private PatientRandomizer patientRandomizer;
    private FurnitureRandomizer furnitureRandomizer;

    // Agents
    public Transform RobotRoot;  // kinova root
    public Transform WheelChair;  // the wheekchair
    public Transform male;  // male root

    // 轮椅和kinova
    public float ikMoveSpeedFactor = 0.2f;    // 抓取控制缩放（IK慢动作）
    public float wheelchairSpeedFactor = 0.25f;    // 轮椅控制缩放
    Vector3 lastWheelPos;
    private Vector3 kinovaOffsetPos;
    private Quaternion kinovaOffsetRot;
    public Transform WheelChairPos0;
    public Transform WheelChairPos1;
    public Transform WheelChairPos2;
    public Transform WheelChairPos3;
    public Transform WheelChairPos4;
    public Transform WheelChairPos5;
    public Transform WheelChairPos6;

    // Mouth Position
    public Transform Mouth;

    // 速度和距离
    public float moveSpeed = 0.3f;
    public float closeEnoughThreshold = 0.01f;

    // kinova IKTargetPoint
    private Transform ikTargetPoint;

    // 流程管理
    private enum State { Idle, WaitToMove, MoveMale, Done }
    private State currentState = State.Idle;
    private float waitTimer = 0f;

    void Start()
    {
        recorder = FindObjectOfType<VideoRecorder>();
        StartCoroutine(WaitBeforeStart());
        // Randomize Floor & Patient
        floorRandomizer = FindObjectOfType<FloorRandomizer>();
        floorRandomizer.RandomizeFloor();
        patientRandomizer = FindObjectOfType<PatientRandomizer>();
        patientRandomizer.RandomizePatient();
        furnitureRandomizer = FindObjectOfType<FurnitureRandomizer>();
        furnitureRandomizer.RandomizeWalls();

        lastWheelPos = WheelChair.position;
        var baseLink = RobotRoot.Find("base_link");
        if (baseLink != null)
        {
            kinovaOffsetPos = Quaternion.Inverse(WheelChair.rotation) * (baseLink.position - WheelChair.position);
            kinovaOffsetRot = Quaternion.Inverse(WheelChair.rotation) * baseLink.rotation;
        }

        StartCoroutine(RunFeedSequence());
    }

    // === Core Feed Sequence ===
    IEnumerator RunFeedSequence()
    {
        yield return new WaitForSeconds(3.0f);

        globalStartTime = Time.time;
        if (recorder != null)
            recorder.BeginRecording();

        yield return StartCoroutine(RunAndLogStep(MoveSpoonToMouth(), "MoveSpoonToMouth"));

        SaveLogToJson();

        if (recorder != null)
            recorder.StopRecording();

        UnityEngine.Debug.Log("✅ All feed steps complete.");
    }

    // === Step 1 ===
    IEnumerator MoveSpoonToMouth()
    {
        UnityEngine.Debug.Log("➡️ Step 1: MoveSpoonToMouth");
        yield return StartCoroutine(MoveIKToAboveKinova());
        yield return new WaitForSeconds(0.2f);
        yield return StartCoroutine(MoveIKTargetTo(Mouth, 1.0f));
    }

    void Update()
    {
        // Move Kinova with wheelchair
        SyncKinovaWithWheelchair();
    }


    // === Utility: Sync kinova base_link with wheelchair ===
    void SyncKinovaWithWheelchair()
    {
        var baseLink = RobotRoot.Find("base_link");
        if (baseLink == null) return;
        var ab = baseLink.GetComponent<ArticulationBody>();
        if (ab == null) return;
        Vector3 newWorldPos = WheelChair.position + WheelChair.rotation * kinovaOffsetPos;
        Quaternion newWorldRot = WheelChair.rotation * kinovaOffsetRot;
        ab.TeleportRoot(newWorldPos, newWorldRot);
    }
    IEnumerator MoveIKToAboveKinova()
    {
        Transform baseLink = RobotRoot.Find("base_link");
        if (baseLink != null)
        {
            Vector3 above = baseLink.position + new Vector3(0f, 0.8f, 0f);
            yield return StartCoroutine(MoveTransformTo(ikTargetPoint, above, 1f));
        }
    }
    IEnumerator MoveWheelchairTo(Transform target)
    {
        Vector3 direction = (target.position - WheelChair.position).normalized;
        Vector3 flatDirection = new Vector3(direction.x, 0f, direction.z).normalized;
        float angle = Quaternion.Angle(WheelChair.rotation, Quaternion.LookRotation(flatDirection, Vector3.up));
        if (angle > 1f)
            yield return StartCoroutine(RotateWheelchairTo(flatDirection));
        Vector3 targetXZ = new Vector3(target.position.x, WheelChair.position.y, target.position.z);
        yield return StartCoroutine(MoveTransformTo(WheelChair, targetXZ, 1f * wheelchairSpeedFactor));
    }
    IEnumerator RotateWheelchairTo(Vector3 lookDirection)
    {
        Quaternion startRot = WheelChair.rotation;
        Quaternion targetRot = Quaternion.LookRotation(lookDirection, Vector3.up);
        float t = 0f;
        float duration = 0.5f * wheelchairSpeedFactor;
        while (t < 1f)
        {
            WheelChair.rotation = Quaternion.Slerp(startRot, targetRot, t);
            t += Time.deltaTime / duration;
            yield return null;
        }
        WheelChair.rotation = targetRot;
    }
    IEnumerator RotateWheelchairBy(float angleDeg)
    {
        Quaternion startRot = WheelChair.rotation;
        Quaternion targetRot = Quaternion.Euler(0, angleDeg, 0) * startRot;
        float t = 0f;
        float duration = 1.0f * wheelchairSpeedFactor;
        while (t < 1f)
        {
            WheelChair.rotation = Quaternion.Slerp(startRot, targetRot, t);
            t += Time.deltaTime / duration;
            yield return null;
        }
        WheelChair.rotation = targetRot;
    }
    IEnumerator MoveTransformTo(Transform obj, Vector3 targetPos, float duration)
    {
        Vector3 startPos = obj.position;
        float t = 0f;
        while (t < 1f)
        {
            obj.position = Vector3.Lerp(startPos, targetPos, t);
            t += Time.deltaTime / duration;
            yield return null;
        }
        obj.position = targetPos;
    }
    IEnumerator MoveTransformToDual(Transform obj1, Transform obj2, Vector3 targetPos, float duration)
    {
        Vector3 start1 = obj1.position;
        Vector3 start2 = obj2.position;
        float t = 0f;
        while (t < 1f)
        {
            obj1.position = Vector3.Lerp(start1, targetPos, t);
            obj2.position = Vector3.Lerp(start2, targetPos, t);
            t += Time.deltaTime / duration;
            yield return null;
        }
        obj1.position = targetPos;
        obj2.position = targetPos;
    }
    IEnumerator MoveIKTargetTo(Transform target, float duration)
    {
        duration = duration * 3;
        float elapsed = 0f;
        Vector3 startPos = ikTargetPoint.position;
        Vector3 endPos = target.position;

        while (elapsed < duration)
        {
            float t = elapsed / duration;
            ikTargetPoint.position = Vector3.Lerp(startPos, endPos, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        ikTargetPoint.position = endPos;
    }

    // Tools
    IEnumerator WaitBeforeStart()
    {
        yield return new WaitForSeconds(0.5f);
        TryFindNearestIKTarget();
    }

    void TryFindNearestIKTarget()
    {
        GameObject[] allTargets = GameObject.FindGameObjectsWithTag("IKTarget");
        if (allTargets.Length == 0)
        {
            UnityEngine.Debug.LogWarning($"{name}: No IKTarget objects found in scene.");
            return;
        }
        float minDist = float.MaxValue;
        Transform closest = null;
        foreach (var go in allTargets)
        {
            float dist = Vector3.Distance(go.transform.position, RobotRoot.position);
            if (dist < minDist)
            {
                minDist = dist;
                closest = go.transform;
            }
        }
        if (closest != null)
        {
            ikTargetPoint = closest;
            ikTargetPoint.rotation = Quaternion.Euler(
                ikTargetPoint.eulerAngles.x,
                ikTargetPoint.eulerAngles.y + 90f,
                ikTargetPoint.eulerAngles.z + 180f
            );
            UnityEngine.Debug.Log($"{name}: Found IK target at {minDist:F3} meters from robot.");
        }
    }

    IEnumerator RunAndLogStep(IEnumerator coroutine, string taskName)
    {
        float begin = Time.time - globalStartTime;
        yield return StartCoroutine(coroutine);
        float end = Time.time - globalStartTime;
        TimeSpan duration = TimeSpan.FromSeconds(end - begin);

        FeedStepLog log = new FeedStepLog
        {
            BeginTime = FormatTime(begin),
            EndTime = FormatTime(end),
            Duration = FormatTime((float)duration.TotalSeconds),
            Task = taskName
        };
        stepLogs.Add(log);
    }
    string FormatTime(float seconds)
    {
        TimeSpan timeSpan = TimeSpan.FromSeconds(seconds);
        return timeSpan.ToString(@"hh\:mm\:ss\.fff");
    }
    void SaveLogToJson()
    {
        // /home/qiandaoliu/Feeding/JSON
        string baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Feeding", "JSON"
        );
        if (!Directory.Exists(baseDir))
            Directory.CreateDirectory(baseDir);

        int index = 1;
        string dir;
        do
        {
            dir = Path.Combine(baseDir, $"feed_{index:000}");
            index++;
        }
        while (Directory.Exists(dir));
        Directory.CreateDirectory(dir);

        string path = Path.Combine(dir, "log.json");

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("[");
        for (int i = 0; i < stepLogs.Count; i++)
        {
            var log = stepLogs[i];
            sb.AppendLine("  {");
            sb.AppendLine($"    \"Begin Time\": \"{log.BeginTime}\",");
            sb.AppendLine($"    \"End Time\": \"{log.EndTime}\",");
            sb.AppendLine($"    \"Duration\": \"{log.Duration}\",");
            sb.AppendLine($"    \"Task\": \"{log.Task}\"");
            sb.Append(i < stepLogs.Count - 1 ? "  }," : "  }");
            sb.AppendLine();
        }
        sb.AppendLine("]");

        File.WriteAllText(path, sb.ToString());
        UnityEngine.Debug.Log($"✅ feed log saved to: {path}");
    }
}
