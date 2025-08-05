using UnityEngine;
using System.Collections;
using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;

public class TransferStepLog
{
    public string BeginTime;
    public string EndTime;
    public string Duration;
    public string Task;
}

public class TransferController : MonoBehaviour
{
    // .json log & Video
    private float globalStartTime;
    private List<TransferStepLog> stepLogs = new List<TransferStepLog>();
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
    public Transform Cloth;  // cloth position

    // Rope & Cloth
    public Transform ClothRopeGroup;
    public Transform ClothRope0;
    public Transform ClothRope1;
    public Transform ClothRope2;
    public Transform ClothRope3;

    public Transform RopeTarget0;
    public Transform RopeTarget1;
    public Transform RopeTarget2;
    public Transform RopeTarget3;

    public Transform RopeLiftGroup;
    public Transform RopeLift0;
    public Transform RopeLift1;
    public Transform RopeLift2;
    public Transform RopeLift3;

    // Lift System
    public Transform LiftSystem;
    public Transform liftGameObject;          // LiftSystem/PatientLift/GameObject
    public Transform top_pivot;               // LiftSystem/PatientLift/GameObject/top_pivot
    public Transform topPivotChild;           // LiftSystem/PatientLift/GameObject/top_pivot/GameObject
    public Transform patient_lift_base_2_pivot;  // LiftSystem/PatientLift/patient_lift_base_2_pivot
    public Transform patient_lift_base_1_pivot;  // LiftSystem/PatientLift/patient_lift_base_1_pivot
    private Vector3 ropeLiftOffset;
    private bool ropeLiftFollowing = false;

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
    public Transform WheelChairPos7;
    public Transform WheelChairPos8;

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
        Debug.Log("Start 1 - Recorder found");

        StartCoroutine(WaitBeforeStart());
        // Randomize Floor & Patient & Rope
        floorRandomizer = FindObjectOfType<FloorRandomizer>();
        floorRandomizer.RandomizeFloor();
        Debug.Log("Start 2 - Randomizing floor");
        patientRandomizer = FindObjectOfType<PatientRandomizer>();
        patientRandomizer.RandomizePatient();
        Debug.Log("Start 3 - Randomizing patient");
        ropeRandomizer = FindObjectOfType<RopeRandomizer>();
        ropeRandomizer.RandomizeAllRopes();
        Debug.Log("Start 4 - Randomizing ropes");
        furnitureRandomizer = FindObjectOfType<FurnitureRandomizer>();
        furnitureRandomizer.RandomizeWalls();
        Debug.Log("Start 5 - Randomizing furniture");   

        lastWheelPos = WheelChair.position;
        var baseLink = RobotRoot.Find("base_link");
        if (baseLink != null)
        {
            kinovaOffsetPos = Quaternion.Inverse(WheelChair.rotation) * (baseLink.position - WheelChair.position);
            kinovaOffsetRot = Quaternion.Inverse(WheelChair.rotation) * baseLink.rotation;
        }
        Debug.Log("Start 6 - Calculating kinova offset");

        StartCoroutine(LowerRopeLiftGroupSmooth());
        Debug.Log("Start 7 - Starting rope lowering coroutine");
        StartCoroutine(RunTransferSequence());
    }

    // === Core Transfer Sequence ===
    IEnumerator RunTransferSequence()
    {
        Debug.Log("Start 8 - Starting transfer sequence"); 
        yield return new WaitForSeconds(3.0f);

        globalStartTime = Time.time;
        if (recorder != null)
            recorder.BeginRecording("Transfer");

        yield return StartCoroutine(RunAndLogStep(AlignLiftToBed(), "AlignLiftToBed"));
        yield return StartCoroutine(RunAndLogStep(LoadPatientOnLift(), "LoadPatientOnLift"));
        yield return StartCoroutine(RunAndLogStep(RaisePatientOnLift(), "RaisePatientOnLift"));
        yield return StartCoroutine(RunAndLogStep(AlignLiftToWheelchair(), "AlignLiftToWheelchair"));
        yield return StartCoroutine(RunAndLogStep(LowerPatientOnLift(), "LowerPatientOnLift"));
        yield return StartCoroutine(RunAndLogStep(UnloadPatientOffLift(), "UnloadPatientOffLift"));
        yield return StartCoroutine(RunAndLogStep(RemoveLift(), "RemoveLift"));

        SaveLogToJson();

        if (recorder != null)
            recorder.StopRecording();

        Debug.Log("✅ All transfer steps complete.");
    }
    
    // === Step 1 ===
    IEnumerator AlignLiftToBed()
    {
        Debug.Log("➡️ Step 1: TransferBedToWheelchair_AlignLift_ToBed");
        Vector3 clothXZ = new Vector3(Cloth.position.x, 0f, Cloth.position.z);
        float threshold = 0.01f;
        float speed = 0.5f;
        while (true)
        {
            Vector3 topXZ = new Vector3(topPivotChild.position.x, 0f, topPivotChild.position.z);
            Vector3 delta = clothXZ - topXZ;
            if (delta.magnitude < threshold)
            {
                Debug.Log("✅ Lift aligned over Cloth.");
                break;
            }
            Vector3 moveDir = delta.normalized;
            Vector3 moveStep = moveDir * speed * Time.deltaTime;
            LiftSystem.position += new Vector3(moveStep.x, 0f, moveStep.z);
            Debug.DrawLine(topPivotChild.position, Cloth.position, Color.green);
            yield return null;
        }
    }

    // === Step 2 ===
    IEnumerator LoadPatientOnLift()
    {
        Debug.Log("➡️ Step 2: TransferBedToWheelchair_LoadPatient_OnLift");

        // === Step 1 ===
        yield return StartCoroutine(AttachRope(RopeLift1, RopeTarget1));
        yield return new WaitForSeconds(0.5f);
        yield return StartCoroutine(MoveIKToAboveKinova());
        // === Step 2 ===
        yield return StartCoroutine(MoveWheelchairTo(WheelChairPos0));
        yield return StartCoroutine(AttachRope(RopeLift0, RopeTarget0));
        yield return new WaitForSeconds(0.5f);
        // === Step 3 === 回到 Kinova 正上方
        yield return StartCoroutine(MoveIKToAboveKinova());
        // === Step 4 === 移动轮椅到 Pos1 → Pos2 → Pos3
        yield return StartCoroutine(MoveWheelchairTo(WheelChairPos1));
        yield return StartCoroutine(MoveWheelchairTo(WheelChairPos2));
        yield return StartCoroutine(MoveWheelchairTo(WheelChairPos3));
        // === Step 5 ===
        yield return StartCoroutine(AttachRope(RopeLift3, RopeTarget3));
        yield return new WaitForSeconds(0.5f);
        yield return StartCoroutine(MoveIKToAboveKinova());
        // === Step 6 === 移动轮椅到 Pos4 → Pos5 → Pos6
        yield return StartCoroutine(MoveWheelchairTo(WheelChairPos4));
        yield return StartCoroutine(MoveWheelchairTo(WheelChairPos5));
        yield return StartCoroutine(MoveWheelchairTo(WheelChairPos6));
        // === Step 7 ===
        yield return StartCoroutine(AttachRope(RopeLift2, RopeTarget2));
        yield return new WaitForSeconds(0.5f);
        // === Step 8 === 回到 Kinova 正上方
        yield return StartCoroutine(MoveIKToAboveKinova());
        yield return new WaitForSeconds(0.5f);
        // === Step 9 ===
        yield return StartCoroutine(RotateWheelchairBy(-90f));
    }

    // === Step 3 ===
    IEnumerator RaisePatientOnLift()
    {
        Debug.Log("➡️ Step 3: TransferBedToWheelchair_RaisePatient_OnLift");
        ropeLiftOffset = RopeLiftGroup.position - topPivotChild.position;
        ropeLiftFollowing = true;
        yield return StartCoroutine(LiftSequenceCoroutine());
    }

    // === Step 4 ===
    IEnumerator AlignLiftToWheelchair()
    {
        Debug.Log("➡️ Step 4: TransferBedToWheelchair_AlignLift_ToWheelchair");
        Vector3 targetXZ = new Vector3(WheelChair.position.x + 0.5f, topPivotChild.position.y, WheelChair.position.z);
        Vector3 offset = targetXZ - topPivotChild.position;
        Transform[] moveGroup = new Transform[]
        {
            LiftSystem,
            male,
            ClothRope0,
            ClothRope1,
            ClothRope2,
            ClothRope3
        };
        float duration = 5.0f;
        float elapsed = 0f;
        Vector3[] startPositions = new Vector3[moveGroup.Length];
        for (int i = 0; i < moveGroup.Length; i++)
            startPositions[i] = moveGroup[i].position;
        while (elapsed < duration)
        {
            float t = elapsed / duration;
            for (int i = 0; i < moveGroup.Length; i++)
                moveGroup[i].position = Vector3.Lerp(startPositions[i], startPositions[i] + offset, t);

            elapsed += Time.deltaTime;
            yield return null;
        }
        for (int i = 0; i < moveGroup.Length; i++)
            moveGroup[i].position = startPositions[i] + offset;
    }

    // === Step 5 ===
    IEnumerator LowerPatientOnLift()
    {
        Debug.Log("➡️ Step 5: TransferBedToWheelchair_LowerPatient_OnLift");
        yield return new WaitForSeconds(1.0f);
        yield return StartCoroutine(LowerSequenceCoroutine());
        ropeLiftFollowing = false;
    }

    // === Step 6 ===
    IEnumerator UnloadPatientOffLift()
    {
        Debug.Log("➡️ Step 6: TransferBedToWheelchair_UnloadPatient_OffLift");
        ropeLiftFollowing = false;

        Transform[] ropeTargets = new Transform[] { RopeTarget0, RopeTarget1, RopeTarget2, RopeTarget3 };
        Transform[] ropeLifts = new Transform[] { RopeLift0, RopeLift1, RopeLift2, RopeLift3 };

        for (int i = 0; i < 4; i++)
        {
            yield return StartCoroutine(MoveIKTargetTo(ropeTargets[i], 1.0f));
            StartCoroutine(DropRopeLift(ropeLifts[i]));
            yield return new WaitForSeconds(0.5f);
        }
    }

    // === Step 7 ===
    IEnumerator RemoveLift()
    {
        Debug.Log("➡️ Step 7: TransferBedToWheelchair_Remove_Lift");
        // TODO: Move lift away from scene
        yield return new WaitForSeconds(1.0f);
    }


    void Update()
    {
        // Move Kinova with wheelchair
        SyncKinovaWithWheelchair();
        
        if (ropeLiftFollowing)
        {
            RopeLiftGroup.position = topPivotChild.position + ropeLiftOffset;
        }
    }

    // === Helper: Lift up ===
    IEnumerator LiftSequenceCoroutine()
    {
        float duration = 5.0f;
        float elapsed = 0f;
        StartCoroutine(MalePoseLiftAnimationCoroutine(duration));
        Quaternion liftStartRot = liftGameObject.rotation;
        Quaternion topChildStartRot = topPivotChild.rotation;
        Vector3 baseStartPos = patient_lift_base_2_pivot.position;
        Vector3 baseTargetPos = baseStartPos + new Vector3(0f, 0.5f, 0f);
        Quaternion liftTargetRot = Quaternion.AngleAxis(-45f, liftGameObject.right) * liftStartRot;
        Quaternion topChildTargetRot = Quaternion.AngleAxis(0.0f, topPivotChild.right) * topChildStartRot;
        Quaternion base1StartRot = patient_lift_base_1_pivot.rotation;
        Quaternion base2StartRot = patient_lift_base_2_pivot.rotation;
        Quaternion base1TargetRot = Quaternion.AngleAxis(-2.4f, patient_lift_base_1_pivot.right) * base1StartRot;
        Quaternion base2TargetRot = Quaternion.AngleAxis(-2.4f, patient_lift_base_2_pivot.right) * base2StartRot;
        Vector3 clothRopeStartPos = ClothRopeGroup.position;
        Vector3 clothRopeTargetPos = clothRopeStartPos + new Vector3(0f, 0.5f, 0f);
        Vector3[] ropeStart = new Vector3[4];
        Vector3[] ropeTarget = new Vector3[4];
        Transform[] ropes = new Transform[] { ClothRope0, ClothRope1, ClothRope2, ClothRope3 };
        Vector3 center = (ClothRope0.position + ClothRope1.position + ClothRope2.position + ClothRope3.position) / 4f;
        for (int i = 0; i < 4; i++)
        {
            ropeStart[i] = ropes[i].position;
            Vector3 dirToCenter = (center - ropeStart[i]).normalized;
            ropeTarget[i] = ropeStart[i] + dirToCenter * 0.3f + new Vector3(0f, 1.0f, 0.3f);
        }
        while (elapsed < duration)
        {
            float t = elapsed / duration;
            liftGameObject.rotation = Quaternion.Slerp(liftStartRot, liftTargetRot, t);
            topPivotChild.rotation = Quaternion.Slerp(topChildStartRot, topChildTargetRot, t);
            patient_lift_base_2_pivot.position = Vector3.Lerp(baseStartPos, baseTargetPos, t);
            patient_lift_base_1_pivot.rotation = Quaternion.Slerp(base1StartRot, base1TargetRot, t);
            patient_lift_base_2_pivot.rotation = Quaternion.Slerp(base2StartRot, base2TargetRot, t);
            ClothRopeGroup.position = Vector3.Lerp(clothRopeStartPos, clothRopeTargetPos, t);
            for (int i = 0; i < 4; i++)
                ropes[i].position = Vector3.Lerp(ropeStart[i], ropeTarget[i], t);
            elapsed += Time.deltaTime;
            yield return null;
        }
        liftGameObject.rotation = liftTargetRot;
        topPivotChild.rotation = topChildTargetRot;
        patient_lift_base_2_pivot.position = baseTargetPos;
        patient_lift_base_1_pivot.rotation = base1TargetRot;
        patient_lift_base_2_pivot.rotation = base2TargetRot;
        ClothRopeGroup.position = clothRopeTargetPos;
        for (int i = 0; i < 4; i++)
            ropes[i].position = ropeTarget[i];
    }

    IEnumerator LowerSequenceCoroutine()
    {
        float duration = 5.0f;
        float elapsed = 0f;
        StartCoroutine(MalePoseLowerAnimationCoroutine(duration));

        // LiftGameObject x轴旋转 +45°
        Quaternion liftStartRot = liftGameObject.rotation;
        Quaternion liftTargetRot = Quaternion.AngleAxis(45f, liftGameObject.right) * liftStartRot;

        // base_2 向下移动 0.5f
        Vector3 baseStartPos = patient_lift_base_2_pivot.position;
        Vector3 baseTargetPos = baseStartPos - new Vector3(0f, 0.5f, 0f);

        // base_1 / base_2 x轴旋转 +2.4°
        Quaternion base1StartRot = patient_lift_base_1_pivot.rotation;
        Quaternion base2StartRot = patient_lift_base_2_pivot.rotation;
        Quaternion base1TargetRot = Quaternion.AngleAxis(2.4f, patient_lift_base_1_pivot.right) * base1StartRot;
        Quaternion base2TargetRot = Quaternion.AngleAxis(2.4f, patient_lift_base_2_pivot.right) * base2StartRot;

        // topPivotChild x轴旋转 0°
        Quaternion topStartRot = topPivotChild.rotation;
        Quaternion topTargetRot = Quaternion.AngleAxis(-0f, topPivotChild.right) * topStartRot;

        // ClothRope 四点下落 0.6f + 向中心聚拢 0.24f
        Vector3[] ropeStart = new Vector3[4];
        Vector3[] ropeTarget = new Vector3[4];
        Transform[] ropes = new Transform[] { ClothRope0, ClothRope1, ClothRope2, ClothRope3 };

        Vector3 center = (ClothRope0.position + ClothRope1.position + ClothRope2.position + ClothRope3.position) / 4f;
        for (int i = 0; i < 4; i++)
        {
            ropeStart[i] = ropes[i].position;
            Vector3 dirToCenter = (center - ropeStart[i]).normalized;
            ropeTarget[i] = ropeStart[i] + dirToCenter * 0.24f - new Vector3(0f, 0.6f, 0f);
        }

        while (elapsed < duration)
        {
            float t = elapsed / duration;
            liftGameObject.rotation = Quaternion.Slerp(liftStartRot, liftTargetRot, t);
            topPivotChild.rotation = Quaternion.Slerp(topStartRot, topTargetRot, t);
            patient_lift_base_2_pivot.position = Vector3.Lerp(baseStartPos, baseTargetPos, t);
            patient_lift_base_1_pivot.rotation = Quaternion.Slerp(base1StartRot, base1TargetRot, t);
            patient_lift_base_2_pivot.rotation = Quaternion.Slerp(base2StartRot, base2TargetRot, t);
            for (int i = 0; i < 4; i++)
                ropes[i].position = Vector3.Lerp(ropeStart[i], ropeTarget[i], t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Snap to final
        liftGameObject.rotation = liftTargetRot;
        topPivotChild.rotation = topTargetRot;
        patient_lift_base_2_pivot.position = baseTargetPos;
        patient_lift_base_1_pivot.rotation = base1TargetRot;
        patient_lift_base_2_pivot.rotation = base2TargetRot;
        for (int i = 0; i < 4; i++)
            ropes[i].position = ropeTarget[i];
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

    // === Utility: Setup Rope start position ===
    IEnumerator LowerRopeLiftGroupSmooth(float distance = 1.5f, float duration = 1.0f)
    {
        Vector3 startPos = RopeLiftGroup.position;
        Vector3 targetPos = startPos + new Vector3(0f, -distance, 0f);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float t = elapsed / duration;
            RopeLiftGroup.position = Vector3.Lerp(startPos, targetPos, t);
            elapsed += Time.deltaTime;
            yield return null;
        }
        RopeLiftGroup.position = targetPos;
    }


    IEnumerator AttachRope(Transform rope, Transform target)
    {
        yield return StartCoroutine(MoveTransformTo(ikTargetPoint, rope.position, 1f * ikMoveSpeedFactor));
        yield return new WaitForSeconds(0.5f);
        Vector3 targetPos = target.position;
        yield return StartCoroutine(MoveTransformToDual(ikTargetPoint, rope, targetPos, 1f * ikMoveSpeedFactor));
    }
    IEnumerator MoveIKToAboveKinova()
    {
        Transform baseLink = RobotRoot.Find("base_link");
        if (baseLink != null)
        {
            Vector3 above = baseLink.position + new Vector3(0f, 0.6f, 0f);
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
    IEnumerator DropRopeLift(Transform ropeLift)
    {
        float duration = 2f;
        Vector3 start = ropeLift.position;
        Vector3 end = new Vector3(start.x, -0.1f, start.z);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float t = elapsed / duration;
            ropeLift.position = Vector3.Lerp(start, end, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        ropeLift.position = end;
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

        TransferStepLog log = new TransferStepLog
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
        // /home/qiandaoliu/Transferring/JSON
        string baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Transferring", "JSON"
        );
        if (!Directory.Exists(baseDir))
            Directory.CreateDirectory(baseDir);

        int index = 1;
        string dir;
        do
        {
            dir = Path.Combine(baseDir, $"transfer_{index:000}");
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
        Debug.Log($"✅ Transfer log saved to: {path}");
    }

}
