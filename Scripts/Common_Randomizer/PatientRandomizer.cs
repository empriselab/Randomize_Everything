using UnityEngine;

public class PatientRandomizer : MonoBehaviour
{
    public SkinnedMeshRenderer patientRenderer;
    public Mesh[] patientMeshes;                // mesh list
    public Material[] patientMaterials;         // material list
    public void RandomizePatient()
    {
        // random mesh
        if (patientMeshes.Length > 0)
        {
            int meshIdx = Random.Range(0, patientMeshes.Length);
            patientRenderer.sharedMesh = patientMeshes[meshIdx];
        }

        // random materials
        if (patientMaterials.Length > 0)
        {
            int matIdx = Random.Range(0, patientMaterials.Length);
            patientRenderer.material = patientMaterials[matIdx];
        }
    }
}
