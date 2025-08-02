using UnityEngine;
using System.Collections.Generic;

public class FurnitureSwitcher : MonoBehaviour
{
    public Transform bedSlot;
    public Transform chairSlot;
    public Transform tableSlot;

    private List<GameObject> beds = new List<GameObject>();
    private List<GameObject> chairs = new List<GameObject>();
    private List<GameObject> tables = new List<GameObject>();

    private GameObject currentBed, currentChair, currentTable;

    private Vector3 bedInitPos, chairInitPos, tableInitPos;
    private Quaternion bedInitRot, chairInitRot, tableInitRot;

    public Transform robot;
    private Vector3 robotInitPos;
    private Quaternion robotInitRot;


    void Start()
    {
        // Hide Slot visuals
        HidePlaceholderVisual(bedSlot);
        HidePlaceholderVisual(chairSlot);
        HidePlaceholderVisual(tableSlot);

        // Record initial poses
        bedInitPos = bedSlot.position;
        chairInitPos = chairSlot.position;
        tableInitPos = tableSlot.position;

        bedInitRot = bedSlot.rotation;
        chairInitRot = chairSlot.rotation;
        tableInitRot = tableSlot.rotation;

        // Load prefabs
        beds.AddRange(Resources.LoadAll<GameObject>("Beds"));
        chairs.AddRange(Resources.LoadAll<GameObject>("Chairs"));
        tables.AddRange(Resources.LoadAll<GameObject>("Tables"));

        // record robot init state
        robotInitPos = robot.position;
        robotInitRot = robot.rotation;

        SwitchFurniture(); // Initial placement
    }

    void HidePlaceholderVisual(Transform slot)
    {
        var renderer = slot.GetComponent<MeshRenderer>();
        if (renderer != null)
            renderer.enabled = false;
    }

    public void SwitchFurniture()
    {
        if (currentBed) Destroy(currentBed);
        if (currentChair) Destroy(currentChair);
        if (currentTable) Destroy(currentTable);

        currentBed = InstantiateAndFit(RandomChoice(beds), bedSlot, bedInitPos, bedInitRot);
        currentChair = InstantiateAndFit(RandomChoice(chairs), chairSlot, chairInitPos, chairInitRot);
        currentTable = InstantiateAndFit(RandomChoice(tables), tableSlot, tableInitPos, tableInitRot);

        ResetRobot();
    }

    GameObject InstantiateAndFit(GameObject prefab, Transform slot, Vector3 targetPos, Quaternion targetRot)
    {
        GameObject obj = Instantiate(prefab, targetPos, targetRot);

        // Add Rigidbody
        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb == null)
            rb = obj.AddComponent<Rigidbody>();

        rb.mass = 1f;
        rb.useGravity = false;
        rb.isKinematic = false;
        rb.detectCollisions = false;

        // Add Collider
        Collider col = obj.GetComponentInChildren<Collider>();
        if (col == null)
        {
            col = obj.AddComponent<BoxCollider>();
        }
        col.enabled = false;

        // Resize
        Renderer renderer = obj.GetComponentInChildren<Renderer>();
        if (renderer == null) return obj;

        Vector3 actualSize = renderer.bounds.size;
        Vector3 targetSize = slot.localScale;

        if (slot == tableSlot)
        {
            // Use Y height for scaling
            if (actualSize.y > 0)
            {
                float scaleFactor = targetSize.y / actualSize.y;
                obj.transform.localScale *= scaleFactor;
            }
        }
        else
        {
            // Use volume-based scaling
            float actualVolume = actualSize.x * actualSize.y * actualSize.z;
            float targetVolume = targetSize.x * targetSize.y * targetSize.z;

            if (actualVolume > 0)
            {
                float volumeRatio = targetVolume / actualVolume;
                float scaleFactor = Mathf.Pow(volumeRatio, 1f / 3f);
                obj.transform.localScale *= scaleFactor;
            }
        }

        return obj;

    }

    GameObject RandomChoice(List<GameObject> list)
    {
        if (list.Count == 0) return null;
        return list[Random.Range(0, list.Count)];
    }
    
    void ResetRobot()
    {
        robot.position = robotInitPos;
        robot.rotation = robotInitRot;

        Rigidbody rb = robot.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

}
