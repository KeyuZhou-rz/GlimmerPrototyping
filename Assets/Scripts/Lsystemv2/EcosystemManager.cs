using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace GlimmerDiary.Flora
{
    /// <summary>
    /// Manages an entire ecosystem - spawns and controls all flora in a scene.
    /// Handles procedural placement, emotion-driven updates, and performance.
    /// </summary>
    public class EcosystemManager : MonoBehaviour
    {
        [Header("Scene Configuration")]
        public BiomePreset biomePreset;
        public Vector2 areaSize = new Vector2(100f, 100f);
        public Transform groundPlane;

        [Header("Terrain Alignment")]
        [Tooltip("Auto-found if left empty. Placement is centered on this terrain.")]
        public TerrainGenerator terrain;
        [Tooltip("areaSize follows the terrain's world-space footprint (width*scale).")]
        public bool matchTerrainSize = true;
        
        [Header("Plant Prefabs")]
        public PlantDefinition baobabDefinition;
        public PlantDefinition acaciaDefinition;
        public PlantDefinition shrubDefinition;
        
        [Header("Density Settings")]
        public int majorTreeCount = 1;      // Baobab - centerpiece
        [Range(0, 20)] public int mediumTreeCount = 8;    // Acacia scattered
        [Range(0, 50)] public int shrubCount = 20;        // Shrubs
        
        [Header("Grass")]
        public GrassSystem grassSystem;
        [Range(10, 200)] public int grassDensityPerUnit = 50;
        
        [Header("Global Emotion State")]
        [Range(-1f, 1f)] public float globalValence = 0.5f;
        [Range(0f, 1f)] public float globalArousal = 0.3f;
        
        [Header("Placement")]
        public float minDistanceBetweenTrees = 5f;
        public float minDistanceFromCenter = 8f;  // Keep area around baobab clear
        public LayerMask groundLayer;
        
        [Header("Runtime")]
        public bool generateOnStart = true;
        public bool animateGrowthOnGenerate = true;
        public float growthStaggerDelay = 0.5f;  // Delay between each plant starting growth
        
        // Active plant instances in the scene (runtime GameObjects)
        private List<PlantController> _activePlants = new();
        private List<Vector3> _occupiedPositions = new();

        // Source of truth: data for every plant in the world. Append-only (trees are permanent).
        private List<TreeConfigData> _TreeConfigs = new();

        // definitionID -> generation rule. Used to resolve plantDefinition when spawning from data.
        private Dictionary<string, PlantDefinition> _definitionsByID = new();

        // True once the centerpiece (baobab) exists, so placement keeps the center clear.
        private bool _centerpiecePlaced;

        // World-space center of the placement area (terrain center, or this transform).
        private Vector3 _areaCenter;

        // Wind system reference
        private WindSystem _windSystem;

        private void Awake()
        {
            BuildDefinitionRegistry();
        }

        private void Start()
        {
            _windSystem = FindFirstObjectByType<WindSystem>();
            
            if (_windSystem == null)
            {
                // Create wind system if not present
                var windGO = new GameObject("WindSystem");
                _windSystem = windGO.AddComponent<WindSystem>();
            }
            
            AlignToTerrain();

            if (generateOnStart)
            {
                GenerateEcosystem();
            }
        }

        // Center the placement area on the terrain so the gizmo, the baobab,
        // and the actual spawn region all agree. Without this, placement used
        // world origin while the gizmo drew around transform.position.
        private void AlignToTerrain()
        {
            if (terrain == null) terrain = FindFirstObjectByType<TerrainGenerator>();

            if (terrain != null)
            {
                Vector3 terrainCenter = terrain.transform.position;
                if (!terrain.centerMesh)
                {
                    terrainCenter += new Vector3(
                        terrain.width * terrain.scale * 0.5f, 0f,
                        terrain.depth * terrain.scale * 0.5f);
                }

                transform.position = terrainCenter;   // keep gizmo + placement in sync

                if (matchTerrainSize)
                {
                    areaSize = new Vector2(
                        terrain.width * terrain.scale,
                        terrain.depth * terrain.scale);
                }
            }

            _areaCenter = transform.position;
        }
        
        // ── Entry point 1: first-time generation ──────────────────────────────
        // Layer A decides placement/randomness and produces config data;
        // Layer B (SpawnAll) turns that data into GameObjects.
        [ContextMenu("Generate Ecosystem")]


        public void GenerateEcosystem()
        {
            ClearEcosystem();
            AlignToTerrain();
            Debug.Log($"Generating ecosystem: {majorTreeCount} major, {mediumTreeCount} medium, {shrubCount} shrubs");
            var configs = BuildInitialConfigs();
            _TreeConfigs.AddRange(configs);
            SpawnAll(configs);

            SetupGrass();

            if (animateGrowthOnGenerate) StartCoroutine(StaggeredGrowthAnimation());

            Debug.Log($"The Ecosystem Generated: {_activePlants.Count} plants.");
        }

        // ── Entry point 2: load from saved data (no randomness, no placement) ──
        // savedConfigs are already the source of truth, so they are not regenerated.
        public void LoadEcosystem(List<TreeConfigData> savedConfigs)
        {
            if (savedConfigs == null) return;

            ClearEcosystem();
            _TreeConfigs.AddRange(savedConfigs);

            foreach (TreeConfigData cfg in savedConfigs)
            {
                _occupiedPositions.Add(cfg.location);
                if (baobabDefinition != null && cfg.definitionID == baobabDefinition.definitionID)
                    _centerpiecePlaced = true;
            }

            SpawnAll(savedConfigs);
            SetupGrass();

            if (animateGrowthOnGenerate)
            {
                StartCoroutine(StaggeredGrowthAnimation());
            }

            Debug.Log($"Ecosystem loaded: {_activePlants.Count} plants from saved data");
        }

        // ── Layer A: placement — owns all randomness, produces data only ──────
        // Reserves each chosen spot in _occupiedPositions so later picks avoid it.
        private List<TreeConfigData> BuildInitialConfigs()
        {
            var configs = new List<TreeConfigData>();

            var major = CreateMajorConfig();
            if (major != null)
            {
                configs.Add(major);
                _occupiedPositions.Add(major.location);
                _centerpiecePlaced = true;
            }

            for (int i = 0; i < mediumTreeCount; i++)
            {
                var cfg = CreateAcaciaConfig();
                if (cfg == null) continue;
                configs.Add(cfg);
                _occupiedPositions.Add(cfg.location);
            }

            for (int i = 0; i < shrubCount; i++)
            {
                var cfg = CreateShrubConfig();
                if (cfg == null) continue;
                configs.Add(cfg);
                _occupiedPositions.Add(cfg.location);
            }

            return configs;
        }

        private TreeConfigData CreateMajorConfig()
        {
            if (baobabDefinition == null) return null;

            Vector3 position = GetGroundPosition(_areaCenter);   // centerpiece sits at the middle
            return new TreeConfigData(
                position,
                Random.Range(0f, 360f),                              // rotation
                2f,                                                  // sizeScale — baobab is the big centerpiece
                Random.Range(0, 100000),                             // seed
                System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(),    // birthTimestamp
                baobabDefinition.definitionID,                       // definitionID
                baobabDefinition);                                   // plantDefinition
        }

        private TreeConfigData CreateAcaciaConfig()
        {
            if (acaciaDefinition == null) return null;

            Vector3 position = FindValidPosition(minDistanceBetweenTrees, 20);
            if (position == Vector3.negativeInfinity) return null;   // no valid spot found

            return new TreeConfigData(
                position,
                Random.Range(0f, 360f),
                Random.Range(0.8f, 1.3f),                            // acacia size range
                Random.Range(0, 100000),
                System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                acaciaDefinition.definitionID,
                acaciaDefinition);
        }

        private TreeConfigData CreateShrubConfig()
        {
            if (shrubDefinition == null) return null;

            Vector3 position = FindValidPosition(minDistanceBetweenTrees * 0.5f, 15);
            if (position == Vector3.negativeInfinity) return null;

            return new TreeConfigData(
                position,
                Random.Range(0f, 360f),
                Random.Range(0.5f, 1.2f),
                Random.Range(0, 100000),
                System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                shrubDefinition.definitionID,
                shrubDefinition);
        }

        // ── Entry point 3: runtime growth — a new plant is born ───────────────
        // Append to the source of truth, reserve its spot, then spawn it.
        // This is the atomic "a tree arrived" operation (CLAUDE.md: append-only).
        private PlantController AddPlant(TreeConfigData cfg)
        {
            if (cfg == null) return null;

            _TreeConfigs.Add(cfg);
            _occupiedPositions.Add(cfg.location);
            return SpawnFromConfig(cfg);
        }

        [ContextMenu("Grow New Acacia")]
        public void GrowNewAcacia() => AddPlant(CreateAcaciaConfig());

        [ContextMenu("Grow New Shrub")]
        public void GrowNewShrub() => AddPlant(CreateShrubConfig());

        // ── Layer B: spawning — shared by every path, fully deterministic ─────
        private void SpawnAll(List<TreeConfigData> configs)
        {
            foreach (var cfg in configs)
                SpawnFromConfig(cfg);
        }
        // Pure, deterministic: builds one plant GameObject from its config.
        // The single place where TreeConfigData becomes a live object — both
        // fresh generation and loading converge here.
        private PlantController SpawnFromConfig(TreeConfigData cfg)
        {
            PlantDefinition definition = ResolveDefinition(cfg);
            if (definition == null)
            {
                Debug.LogError($"[EcosystemManager] Cannot spawn '{cfg.definitionID}': no matching PlantDefinition registered.");
                return null;
            }

            var go = new GameObject(cfg.definitionID);
            go.transform.SetParent(transform);
            go.transform.position = cfg.location;
            go.transform.rotation = Quaternion.Euler(0, cfg.rotation, 0);
            go.transform.localScale = Vector3.one * cfg.sizeScale;

            // Add required components
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();

            var controller = go.AddComponent<PlantController>();
            controller.plantDefinition = definition;
            controller.valence = globalValence;
            controller.arousal = globalArousal;
            controller.randomSeed = cfg.seed;
            controller.autoGenerateOnStart = false;  // We trigger manually
            controller.animateOnGenerate = false;

            controller.Generate();

            _activePlants.Add(controller);
            return controller;
        }

        // Fresh configs already carry their PlantDefinition; loaded configs only
        // have a definitionID, which we resolve through the registry.
        private PlantDefinition ResolveDefinition(TreeConfigData cfg)
        {
            if (cfg.plantDefinition != null) return cfg.plantDefinition;
            return _definitionsByID.TryGetValue(cfg.definitionID, out var def) ? def : null;
        }

        private void BuildDefinitionRegistry()
        {
            _definitionsByID.Clear();
            Register(baobabDefinition);
            Register(acaciaDefinition);
            Register(shrubDefinition);

            void Register(PlantDefinition def)
            {
                if (def == null) return;
                if (string.IsNullOrEmpty(def.definitionID))
                {
                    Debug.LogWarning($"[EcosystemManager] PlantDefinition '{def.plantName}' has no definitionID; it cannot be resolved when loading saved data.");
                    return;
                }
                _definitionsByID[def.definitionID] = def;
            }
        }
        
        private Vector3 FindValidPosition(float minDistance, int maxAttempts)
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                // Random position within area, centered on the terrain
                float x = Random.Range(-areaSize.x / 2, areaSize.x / 2);
                float z = Random.Range(-areaSize.y / 2, areaSize.y / 2);
                Vector3 candidate = _areaCenter + new Vector3(x, 0, z);

                // Check distance from center (keep baobab area clear) — horizontal only
                if (_centerpiecePlaced)
                {
                    Vector2 fromCenter = new(candidate.x - _areaCenter.x, candidate.z - _areaCenter.z);
                    if (fromCenter.magnitude < minDistanceFromCenter) continue;
                }
                
                // Check distance from other plants
                bool valid = true;
                foreach (var pos in _occupiedPositions)
                {
                    if (Vector3.Distance(candidate, pos) < minDistance)
                    {
                        valid = false;
                        break;
                    }
                }
                
                if (valid)
                {
                    return GetGroundPosition(candidate);
                }
            }
            
            return Vector3.negativeInfinity;  // Failed to find position
        }
        
        private Vector3 GetGroundPosition(Vector3 xzPosition)
        {
            Vector3 rayStart = new Vector3(xzPosition.x, 100f, xzPosition.z);

            // If no layer is configured, raycast against everything (mask 0 hits nothing).
            int mask = groundLayer.value == 0 ? Physics.DefaultRaycastLayers : groundLayer.value;

            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 200f, mask))
            {
                return hit.point;
            }

            // No ground hit: warn (a tree at y=0 would be buried under raised terrain)
            // and fall back to the area center's height instead of world y=0.
            Debug.LogWarning($"[EcosystemManager] No ground collider hit at ({xzPosition.x:F1}, {xzPosition.z:F1}). " +
                             "Check that the terrain has a Collider and is on the Ground Layer.");
            return new Vector3(xzPosition.x, _areaCenter.y, xzPosition.z);
        }
        
        private void SetupGrass()
        {
            if (grassSystem == null)
            {
                // Create grass system if not assigned
                var grassGO = new GameObject("GrassSystem");
                grassGO.transform.SetParent(transform);
                grassSystem = grassGO.AddComponent<GrassSystem>();
            }
            
            grassSystem.areaSize = areaSize;
            grassSystem.grassDensity = grassDensityPerUnit;
            grassSystem.currentValence = globalValence;
            grassSystem.currentArousal = globalArousal;
            grassSystem.GenerateGrass();
        }
        
        private System.Collections.IEnumerator StaggeredGrowthAnimation()
        {
            // Copy so the sort can't be disturbed by plants born mid-animation
            var allPlants = new List<PlantController>(_activePlants);

            // Sort by distance from center (center grows first)
            allPlants.Sort((a, b) => 
                a.transform.position.magnitude.CompareTo(b.transform.position.magnitude));
            
            foreach (var plant in allPlants)
            {
                plant.PlayGrowthAnimation();
                yield return new WaitForSeconds(growthStaggerDelay);
            }
        }
        
        /// <summary>
        /// Update all flora based on emotion state
        /// </summary>
        public void SetEmotionState(float valence, float arousal)
        {
            globalValence = Mathf.Clamp(valence, -1f, 1f);
            globalArousal = Mathf.Clamp01(arousal);
            
            // Update wind system
            if (_windSystem != null)
            {
                _windSystem.SetEmotionState(globalValence, globalArousal);
            }
            
            // Update all plants
            foreach (var plant in _activePlants)
                if (plant != null) plant.SetEmotion(globalValence, globalArousal);

            // Update grass
            if (grassSystem != null)
            {
                grassSystem.SetEmotionState(globalValence, globalArousal);
            }
        }
        
        /// <summary>
        /// Smoothly transition to new emotion state
        /// </summary>
        public void TransitionToEmotion(float targetValence, float targetArousal, float duration)
        {
            StartCoroutine(EmotionTransitionCoroutine(targetValence, targetArousal, duration));
        }
        

        private System.Collections.IEnumerator EmotionTransitionCoroutine(
            float targetValence, float targetArousal, float duration)
        {
            float startValence = globalValence;
            float startArousal = globalArousal;
            float elapsed = 0f;
            
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                t = t * t * (3f - 2f * t);  // Smoothstep
                
                float v = Mathf.Lerp(startValence, targetValence, t);
                float a = Mathf.Lerp(startArousal, targetArousal, t);
                
                SetEmotionState(v, a);
                
                yield return null;
            }
            
            SetEmotionState(targetValence, targetArousal);
        }
        
        [ContextMenu("Clear Ecosystem")]
        public void ClearEcosystem()
        {
            foreach (var plant in _activePlants)
                if (plant != null) DestroyImmediate(plant.gameObject);

            _activePlants.Clear();
            _TreeConfigs.Clear();
            _occupiedPositions.Clear();
            _centerpiecePlaced = false;
        }
        
        private void OnValidate()
        {
            // Update emotion in editor
            if (Application.isPlaying)
            {
                SetEmotionState(globalValence, globalArousal);
            }
        }
        
        private void OnDrawGizmosSelected()
        {
            // Draw area bounds
            Gizmos.color = new Color(0.2f, 0.6f, 0.2f, 0.3f);
            Gizmos.DrawCube(transform.position, new Vector3(areaSize.x, 0.5f, areaSize.y));
            
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(transform.position, new Vector3(areaSize.x, 0.5f, areaSize.y));
            
            // Draw center clearance zone
            Gizmos.color = new Color(1f, 0.8f, 0f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, minDistanceFromCenter);
        }
    }
    
    /// <summary>
    /// Biome configuration preset
    /// </summary>
    [CreateAssetMenu(fileName = "BiomePreset", menuName = "GlimmerDiary/Flora/Biome Preset")]
    public class BiomePreset : ScriptableObject
    {
        public string biomeName = "African Savanna";
        
        [Header("Plants")]
        public PlantDefinition majorTree;
        public PlantDefinition mediumTree;
        public PlantDefinition shrub;
        public GrassPreset grass;
        
        [Header("Density")]
        public int majorTreeDensity = 1;
        public int mediumTreeDensity = 8;
        public int shrubDensity = 20;
        public int grassDensity = 50;
        
        [Header("Environment")]
        public Color ambientColor = new Color(1f, 0.95f, 0.85f);
        public Color sunColor = new Color(1f, 0.9f, 0.7f);
        public float sunIntensity = 1.2f;
        
        [Header("Atmosphere")]
        public Color fogColor = new Color(0.8f, 0.75f, 0.65f);
        public float fogDensity = 0.01f;
    }
}
