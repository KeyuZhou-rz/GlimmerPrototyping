using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TreeController : MonoBehaviour
{
    [Header("Parameters")]
    public TreeParameters parameters;
    
    [Header("Emotion Input (for testing)")]
    [Range(-1f, 1f)] public float valence = 0.5f;
    [Range(0f, 1f)] public float arousal = 0.3f;
    
    [Header("Generation")]
    public int randomSeed = 42;
    public bool use3DCylinders = true;
    
    [Header("Growth Animation")]
    [Range(0f, 1f)] public float growthProgress = 1f;
    public float growthDuration = 3f;
    
    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private Material _material;
    private TurtleInterpreter _interpreter;
    
    private static readonly int GrowthProgressID = Shader.PropertyToID("_GrowthProgress");

    private void Awake()
    {
        _meshFilter = GetComponent<MeshFilter>();
        _meshRenderer = GetComponent<MeshRenderer>();
        _interpreter = new TurtleInterpreter();
        
        // Clone material to avoid modifying shared asset
        if (_meshRenderer.sharedMaterial != null)
        {
            _material = new Material(_meshRenderer.sharedMaterial);
            _meshRenderer.material = _material;
        }
    }

    [ContextMenu("Generate Tree")]
    public void GenerateTree()
    {
        if (parameters == null)
        {
            Debug.LogError("TreeParameters not assigned!");
            return;
        }

        // Map emotion to parameters
        var mappedParams = MapEmotionToParams(valence, arousal);
        
        // Generate L-System string
        string lSystemString = LSystemGenerator.GenerateStochastic(
            parameters.axiom,
            mappedParams.iterations,
            parameters.angleVariance,
            randomSeed
        );
        
        Debug.Log($"L-System string length: {lSystemString.Length}");

        // Interpret string to geometry
        var segments = _interpreter.Interpret(
            lSystemString,
            mappedParams.angle,
            mappedParams.stepLength,
            mappedParams.initialWidth,
            parameters.widthDecay,
            mappedParams.angleVariance,
            mappedParams.lengthVariance,
            randomSeed
        );
        
        Debug.Log($"Generated {segments.Count} branch segments");

        // Build mesh
        Mesh mesh = use3DCylinders 
            ? TreeMeshBuilder.Build(segments, parameters.radialSegments)
            : TreeMeshBuilder.BuildFlat(segments);
        
        // Clean up old mesh
        if (_meshFilter.sharedMesh != null && !UnityEditor.AssetDatabase.Contains(_meshFilter.sharedMesh))
        {
            DestroyImmediate(_meshFilter.sharedMesh);
        }
        
        _meshFilter.sharedMesh = mesh;
        
        Debug.Log($"Mesh created: {mesh.vertexCount} vertices, {mesh.triangles.Length / 3} triangles");
    }

    private (int iterations, float angle, float stepLength, float initialWidth, 
             float angleVariance, float lengthVariance) MapEmotionToParams(float v, float a)
    {
        // Valence affects health/fullness
        // High valence = more iterations, wider branches, golden angle
        // Low valence = fewer iterations, drooping, narrow
        
        float valenceNorm = (v + 1f) / 2f;  // Map [-1,1] to [0,1]
        
        int iterations = Mathf.RoundToInt(Mathf.Lerp(2, parameters.iterations, valenceNorm));
        float angle = Mathf.Lerp(15f, parameters.angle, valenceNorm);
        float stepLength = Mathf.Lerp(parameters.stepLength * 0.5f, parameters.stepLength, valenceNorm);
        float initialWidth = Mathf.Lerp(parameters.initialWidth * 0.6f, parameters.initialWidth, valenceNorm);
        
        // Arousal affects chaos/energy
        // High arousal = more variance, jittery
        // Low arousal = orderly, calm
        
        float angleVariance = Mathf.Lerp(0f, parameters.angleVariance, a);
        float lengthVariance = Mathf.Lerp(0f, parameters.lengthVariance, a);
        
        return (iterations, angle, stepLength, initialWidth, angleVariance, lengthVariance);
    }

    [ContextMenu("Animate Growth")]
    public void AnimateGrowth()
    {
        growthProgress = 0f;
        // In production, use PrimeTween:
        // Tween.Custom(0f, 1f, growthDuration, v => SetGrowth(v));
    }

    private void Update()
    {
        // Simple growth animation for testing (replace with PrimeTween)
        if (growthProgress < 1f)
        {
            growthProgress += Time.deltaTime / growthDuration;
            growthProgress = Mathf.Clamp01(growthProgress);
            SetGrowth(growthProgress);
        }
    }

    private void SetGrowth(float progress)
    {
        if (_material != null)
        {
            _material.SetFloat(GrowthProgressID, progress);
        }
    }

    private void OnValidate()
    {
        // Update growth in editor
        if (_material != null)
        {
            _material.SetFloat(GrowthProgressID, growthProgress);
        }
    }
}
