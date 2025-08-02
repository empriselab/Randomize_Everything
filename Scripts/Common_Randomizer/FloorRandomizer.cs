using UnityEngine;

public class FloorRandomizer : MonoBehaviour
{
    public Renderer floorRenderer; // Assign in Inspector
    public Material[] floorMaterials; // Wood-1, Wood-2, Wood-3

    public void RandomizeFloor()
    {
        if (floorMaterials.Length > 0)
        {
            int idx = Random.Range(0, floorMaterials.Length);
            floorRenderer.material = floorMaterials[idx];
        }
    }
}