using UnityEngine;
using System.Collections;
using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;

public class VestStepLog
{
    public string BeginTime;
    public string EndTime;
    public string Duration;
    public string Task;
}

public class VestController : MonoBehaviour
{
    // .json log & Video
    private float globalStartTime;
    private List<VestStepLog> stepLogs = new List<VestStepLog>();
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

    // Vest
    public Transform WheelChairPos0;
    public Transform vest;
    public Transform ik1;
    public Transform ik2;
    public Transform ik3;
    public Transform ik4;
    public Transform ik5;
    public Transform ik6;
    public Transform ik7;
    public Transform ik8;
    public Transform ik9;
    public Transform s1;
    public Transform s2;

    // ClothGroup
    public Transform Cloth;  // Unity obi cloth
    public Transform ClothGroup;
    public Transform Cloth1;
    public Transform Cloth2;
    public Transform Cloth3;
    public Transform Cloth4;

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

        SetAllRenderersEnabled(vest, false);
        SetAllRenderersEnabled(Cloth, true);
        SetAllRenderersEnabled(ClothGroup, true);

        StartCoroutine(RunVestSequence());
    }

    // === Core Vest Sequence ===
    IEnumerator RunVestSequence()
    {
        yield return new WaitForSeconds(3.0f);
        // 【此处让】
        // 【此处把Cloth1 2 3 4 这四个物体朝中间聚拢，直到几乎接触到一起】
        yield return StartCoroutine(SetupCloth());

        globalStartTime = Time.time;
        if (recorder != null)
            recorder.BeginRecording("Vest");

        yield return StartCoroutine(RunAndLogStep(OrientVest(), "OrientVest"));
        yield return StartCoroutine(RunAndLogStep(VestOnBedDressLeftArm(), "VestOnBedDressLeftArm"));
        yield return StartCoroutine(RunAndLogStep(VestOnBedDressRightArm(), "VestOnBedDressRightArm"));
        yield return StartCoroutine(RunAndLogStep(VestOnBedDressBack(), "VestOnBedDressBack"));
        yield return StartCoroutine(RunAndLogStep(VestOnBedCloseFrontOfVest(), "VestOnBedCloseFrontOfVest"));

        SaveLogToJson();

        if (recorder != null)
            recorder.StopRecording();

        Debug.Log("✅ All vest steps complete.");
    }

    // === Step 0 ===
    IEnumerator SetupCloth()
    {
        Vector3 center = (Cloth1.position + Cloth2.position + Cloth3.position + Cloth4.position) / 4f;
        float r = 0.02f;
        Vector3[] offsets = new Vector3[]
        {
            new Vector3(+r, 0, 0),
            new Vector3(-r, 0, 0),
            new Vector3(0, 0, +r),
            new Vector3(0, 0, -r),
        };
        yield return StartCoroutine(RunParallel(
            MoveTransformTo(Cloth1, center + offsets[0], 1.0f),
            MoveTransformTo(Cloth2, center + offsets[1], 1.0f),
            MoveTransformTo(Cloth3, center + offsets[2], 1.0f),
            MoveTransformTo(Cloth4, center + offsets[3], 1.0f),
            ClothGroup ? MoveTransformTo(ClothGroup, center, 1.0f) : NullRoutine()
        ));
    }

    // === Step 1 ===
    IEnumerator OrientVest()
    {
        yield return new WaitForSeconds(0.2f);
        yield return StartCoroutine(MoveIKToAboveKinova());
        yield return StartCoroutine(MoveWheelchairTo(WheelChairPos0));

        yield return StartCoroutine(MoveIKTargetTo(ik1, 10.0f));
        yield return new WaitForSeconds(1.2f);
    }

    // === Step 2 ===
    IEnumerator VestOnBedDressLeftArm()
    {
        yield return new WaitForSeconds(0.5f);

        // 【此处 ClothGroup 和 IKTarget 一起 移动到 ik2】
        yield return StartCoroutine(RunParallel(
            MoveIKTargetTo(ik2, 10.0f),
            ClothGroup ? MoveTransformToTarget(ClothGroup, ik2, 10.0f) : NullRoutine()
        ));

        // 【此处 ClothGroup 和 IKTarget 一起 移动到 ik3】
        yield return StartCoroutine(RunParallel(
            MoveIKTargetTo(ik3, 10.0f),
            ClothGroup ? MoveTransformToTarget(ClothGroup, ik3, 10.0f) : NullRoutine()
        ));
    }

    // === Step 3 ===
    IEnumerator VestOnBedDressRightArm()
    {
        yield return new WaitForSeconds(2.0f);

        // 目标集合：Cloth1/2/3 和 IK
        yield return StartCoroutine(RunParallel(
            MoveIKTargetTo(ik4, 10.0f),
            MoveTransformToTarget(Cloth1, ik4, 10.0f),
            MoveTransformToTarget(Cloth2, ik4, 10.0f),
            MoveTransformToTarget(Cloth3, ik4, 10.0f)
        // Cloth4 留在原地，不加入
        ));

        yield return StartCoroutine(RunParallel(
            MoveIKTargetTo(ik5, 10.0f),
            MoveTransformToTarget(Cloth1, ik5, 10.0f),
            MoveTransformToTarget(Cloth2, ik5, 10.0f),
            MoveTransformToTarget(Cloth3, ik5, 10.0f)
        ));
    }

    // === Step 4 ===
    // IEnumerator TShirtOnBedTShirtOverHead()
    // {
    //     yield return new WaitForSeconds(2.5f);

    //     // 【Cloth1 留在原地，Cloth2/3 + IK】
    //     yield return StartCoroutine(RunParallel(
    //         MoveIKTargetTo(ik6, 7.0f),
    //         MoveTransformToTarget(Cloth2, ik6, 7.0f),
    //         MoveTransformToTarget(Cloth3, ik6, 7.0f)
    //     ));

    //     yield return StartCoroutine(RunParallel(
    //         MoveIKTargetTo(ik7, 7.0f),
    //         MoveTransformToTarget(Cloth2, ik7, 7.0f),
    //         MoveTransformToTarget(Cloth3, ik7, 7.0f)
    //     ));

    //     yield return StartCoroutine(RunParallel(
    //         MoveIKTargetTo(ik8, 7.0f),
    //         MoveTransformToTarget(Cloth2, ik8, 7.0f),
    //         MoveTransformToTarget(Cloth3, ik8, 7.0f)
    //     ));
    // }

    // === Step 5 ===
    IEnumerator VestOnBedDressBack()
    {
        yield return new WaitForSeconds(1.5f);

        // 【Cloth2 3这两个物体 和 IKTarget 一起 移动到 ik9】
        yield return StartCoroutine(RunParallel(
            MoveIKTargetTo(ik9, 7.0f),
            MoveTransformToTarget(Cloth2, ik9, 7.0f),
            MoveTransformToTarget(Cloth3, ik9, 7.0f)
        ));
    }

    // === Step 6 ===
    IEnumerator VestOnBedCloseFrontOfVest()
    {
        // 【此处让 obi Cloth 物体消失，同时让 Shirt 出现】
        // 仅切渲染器，不影响物理；若想物理也停，可直接 SetActive(false)
        SetAllRenderersEnabled(Cloth, false);
        SetAllRenderersEnabled(ClothGroup, false);
        SetAllRenderersEnabled(vest, true);

        yield return new WaitForSeconds(2.0f);
        yield return StartCoroutine(MoveIKToAboveKinova());
        yield return new WaitForSeconds(4.0f);
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

    IEnumerator MoveTransformToTarget(Transform obj, Transform target, float duration)
    {
        if (obj == null || target == null) yield break;
        Vector3 startPos = obj.position;
        Vector3 endPos = target.position;
        float t = 0f;
        while (t < 1f)
        {
            obj.position = Vector3.Lerp(startPos, endPos, t);
            t += Time.deltaTime / duration;
            yield return null;
        }
        obj.position = endPos;
    }
    IEnumerator RunParallel(params IEnumerator[] coroutines)
    {
        int n = coroutines.Length;
        int finished = 0;
        bool[] done = new bool[n];

        for (int i = 0; i < n; i++)
        {
            int idx = i;
            StartCoroutine(Wrapper(coroutines[i], () => { done[idx] = true; finished++; }));
        }
        while (finished < n)
            yield return null;

        IEnumerator Wrapper(IEnumerator co, Action onDone)
        {
            yield return StartCoroutine(co);
            onDone?.Invoke();
        }
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

        VestStepLog log = new VestStepLog
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
        // /home/qiandaoliu/Vest/JSON
        string baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Vest", "JSON"
        );
        if (!Directory.Exists(baseDir))
            Directory.CreateDirectory(baseDir);

        int index = 1;
        string dir;
        do
        {
            dir = Path.Combine(baseDir, $"vest_{index:000}");
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
        Debug.Log($"✅ Vest log saved to: {path}");
    }

    // Specific tools for Vest task
    IEnumerator NullRoutine() { yield break; }

    void SetAllRenderersEnabled(Transform root, bool enabled)
    {
        if (root == null) return;
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers) r.enabled = enabled;

        // 如果还有额外可见性组件（例如某些 Obi 可视化脚本继承自 Behaviour）
        var behaviours = root.GetComponentsInChildren<Behaviour>(true);
        foreach (var b in behaviours)
        {
            // 仅对典型的可视化类做兼容性开关（避免误关 IK/刚体等）
            string n = b.GetType().Name.ToLowerInvariant();
            if (n.Contains("renderer") || n.Contains("visual"))
                b.enabled = enabled;
        }
    }


}
