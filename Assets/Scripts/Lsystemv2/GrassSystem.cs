using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GlimmerDiary.Flora
{
    /// <summary>
    /// High-performance grass system using GPU instancing.
    /// Can render hundreds of thousands of grass blades efficiently.
    /// </summary>
    public class GrassSystem : MonoBehaviour
    {
        [Header("Grass Settings")]
        public GrassPreset preset;
        
        [Header("Distribution")]
        public Vector2 areaSize = new Vector2(50f, 50f);
        public int grassDensity = 100;  // Per unit area
        public float minHeight = 0.3f;
        public float maxHeight = 1.2f;
        
        [Header("Rendering")]
        public Material grassMaterial;
        public Mesh grassBladeMesh;
        public bool castShadows = false;
        
        [Header("Wind")]
        public Vector2 windDirection = new Vector2(1, 0);
        public float windStrength = 1f;
        public float windFrequency = 1f;
        
        [Header("Emotion Response")]
        [Range(-1f, 1f)] public float currentValence = 0.5f;
        [Range(0f, 1f)] public float currentArousal = 0.3f;
        
        // Instancing data
        private List<Matrix4x4[]> _instanceBatches = new();
        private MaterialPropertyBlock _propertyBlock;
        private ComputeBuffer _argsBuffer;
        
        // Shader property IDs
        private static readonly int WindDirectionID = Shader.PropertyToID("_WindDirection");
        private static readonly int WindStrengthID = Shader.PropertyToID("_WindStrength");
        private static readonly int WindFrequencyID = Shader.PropertyToID("_WindFrequency");
        private static readonly int TimeID = Shader.PropertyToID("_Time");
        private static readonly int ValenceID = Shader.PropertyToID("_Valence");
        private static readonly int ArousalID = Shader.PropertyToID("_Arousal");
        
        private void Start()
        {
            _propertyBlock = new MaterialPropertyBlock();
            
            if (grassBladeMesh == null)
                grassBladeMesh = CreateDefaultGrassBlade();
            
            GenerateGrass();
        }
        
        [ContextMenu("Regenerate Grass")]
        public void GenerateGrass()
        {
            _instanceBatches.Clear();
            
            int totalBlades = Mathf.RoundToInt(areaSize.x * areaSize.y * grassDensity);
            int batchSize = 1023;  // Unity's max for Graphics.DrawMeshInstanced
            
            var currentBatch = new List<Matrix4x4>();
            var random = new System.Random(42);
            
            Vector3 areaMin = new Vector3(-areaSize.x / 2, 0, -areaSize.y / 2);
            
            for (int i = 0; i < totalBlades; i++)
            {
                float x = areaMin.x + (float)random.NextDouble() * areaSize.x;
                float z = areaMin.z + (float)random.NextDouble() * areaSize.y;
                
                // Raycast to find ground (if terrain exists)
                float y = 0f;
                if (Physics.Raycast(new Vector3(x, 100f, z), Vector3.down, out RaycastHit hit, 200f))
                {
                    y = hit.point.y;
                }
                
                Vector3 position = new Vector3(x, y, z);
                
                // Random rotation around Y axis
                Quaternion rotation = Quaternion.Euler(
                    (float)(random.NextDouble() * 10 - 5),   // Slight tilt
                    (float)(random.NextDouble() * 360),       // Full Y rotation
                    (float)(random.NextDouble() * 10 - 5)    // Slight tilt
                );
                
                // Random height
                float height = Mathf.Lerp(minHeight, maxHeight, (float)random.NextDouble());
                float width = height * 0.1f;
                Vector3 scale = new Vector3(width, height, width);
                
                currentBatch.Add(Matrix4x4.TRS(position, rotation, scale));
                
                if (currentBatch.Count >= batchSize)
                {
                    _instanceBatches.Add(currentBatch.ToArray());
                    currentBatch.Clear();
                }
            }
            
            // Add remaining
            if (currentBatch.Count > 0)
            {
                _instanceBatches.Add(currentBatch.ToArray());
            }
            
            Debug.Log($"Generated {totalBlades} grass blades in {_instanceBatches.Count} batches");
        }
        
        private void Update()
        {
            if (grassMaterial == null || grassBladeMesh == null) return;
            
            // Update material properties
            _propertyBlock.SetVector(WindDirectionID, new Vector4(windDirection.x, 0, windDirection.y, 0));
            _propertyBlock.SetFloat(WindStrengthID, windStrength * (0.5f + currentArousal));
            _propertyBlock.SetFloat(WindFrequencyID, windFrequency);
            _propertyBlock.SetFloat(ValenceID, currentValence);
            _propertyBlock.SetFloat(ArousalID, currentArousal);
            
            // Draw all batches
            foreach (var batch in _instanceBatches)
            {
                Graphics.DrawMeshInstanced(
                    grassBladeMesh,
                    0,
                    grassMaterial,
                    batch,
                    batch.Length,
                    _propertyBlock,
                    castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    true
                );
            }
        }
        
        /// <summary>
        /// Update grass based on emotion state
        /// </summary>
        public void SetEmotionState(float valence, float arousal)
        {
            currentValence = valence;
            currentArousal = arousal;
            
            // High arousal = more wind
            windStrength = Mathf.Lerp(0.3f, 2f, arousal);
            
            // Low valence = grass appears more wilted (handled in shader)
        }
        
        /// <summary>
        /// Creates a simple grass blade mesh
        /// </summary>
        public static Mesh CreateDefaultGrassBlade()
        {
            var mesh = new Mesh { name = "GrassBlade" };
            
            // Curved blade with multiple segments for bending
            int segments = 4;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            
            float width = 0.5f;
            
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float y = t;
                
                // Slight curve
                float curve = Mathf.Sin(t * Mathf.PI * 0.5f) * 0.1f;
                float currentWidth = Mathf.Lerp(width, 0f, t);  // Taper to point
                
                vertices.Add(new Vector3(-currentWidth, y, curve));
                vertices.Add(new Vector3(currentWidth, y, curve));
                
                normals.Add(Vector3.back);
                normals.Add(Vector3.back);
                
                uvs.Add(new Vector2(0, t));
                uvs.Add(new Vector2(1, t));
            }
            
            // Build triangles
            for (int i = 0; i < segments; i++)
            {
                int bl = i * 2;
                int br = i * 2 + 1;
                int tl = i * 2 + 2;
                int tr = i * 2 + 3;
                
                triangles.Add(bl);
                triangles.Add(tl);
                triangles.Add(br);
                
                triangles.Add(br);
                triangles.Add(tl);
                triangles.Add(tr);
            }
            
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            
            return mesh;
        }
        
        private void OnDestroy()
        {
            _argsBuffer?.Release();
        }
        
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.3f);
            Gizmos.DrawCube(transform.position, new Vector3(areaSize.x, 0.1f, areaSize.y));
            
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(transform.position, new Vector3(areaSize.x, 0.1f, areaSize.y));
        }
    }
    
    /// <summary>
    /// Preset for different grass types
    /// </summary>
    [CreateAssetMenu(fileName = "GrassPreset", menuName = "GlimmerDiary/Flora/Grass Preset")]
    public class GrassPreset : ScriptableObject
    {
        public string grassName = "Savanna Grass";
        
        [Header("Appearance")]
        public Color baseColor = new Color(0.7f, 0.6f, 0.3f);
        public Color tipColor = new Color(0.9f, 0.8f, 0.5f);
        public Gradient seasonalColorGradient;
        
        [Header("Shape")]
        [Range(0.1f, 2f)] public float heightMin = 0.3f;
        [Range(0.1f, 3f)] public float heightMax = 1.2f;
        [Range(0.01f, 0.2f)] public float bladeWidth = 0.05f;
        [Range(0f, 1f)] public float curvature = 0.3f;
        
        [Header("Wind Response")]
        [Range(0f, 1f)] public float windSensitivity = 0.7f;
        [Range(0.1f, 5f)] public float swaySpeed = 1.5f;
        
        [Header("Emotion Mapping")]
        [Tooltip("How grass responds to positive/negative emotion")]
        public AnimationCurve valenceToHealth;  // Maps valence to grass health (color, uprightness)
        public AnimationCurve arousalToMotion;  // Maps arousal to wind response
    }
}
