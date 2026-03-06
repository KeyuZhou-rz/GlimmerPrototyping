using System.Collections.Generic;
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
        
        [Header("Plant Prefabs")]
        public PlantDefinition baobabDefinition;
        public PlantDefinition acaciaDefinition;
        public PlantDefinition shrubDefinition;
        
        [Header("Density Settings")]
        [Range(0, 5)] public int majorTreeCount = 1;      // Baobab - centerpiece
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
        
        // Generated plants
        private List<PlantController> _majorTrees = new();
        private List<PlantController> _mediumTrees = new();
        private List<PlantController> _shrubs = new();
        private List<Vector3> _occupiedPositions = new();
        
        // Wind system reference
        private WindSystem _windSystem;
        
        private void Start()
        {
            _windSystem = FindFirstObjectByType<WindSystem>();
            
            if (_windSystem == null)
            {
                // Create wind system if not present
                var windGO = new GameObject("WindSystem");
                _windSystem = windGO.AddComponent<WindSystem>();
            }
            
            if (generateOnStart)
            {
                GenerateEcosystem();
            }
        }
        
        [ContextMenu("Generate Ecosystem")]
        public void GenerateEcosystem()
        {
            ClearEcosystem();
            _occupiedPositions.Clear();
            
            Debug.Log($"Generating ecosystem: {majorTreeCount} major, {mediumTreeCount} medium, {shrubCount} shrubs");
            
            // 1. Place centerpiece (Baobab) at or near center
            PlaceMajorTrees();
            
            // 2. Scatter medium trees (Acacia)
            PlaceMediumTrees();
            
            // 3. Fill with shrubs
            PlaceShrubs();
            
            // 4. Setup grass
            SetupGrass();
            
            // 5. Animate growth with staggered timing
            if (animateGrowthOnGenerate)
            {
                StartCoroutine(StaggeredGrowthAnimation());
            }
            
            Debug.Log($"Ecosystem generated: {_majorTrees.Count + _mediumTrees.Count + _shrubs.Count} plants");
        }
        
        private void PlaceMajorTrees()
        {
            if (baobabDefinition == null || majorTreeCount == 0) return;
            
            for (int i = 0; i < majorTreeCount; i++)
            {
                // First baobab at center, others scattered
                Vector3 position = i == 0 
                    ? GetGroundPosition(Vector3.zero) 
                    : FindValidPosition(15f, 10);
                
                if (position != Vector3.negativeInfinity)
                {
                    var plant = CreatePlant(baobabDefinition, position, $"Baobab_{i}");
                    plant.transform.localScale = Vector3.one * Random.Range(1.5f, 2.5f);  // Baobabs are big
                    _majorTrees.Add(plant);
                    _occupiedPositions.Add(position);
                }
            }
        }
        
        private void PlaceMediumTrees()
        {
            if (acaciaDefinition == null) return;
            
            for (int i = 0; i < mediumTreeCount; i++)
            {
                Vector3 position = FindValidPosition(minDistanceBetweenTrees, 20);
                
                if (position != Vector3.negativeInfinity)
                {
                    var plant = CreatePlant(acaciaDefinition, position, $"Acacia_{i}");
                    plant.transform.localScale = Vector3.one * Random.Range(0.8f, 1.3f);
                    _mediumTrees.Add(plant);
                    _occupiedPositions.Add(position);
                }
            }
        }
        
        private void PlaceShrubs()
        {
            if (shrubDefinition == null) return;
            
            for (int i = 0; i < shrubCount; i++)
            {
                Vector3 position = FindValidPosition(minDistanceBetweenTrees * 0.5f, 15);
                
                if (position != Vector3.negativeInfinity)
                {
                    var plant = CreatePlant(shrubDefinition, position, $"Shrub_{i}");
                    plant.transform.localScale = Vector3.one * Random.Range(0.5f, 1.2f);
                    _shrubs.Add(plant);
                    _occupiedPositions.Add(position);
                }
            }
        }
        
        private PlantController CreatePlant(PlantDefinition definition, Vector3 position, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
            
            // Add required components
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            
            var controller = go.AddComponent<PlantController>();
            controller.plantDefinition = definition;
            controller.valence = globalValence;
            controller.arousal = globalArousal;
            controller.randomSeed = Random.Range(0, 100000);
            controller.autoGenerateOnStart = false;  // We'll trigger manually
            controller.animateOnGenerate = false;
            
            // Generate the plant
            controller.Generate();
            
            return controller;
        }
        
        private Vector3 FindValidPosition(float minDistance, int maxAttempts)
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                // Random position within area
                float x = Random.Range(-areaSize.x / 2, areaSize.x / 2);
                float z = Random.Range(-areaSize.y / 2, areaSize.y / 2);
                Vector3 candidate = new Vector3(x, 0, z);
                
                // Check distance from center (keep baobab area clear)
                if (_majorTrees.Count > 0 && candidate.magnitude < minDistanceFromCenter)
                {
                    continue;
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
            
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 200f, groundLayer))
            {
                return hit.point;
            }
            
            // Fallback to y=0 if no ground found
            return new Vector3(xzPosition.x, 0f, xzPosition.z);
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
            // Combine all plants
            var allPlants = new List<PlantController>();
            allPlants.AddRange(_majorTrees);
            allPlants.AddRange(_mediumTrees);
            allPlants.AddRange(_shrubs);
            
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
            foreach (var plant in _majorTrees)
                plant.SetEmotion(globalValence, globalArousal);
            
            foreach (var plant in _mediumTrees)
                plant.SetEmotion(globalValence, globalArousal);
            
            foreach (var plant in _shrubs)
                plant.SetEmotion(globalValence, globalArousal);
            
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
            foreach (var plant in _majorTrees)
                if (plant != null) DestroyImmediate(plant.gameObject);
            
            foreach (var plant in _mediumTrees)
                if (plant != null) DestroyImmediate(plant.gameObject);
            
            foreach (var plant in _shrubs)
                if (plant != null) DestroyImmediate(plant.gameObject);
            
            _majorTrees.Clear();
            _mediumTrees.Clear();
            _shrubs.Clear();
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
