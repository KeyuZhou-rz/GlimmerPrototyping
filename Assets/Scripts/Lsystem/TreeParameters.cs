using Unity.VisualScripting;
using UnityEngine;
[CreateAssetMenu(fileName = "TreeParams", menuName = "GlimmerDiary/Tree Parameters")]
public class TreeParameters: ScriptableObject

{
    [Header("L-System Rules")]
    public string axiom = "X" ;
    public int iterations = 4;

    [Header("Geometry")]
    [Range(10f, 45f)] public float angle = 25f;
    [Range(0.1f, 2f)] public float stepLength = 0.5f;
    [Range(0.01f, 1f)] public float initialWidth = 1f;
    [Range(0.6f,0.95f)] public float widthDecay = 0.75f;

    [Header("Variation")]
    [Range(0f,1f)] public float angleVariance = 0.1f;
    [Range(0f,1f)] public float lengthVariance = 0.1f;

    [Header("Mesh Settings")]
    public int radialSegments = 6; // how round the branches are

}
