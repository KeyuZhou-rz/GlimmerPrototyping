using System.Collections.Generic;
using UnityEngine;

namespace GlimmerDiary.Flora
{
    /// <summary>
    /// Main controller for a single plant instance.
    /// Handles generation, growth animation, and runtime updates.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class PlantController : MonoBehaviour
    {
        [Header("Plant Configuration")]
        public PlantDefinition plantDefinition;
        
        [Header("Emotion Input")]
        [Range(-1f, 1f)] public float valence = 0.5f;
        [Range(0f, 1f)] public float arousal = 0.3f;
        
        [Header("Generation Settings")]
        public int randomSeed = 0;
        public bool autoGenerateOnStart = true;
        public bool generateFoliage = true;
        
        [Header("Growth Animation")]
        [Range(0f, 1f)] public float growthProgress = 1f;
        public bool animateOnGenerate = true;
        
        [Header("Wind")]
        [Tooltip("Toggles the shader's vertex wind animation for this plant. Off = no sway (use to confirm the sway source).")]
        public bool enableWind = false;

        [Header("Debug")]
        public bool showFoliagePoints = false;
        
        // Components
        private MeshFilter _trunkMeshFilter;
        private MeshRenderer _trunkRenderer;
        private Material _trunkMaterial;
        
        // Foliage (separate GameObject)
        private GameObject _foliageObject;
        private MeshFilter _foliageMeshFilter;
        private MeshRenderer _foliageRenderer;
        private Material _foliageMaterial;
        
        // Generation data
        private List<TreeMeshBuilder.FoliageAttachmentPoint> _foliagePoints;
        private TurtleInterpreter3D _interpreter;
        private TreeMeshBuilder _meshBuilder;
        private FoliageGenerator _foliageGenerator;
        
        // Shader property IDs
        private static readonly int GrowthProgressID = Shader.PropertyToID("_GrowthProgress");
        private static readonly int ValenceID = Shader.PropertyToID("_Valence");
        private static readonly int ArousalID = Shader.PropertyToID("_Arousal");
        
        private void Awake()
        {
            _trunkMeshFilter = GetComponent<MeshFilter>();
            _trunkRenderer = GetComponent<MeshRenderer>();
            
            _interpreter = new TurtleInterpreter3D();
            _meshBuilder = new TreeMeshBuilder();
            _foliageGenerator = new FoliageGenerator();
        }
        
        private void Start()
        {
            if (autoGenerateOnStart && plantDefinition != null)
            {
                Generate();
                
                if (animateOnGenerate)
                {
                    growthProgress = 0f;
                }
            }
        }
        
        private void Update()
        {
            // Update growth animation
            if (growthProgress < 1f && plantDefinition != null)
            {
                float growthSpeed = 1f / plantDefinition.growth.fullGrowthDuration;
                growthProgress += Time.deltaTime * growthSpeed;
                growthProgress = Mathf.Clamp01(growthProgress);
                
                // Apply growth curve
                float curvedProgress = plantDefinition.growth.growthCurve.Evaluate(growthProgress);
                UpdateGrowth(curvedProgress);
            }
            
            // Update emotion-driven properties
            UpdateEmotionProperties();
        }
        
        [ContextMenu("Generate Plant")]
        public void Generate()
        {
            if (plantDefinition == null)
            {
                Debug.LogError("PlantDefinition not assigned!");
                return;
            }
            
            // Calculate emotional scale factor
            float emotionalScale = CalculateEmotionalScale();
            int iterations = CalculateIterations();
            
            // Generate L-System string
            string lSystemString = LSystemGenerator.Generate(plantDefinition, iterations, randomSeed);
            Debug.Log($"[{plantDefinition.plantName}] L-System length: {lSystemString.Length}, Iterations: {iterations}");
            
            // Interpret to branch segments
            var segments = _interpreter.Interpret(lSystemString, plantDefinition, emotionalScale, randomSeed);
            Debug.Log($"[{plantDefinition.plantName}] Generated {segments.Count} branch segments");
            
            // Build trunk mesh
            var meshResult = _meshBuilder.Build(segments, plantDefinition);
            _foliagePoints = meshResult.foliagePoints;
            
            // Apply trunk mesh
            CleanupMesh(_trunkMeshFilter.sharedMesh);
            _trunkMeshFilter.sharedMesh = meshResult.trunkMesh;
            
            // Setup trunk material
            SetupTrunkMaterial();
            
            // Generate foliage
            if (generateFoliage && plantDefinition.foliage.leafShape != LeafShape.None)
            {
                GenerateFoliage();
            }
            
            Debug.Log($"[{plantDefinition.plantName}] Trunk: {meshResult.trunkMesh.vertexCount} verts, Foliage points: {_foliagePoints.Count}");
        }
        
        private void GenerateFoliage()
        {
            // Create or get foliage GameObject
            if (_foliageObject == null)
            {
                _foliageObject = new GameObject("Foliage");
                _foliageObject.transform.SetParent(transform, false);
                _foliageMeshFilter = _foliageObject.AddComponent<MeshFilter>();
                _foliageRenderer = _foliageObject.AddComponent<MeshRenderer>();
            }
            
            // Generate foliage mesh
            var foliageResult = _foliageGenerator.Generate(_foliagePoints, plantDefinition, randomSeed);
            
            CleanupMesh(_foliageMeshFilter.sharedMesh);
            _foliageMeshFilter.sharedMesh = foliageResult.combinedMesh;
            
            // Setup foliage material
            SetupFoliageMaterial();
            
            Debug.Log($"[{plantDefinition.plantName}] Foliage: {foliageResult.combinedMesh.vertexCount} verts");
        }
        
        private void SetupTrunkMaterial()
        {
            if (plantDefinition.trunk.barkMaterial != null)
            {
                _trunkMaterial = new Material(plantDefinition.trunk.barkMaterial);
            }
            else
            {
                // Create default vegetation material
                _trunkMaterial = new Material(Shader.Find("GlimmerDiary/Vegetation"));
            }
            
            _trunkMaterial.SetColor("_BaseColor", plantDefinition.trunk.barkTintBase);
            _trunkMaterial.SetFloat("_EnableSSS", 0);  // No SSS for bark
            _trunkMaterial.SetFloat("_EnableWind", enableWind ? 1 : 0);
            _trunkMaterial.SetFloat("_TrunkStiffness", plantDefinition.windResponse.trunkSwayAmount);
            
            _trunkRenderer.sharedMaterial = _trunkMaterial;
        }
        
        private void SetupFoliageMaterial()
        {
            if (plantDefinition.foliage.leafMaterial != null)
            {
                _foliageMaterial = new Material(plantDefinition.foliage.leafMaterial);
            }
            else
            {
                _foliageMaterial = new Material(Shader.Find("GlimmerDiary/Vegetation"));
            }
            
            _foliageMaterial.SetColor("_BaseColor", plantDefinition.foliage.leafColorBase);
            _foliageMaterial.SetFloat("_EnableSSS", plantDefinition.foliage.useSubsurfaceScattering ? 1 : 0);
            _foliageMaterial.SetFloat("_EnableWind", enableWind ? 1 : 0);
            _foliageMaterial.SetColor("_SSSColor", plantDefinition.foliage.leafColorTip);
            _foliageMaterial.SetFloat("_Translucency", plantDefinition.foliage.translucency);
            _foliageMaterial.SetFloat("_TrunkStiffness", 0);  // Leaves are flexible
            _foliageMaterial.SetFloat("_AlphaClip", 1);
            
            _foliageRenderer.sharedMaterial = _foliageMaterial;
        }
        
        private float CalculateEmotionalScale()
        {
            // Valence affects overall health/size
            float valenceEffect = Mathf.Lerp(0.6f, 1.2f, (valence + 1f) * 0.5f);
            
            // Plant's affinity modifies how much it responds
            float affinity = plantDefinition.valenceAffinity;
            float scale = Mathf.Lerp(1f, valenceEffect, Mathf.Abs(affinity));
            
            // Negative affinity plants thrive in negative emotions
            if (affinity < 0)
            {
                scale = Mathf.Lerp(0.6f, 1.2f, (1f - valence) * 0.5f);
            }
            
            return scale;
        }
        
        private int CalculateIterations()
        {
            var preset = plantDefinition.lSystemPreset;
            
            // Emotional intensity affects complexity
            float intensity = Mathf.Abs(valence) * 0.5f + arousal * 0.5f;
            
            int iterations = Mathf.RoundToInt(Mathf.Lerp(
                preset.minIterations,
                preset.maxIterations,
                intensity
            ));
            
            return Mathf.Clamp(iterations, preset.minIterations, preset.maxIterations);
        }
        
        private void UpdateGrowth(float progress)
        {
            if (_trunkMaterial != null)
            {
                _trunkMaterial.SetFloat(GrowthProgressID, progress);
            }
            
            // Foliage appears later in growth
            if (_foliageMaterial != null && plantDefinition != null)
            {
                float foliageDelay = plantDefinition.growth.foliageDelay;
                float foliageProgress = Mathf.InverseLerp(foliageDelay, 1f, progress);
                _foliageMaterial.SetFloat(GrowthProgressID, foliageProgress);
            }
        }
        
        private void UpdateEmotionProperties()
        {
            if (_trunkMaterial != null)
            {
                _trunkMaterial.SetFloat(ValenceID, valence);
                _trunkMaterial.SetFloat(ArousalID, arousal);
            }
            
            if (_foliageMaterial != null)
            {
                _foliageMaterial.SetFloat(ValenceID, valence);
                _foliageMaterial.SetFloat(ArousalID, arousal);
            }
        }
        
        /// <summary>
        /// Set emotion state and optionally regenerate
        /// </summary>
        public void SetEmotion(float newValence, float newArousal, bool regenerate = false)
        {
            valence = Mathf.Clamp(newValence, -1f, 1f);
            arousal = Mathf.Clamp01(newArousal);
            
            if (regenerate)
            {
                Generate();
            }
        }
        
        /// <summary>
        /// Trigger growth animation from beginning
        /// </summary>
        [ContextMenu("Play Growth Animation")]
        public void PlayGrowthAnimation()
        {
            growthProgress = 0f;
        }
        
        /// <summary>
        /// Immediately set to full growth
        /// </summary>
        [ContextMenu("Complete Growth")]
        public void CompleteGrowth()
        {
            growthProgress = 1f;
            UpdateGrowth(1f);
        }
        
        private void CleanupMesh(Mesh mesh)
        {
            bool meshIsAsset = false;
#if UNITY_EDITOR
            meshIsAsset = mesh != null && UnityEditor.AssetDatabase.Contains(mesh);
#endif
            if (mesh != null && !meshIsAsset)
            {
                DestroyImmediate(mesh);
            }
        }
        
        private void OnDestroy()
        {
            CleanupMesh(_trunkMeshFilter?.sharedMesh);
            
            if (_foliageObject != null)
            {
                CleanupMesh(_foliageMeshFilter?.sharedMesh);
                DestroyImmediate(_foliageObject);
            }
            
            if (_trunkMaterial != null) DestroyImmediate(_trunkMaterial);
            if (_foliageMaterial != null) DestroyImmediate(_foliageMaterial);
        }
        
        private void OnDrawGizmosSelected()
        {
            if (!showFoliagePoints || _foliagePoints == null) return;
            
            Gizmos.color = Color.green;
            foreach (var point in _foliagePoints)
            {
                Vector3 worldPos = transform.TransformPoint(point.position);
                Gizmos.DrawWireSphere(worldPos, 0.05f * point.scale);
                Gizmos.DrawRay(worldPos, point.normal * 0.2f);
            }
        }
        
        private void OnValidate()
        {
            // Update materials in editor when values change
            if (Application.isPlaying)
            {
                UpdateGrowth(plantDefinition?.growth.growthCurve.Evaluate(growthProgress) ?? growthProgress);
                UpdateEmotionProperties();
            }

            // Wind toggle applies live without regenerating
            float windValue = enableWind ? 1 : 0;
            if (_trunkMaterial != null) _trunkMaterial.SetFloat("_EnableWind", windValue);
            if (_foliageMaterial != null) _foliageMaterial.SetFloat("_EnableWind", windValue);
        }
    }
}
