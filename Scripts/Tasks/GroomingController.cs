using UnityEngine;
using System.Collections;
using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;

public class GroomStepLog
{
    public string BeginTime;
    public string EndTime;
    public string Duration;
    public string Task;
}

public class GroomingController : MonoBehaviour
{
    // .json log & Video
    private float globalStartTime;
    private List<GroomStepLog> stepLogs = new List<GroomStepLog>();
    private DateTime sequenceStartTime;
    VideoRecorder recorder;
    private FloorRandomizer floorRandomizer;
    private PatientRandomizer patientRandomizer;
    private RopeRandomizer ropeRandomizer;
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

    // === Male Joint Transforms (for Lift Pose) ===
    public Transform maleRoot;
    public Transform spine1;
    public Transform spine2;
    public Transform left_hip;
    public Transform right_hip;
    public Transform left_knee;
    public Transform right_knee;
    public Transform left_elbow;
    public Transform right_elbow;

    // Head Points
    public Transform p1;
    public Transform p2;
    public Transform p3;
    public Transform p4;
    public Transform p5;
    public Transform p6;
    public Transform p7;
    public Transform p8;
    public Transform pA;

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
        floorRandomizer = FindObjectOfType<FloorRandomizer>();
        floorRandomizer.RandomizeFloor();
        patientRandomizer = FindObjectOfType<PatientRandomizer>();
        patientRandomizer.RandomizePatient();
        ropeRandomizer = FindObjectOfType<RopeRandomizer>();
        ropeRandomizer.RandomizeAllRopes();
        furnitureRandomizer = FindObjectOfType<FurnitureRandomizer>();
        furnitureRandomizer.RandomizeWalls();

        lastWheelPos = WheelChair.position;
        var baseLink = RobotRoot.Find("base_link");
        if (baseLink != null)
        {
            kinovaOffsetPos = Quaternion.Inverse(WheelChair.rotation) * (baseLink.position - WheelChair.position);
            kinovaOffsetRot = Quaternion.Inverse(WheelChair.rotation) * baseLink.rotation;
        }

        StartCoroutine(RunGroomSequence());
    }

    // === Core Groom Sequence ===
    IEnumerator RunGroomSequence()
    {
        yield return new WaitForSeconds(3.0f);
        yield return StartCoroutine(MalePoseLiftAnimationCoroutine(0.25f));
        yield return StartCoroutine(MalePoseLowerAnimationCoroutine(0.25f));

        globalStartTime = Time.time;
        if (recorder != null)
            recorder.BeginRecording("Grooming");

        yield return StartCoroutine(RunAndLogStep(GroomingOTBrushHair(), "GroomingOTBrushHair"));

        SaveLogToJson();

        if (recorder != null)
            recorder.StopRecording();

        Debug.Log("✅ All grooming steps complete.");
    }

    // === Step 1 ===
    IEnumerator GroomingOTBrushHair()
    {
        yield return new WaitForSeconds(0.2f);
        yield return StartCoroutine(MoveIKToAboveKinova());
        int totalBrushCycles = UnityEngine.Random.Range(8, 16);
        Transform[] hairPoints = new Transform[] { p1, p2, p3, p4, p5, p6, p7, p8 };

        for (int i = 0; i < totalBrushCycles; i++)
        {
            Transform targetPoint = hairPoints[UnityEngine.Random.Range(0, hairPoints.Length)];
            yield return StartCoroutine(MoveIKTargetTo(pA, 3.0f));
            int strokes = UnityEngine.Random.Range(2, 5);
            for (int s = 0; s < strokes; s++)
            {
                yield return StartCoroutine(MoveIKTargetTo(targetPoint, 2.8f));
                yield return StartCoroutine(MoveIKTargetTo(pA, 2.8f));
            }
            yield return new WaitForSeconds(0.1f);
        }
        
        yield return StartCoroutine(MoveIKToAboveKinova());
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

    void MoveMale()
    {
        if (male == null)
        {
            Debug.LogWarning("Male transform not assigned.");
            return;
        }
        var root = male.Find("SMPLX_male/root");
        if (root == null)
        {
            Debug.LogError("Cannot find 'SMPLX_male/root' under male.");
            return;
        }
        var ab = root.GetComponent<ArticulationBody>();
        if (ab == null)
        {
            Debug.LogError("No ArticulationBody found on root.");
            return;
        }
        Vector3 newPos = ab.transform.position + new Vector3(0, 0, 1.0f);
        Quaternion newRot = ab.transform.rotation;
        ab.TeleportRoot(newPos, newRot);
    }

    IEnumerator MoveIKToAboveKinova()
    {
        Transform baseLink = RobotRoot.Find("base_link");
        if (baseLink != null)
        {
            Vector3 above = baseLink.position + new Vector3(0f, 0.9f, 0f);
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

    IEnumerator MalePoseLiftAnimationCoroutine(float duration)
    {
        float elapsed = 0f;
        Vector3 maleStartPos = maleRoot.position;
        Vector3 maleTargetPos = new Vector3(maleStartPos.x, maleStartPos.y + 0.5f, maleStartPos.z + 0.3f);
        Quaternion maleStart = maleRoot.localRotation;
        Quaternion maleTarget = Quaternion.Euler(-80f, maleStart.eulerAngles.y, maleStart.eulerAngles.z);
        Quaternion spine1Start = spine1.localRotation;
        Quaternion spine1Target = Quaternion.Euler(30f, spine1Start.eulerAngles.y, spine1Start.eulerAngles.z);
        Quaternion spine2Start = spine2.localRotation;
        Quaternion spine2Target = Quaternion.Euler(17f, spine2Start.eulerAngles.y, spine2Start.eulerAngles.z);
        Quaternion lHipStart = left_hip.localRotation;
        Quaternion lHipTarget = Quaternion.Euler(-40f, lHipStart.eulerAngles.y, lHipStart.eulerAngles.z);
        Quaternion rHipStart = right_hip.localRotation;
        Quaternion rHipTarget = Quaternion.Euler(-40f, rHipStart.eulerAngles.y, rHipStart.eulerAngles.z);
        Quaternion lKneeStart = left_knee.localRotation;
        Quaternion lKneeTarget = Quaternion.Euler(110f, lKneeStart.eulerAngles.y, lKneeStart.eulerAngles.z);
        Quaternion rKneeStart = right_knee.localRotation;
        Quaternion rKneeTarget = Quaternion.Euler(110f, rKneeStart.eulerAngles.y, rKneeStart.eulerAngles.z);
        Quaternion lElbowStart = left_elbow.localRotation;
        Quaternion lElbowTarget = Quaternion.Euler(lElbowStart.eulerAngles.x, 55f, lElbowStart.eulerAngles.z);
        Quaternion rElbowStart = right_elbow.localRotation;
        Quaternion rElbowTarget = Quaternion.Euler(rElbowStart.eulerAngles.x, -55f, rElbowStart.eulerAngles.z);
        while (elapsed < duration)
        {
            float t = elapsed / duration;
            maleRoot.position = Vector3.Lerp(maleStartPos, maleTargetPos, t);
            maleRoot.localRotation = Quaternion.Slerp(maleStart, maleTarget, t);
            spine1.localRotation = Quaternion.Slerp(spine1Start, spine1Target, t);
            spine2.localRotation = Quaternion.Slerp(spine2Start, spine2Target, t);
            left_hip.localRotation = Quaternion.Slerp(lHipStart, lHipTarget, t);
            right_hip.localRotation = Quaternion.Slerp(rHipStart, rHipTarget, t);
            left_knee.localRotation = Quaternion.Slerp(lKneeStart, lKneeTarget, t);
            right_knee.localRotation = Quaternion.Slerp(rKneeStart, rKneeTarget, t);
            left_elbow.localRotation = Quaternion.Slerp(lElbowStart, lElbowTarget, t);
            right_elbow.localRotation = Quaternion.Slerp(rElbowStart, rElbowTarget, t);
            elapsed += Time.deltaTime;
            yield return null;
        }
        // Ensure final pose
        maleRoot.position = maleTargetPos;
        maleRoot.localRotation = maleTarget;
        spine1.localRotation = spine1Target;
        spine2.localRotation = spine2Target;
        left_hip.localRotation = lHipTarget;
        right_hip.localRotation = rHipTarget;
        left_knee.localRotation = lKneeTarget;
        right_knee.localRotation = rKneeTarget;
        left_elbow.localRotation = lElbowTarget;
        right_elbow.localRotation = rElbowTarget;
    }
    IEnumerator MalePoseLowerAnimationCoroutine(float duration)
    {
        float elapsed = 0f;

        Quaternion maleStart = maleRoot.localRotation;
        Quaternion maleTarget = Quaternion.Euler(-50f, maleStart.eulerAngles.y, maleStart.eulerAngles.z);
        Quaternion spine1Start = spine1.localRotation;
        Quaternion spine1Target = Quaternion.Euler(14f, spine1Start.eulerAngles.y, spine1Start.eulerAngles.z);
        Quaternion lHipStart = left_hip.localRotation;
        Quaternion lHipTarget = Quaternion.Euler(-53f, lHipStart.eulerAngles.y, lHipStart.eulerAngles.z);
        Quaternion rHipStart = right_hip.localRotation;
        Quaternion rHipTarget = Quaternion.Euler(-53f, rHipStart.eulerAngles.y, rHipStart.eulerAngles.z);
        Quaternion lElbowStart = left_elbow.localRotation;
        Quaternion lElbowTarget = Quaternion.Euler(25.3f, 66.8f, -4.1f);
        Quaternion rElbowStart = right_elbow.localRotation;
        Quaternion rElbowTarget = Quaternion.Euler(25.3f, -66.8f, 4.1f);

        while (elapsed < duration)
        {
            float t = elapsed / duration;
            maleRoot.localRotation = Quaternion.Slerp(maleStart, maleTarget, t);
            spine1.localRotation = Quaternion.Slerp(spine1Start, spine1Target, t);
            left_hip.localRotation = Quaternion.Slerp(lHipStart, lHipTarget, t);
            right_hip.localRotation = Quaternion.Slerp(rHipStart, rHipTarget, t);
            left_elbow.localRotation = Quaternion.Slerp(lElbowStart, lElbowTarget, t);
            right_elbow.localRotation = Quaternion.Slerp(rElbowStart, rElbowTarget, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Snap to final
        maleRoot.localRotation = maleTarget;
        spine1.localRotation = spine1Target;
        left_hip.localRotation = lHipTarget;
        right_hip.localRotation = rHipTarget;
        left_elbow.localRotation = lElbowTarget;
        right_elbow.localRotation = rElbowTarget;
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
            Debug.LogWarning($"{name}: No IKTarget objects found in scene.");
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
            Debug.Log($"{name}: Found IK target at {minDist:F3} meters from robot.");
        }
    }

    IEnumerator RunAndLogStep(IEnumerator coroutine, string taskName)
    {
        float begin = Time.time - globalStartTime;
        yield return StartCoroutine(coroutine);
        float end = Time.time - globalStartTime;
        TimeSpan duration = TimeSpan.FromSeconds(end - begin);

        GroomStepLog log = new GroomStepLog
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
        // /home/qiandaoliu/Grooming/JSON
        string baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Grooming", "JSON"
        );
        if (!Directory.Exists(baseDir))
            Directory.CreateDirectory(baseDir);

        int index = 1;
        string dir;
        do
        {
            dir = Path.Combine(baseDir, $"groom_{index:000}");
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
        Debug.Log($"✅ Groom log saved to: {path}");
    }

}
