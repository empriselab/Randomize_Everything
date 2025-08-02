using UnityEngine;
using System.Collections;

public class FeedingController : MonoBehaviour
{
    public Transform spoon;
    public Transform mouth;
    public Transform robotRoot;  // 本房间的kinova base
    public float moveSpeed = 0.3f;
    public float closeEnoughThreshold = 0.01f;

    private Transform ikTargetPoint;
    private enum State { Idle, MoveToSpoon, WaitAfterSpoon, MoveToMouth, Done }
    private State currentState = State.Idle;

    private Vector3 spoonPickupPos;
    private Vector3 mouthTargetPos;
    private float arcTravelT = 0f;

    private Quaternion arcStartRot;
    private Quaternion arcEndRot;

    void Start()
    {
        StartCoroutine(WaitBeforeStart());
    }

    IEnumerator WaitBeforeStart()
    {
        yield return new WaitForSeconds(0.5f); // 等待场景稳定
        TryFindNearestIKTarget();
    }

    void Update()
    {
        if (ikTargetPoint == null) return;

        if (currentState == State.MoveToSpoon)
        {
            MoveTowards(spoon.position);

            if (Vector3.Distance(ikTargetPoint.position, spoon.position) < closeEnoughThreshold)
            {
                spoon.SetParent(ikTargetPoint);
                spoon.localPosition = Vector3.zero;

                currentState = State.WaitAfterSpoon;
                StartCoroutine(WaitThenMoveToMouth());
            }
        }
        else if (currentState == State.MoveToMouth)
        {
            MoveAlongArc(spoonPickupPos, mouthTargetPos, Vector3.right, 0.2f);
        }
    }

    IEnumerator WaitThenMoveToMouth()
    {
        yield return new WaitForSeconds(1.5f);
        currentState = State.MoveToMouth;

        spoonPickupPos = ikTargetPoint.position;
        mouthTargetPos = mouth.position;
        arcTravelT = 0f;

        arcStartRot = ikTargetPoint.rotation;
        arcEndRot = arcStartRot * Quaternion.Euler(-20f, 0f, 0f);
    }

    void MoveTowards(Vector3 target)
    {
        ikTargetPoint.position = Vector3.MoveTowards(
            ikTargetPoint.position,
            target,
            moveSpeed * Time.deltaTime
        );
    }

    void MoveAlongArc(Vector3 start, Vector3 end, Vector3 arcAxis, float arcHeight)
    {
        arcTravelT += moveSpeed * Time.deltaTime / Vector3.Distance(start, end);
        arcTravelT = Mathf.Clamp01(arcTravelT);

        Vector3 flat = Vector3.Lerp(start, end, arcTravelT);
        float arcOffset = Mathf.Sin(arcTravelT * Mathf.PI) * arcHeight;

        Vector3 offset = arcAxis.normalized * arcOffset;
        ikTargetPoint.position = flat + offset;

        ikTargetPoint.rotation = Quaternion.Slerp(arcStartRot, arcEndRot, arcTravelT);

        if (arcTravelT >= 1.0f)
        {
            currentState = State.Done;
            ikTargetPoint.position = mouthTargetPos;

            if (spoon != null && spoon.parent == ikTargetPoint)
            {
                spoon.SetParent(null);
                spoon.position = ikTargetPoint.position;
            }
        }
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
            float dist = Vector3.Distance(go.transform.position, robotRoot.position);
            if (dist < minDist)
            {
                minDist = dist;
                closest = go.transform;
            }
        }

        if (closest != null)
        {
            ikTargetPoint = closest;

            // 可选：初始旋转调整
            ikTargetPoint.rotation = Quaternion.Euler(
                ikTargetPoint.eulerAngles.x,
                ikTargetPoint.eulerAngles.y + 90f,
                ikTargetPoint.eulerAngles.z + 180f
            );

            currentState = State.MoveToSpoon;
            Debug.Log($"{name}: Found IK target at {minDist:F3} meters from robot.");
        }
    }

    public void ResetFeeding()
    {
        currentState = State.MoveToSpoon;
        arcTravelT = 0f;
    }
}
