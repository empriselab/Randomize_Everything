using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class FurnitureRandomizer : MonoBehaviour
{
    public Transform wallA;
    public Transform wallB;

    private Dictionary<string, List<GameObject>> wallAGroups;
    private Dictionary<string, List<GameObject>> wallBGroups;

    void Start()
    {
        wallAGroups = GroupChildrenByPrefix(wallA);
        wallBGroups = GroupChildrenByPrefix(wallB);
    }

    public void RandomizeWalls()
    {
        if (wallA == null || wallB == null)
        {
            Debug.LogError("RandomizeWalls: wallA 或 wallB 没有在 Inspector 里赋值！");
            return;
        }

        if (wallAGroups == null)
            wallAGroups = GroupChildrenByPrefix(wallA);

        if (wallBGroups == null)
            wallBGroups = GroupChildrenByPrefix(wallB);

        ActivateRandomGroup(wallAGroups);
        ActivateRandomGroup(wallBGroups);
    }

    private Dictionary<string, List<GameObject>> GroupChildrenByPrefix(Transform parent)
    {
        var groups = new Dictionary<string, List<GameObject>>();

        foreach (Transform child in parent)
        {
            string prefix = GetNamePrefix(child.name);

            if (!groups.ContainsKey(prefix))
                groups[prefix] = new List<GameObject>();

            groups[prefix].Add(child.gameObject);
        }

        return groups;
    }

    private string GetNamePrefix(string name)
    {
        int lastUnderscore = name.LastIndexOf('_');
        if (lastUnderscore > 0)
        {
            string suffix = name.Substring(lastUnderscore + 1);
            if (int.TryParse(suffix, out _))
                return name.Substring(0, lastUnderscore);
        }
        return name;
    }

    private void ActivateRandomGroup(Dictionary<string, List<GameObject>> groups)
    {
        foreach (var group in groups.Values)
            foreach (var obj in group)
                obj.SetActive(false);

        var keys = groups.Keys.ToList();
        if (keys.Count == 0) return;

        string chosenKey = keys[Random.Range(0, keys.Count)];
        foreach (var obj in groups[chosenKey])
            obj.SetActive(true);
    }
}
