using UnityEngine;

public class IKTargetTagger : MonoBehaviour
{
    public string ikTargetName = "iKTargetPoint";
    public string tagName = "IKTarget";

    void Start()
    {
        Debug.Log("[IKTargetTagger] Start() called");

        GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>();
        int count = 0;

        foreach (GameObject obj in allObjects)
        {
            if (obj.name.Contains(ikTargetName))
            {
                obj.tag = tagName;
                count++;
            }
        }

        Debug.Log($"[IKTargetTagger] Tagged {count} IK targets with tag '{tagName}'");
    }
}
