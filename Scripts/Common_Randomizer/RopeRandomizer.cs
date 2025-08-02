using UnityEngine;
using Obi;

public class RopeRandomizer : MonoBehaviour
{
    public ObiRopeExtrudedRenderer[] ropeRenderers; // Rope Renderer
    public Material[] ropeMaterials;                

    public void RandomizeAllRopes()
    {
        if (ropeMaterials.Length == 0 || ropeRenderers.Length == 0)
            return;

        int idx = Random.Range(0, ropeMaterials.Length);
        Material chosenMat = ropeMaterials[idx];

        foreach (var ropeRenderer in ropeRenderers)
        {
            if (ropeRenderer != null)
                ropeRenderer.material = chosenMat;
        }

        Debug.Log($"[RopeGroupRandomizer] Applied material: {chosenMat.name} to {ropeRenderers.Length} ropes.");
    }
}
