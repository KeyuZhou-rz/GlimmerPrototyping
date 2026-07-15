using UnityEngine;
using System.Collections.Generic;
using GlimmerDiary.Data;
using Mono.Cecil;
using Unity.VisualScripting;

namespace GlimmerDiary.Ecosystem_nonL
{
    /// <summary>
    /// The second version of ecosystem, with no L-System generation.
    /// Scatters trees across the procedural terrain by <b>rejection sampling</b>
    /// ("dart throwing"): throw N random points, compute a local density 0..1 from
    /// terrain features at each point, and keep the point with probability
    /// <c>Random.value &lt; density</c>. Dense areas keep more darts, sparse areas fewer —
    /// the spatial distribution falls out for free, no hashing or Poisson-disk needed.
    ///
    /// Three natural rules live entirely in <see cref="DensityAt"/>:
    /// trees cluster near water, stand sparse on the plains, thin out on steep high ground.
    ///
    /// Reproducibility comes from <see cref="seed"/> via Random.InitState — this is a
    /// small bounded world, not infinite streaming chunks, so per-grid hashing is unneeded.
    /// </summary>
    public class EcosystemManager : MonoBehaviour
    {
        // ---- World / terrain -------------------------------------------------
        [Header("World")]
        [Tooltip("Fallback sampling rectangle (world units) used when no Terrain is assigned.")]
        public Vector2 areaSize = new Vector2(160f, 160f);

        [Header("Terrain Alignment")]
        [Tooltip("Placement is sampled over this terrain's footprint. Auto-found if left empty.")]
        public TerrainGenerator terrain;
        [Tooltip("Derive the sampling rectangle from the terrain size instead of areaSize.")]
        public bool matchTerrainSize = true;
        [Tooltip("Optional. Provides the water surface level; falls back to terrain river math.")]
        public WaterGenerator water;

        // ---- Species prefab pools (one flattened registry at runtime) --------
        [Header("Tree near the water")]
        public List<GameObject> TreeNearTheWater = new();
        [Header("Tree in the plain")]
        public List<GameObject> TreeInPlain = new();
        [Header("Tree on the cliff & mountain")]
        public List<GameObject> TreeOnClif = new();

        // ---- Sampling --------------------------------------------------------
        [Header("Rejection Sampling")]
        [Tooltip("Locks the whole random sequence — same seed => identical layout.")]
        public int seed = 12345;
        [Tooltip("Number of dart throws. More attempts => denser maximum foliage.")]
        public int attempts = 2000;
        [Tooltip("Minimum spacing between trees (world units). 0 disables the check — " +
                 "clumping is often desirable in low-poly style.")]
        public float minDist = 3f;
        [Tooltip("Layers the ground raycast may hit (assign the terrain's layer).")]
        public LayerMask groundMask = ~0;

        // ---- Density rule parameters ----------------------------------------
        [Header("Density Rules")]
        [Tooltip("Slope (degrees) at/above which density reaches 0.")]
        public float maxSlope = 30f;
        [Tooltip("World Y at/above which density reaches 0 (the tree line).")]
        public float treeline = 8f;
        [Tooltip("World-Y band below the tree line over which density ramps 0..1.")]
        public float treelineBand = 20f;
        [Tooltip("Near-water density bonus falloff (world units).")]
        public float waterFalloff = 8f;

        [Header("Zone thresholds")]
        [Tooltip("Within this distance to water (world units) a point is the 'water' zone.")]
        public float waterZoneDistance = 6f;
        [Tooltip("Slope (degrees) at/above which a point is the 'cliff' zone.")]
        public float cliffSlopeThreshold = 18f;

        [Header("Relative density per zone (inspector knobs)")]
        [Range(0, 50)] public int nearTheRiver = 10;
        [Range(0, 50)] public int inThePlain = 10;
        [Range(0, 20)] public int onTheClif = 5;

        // ---- Emotion (terrain-only for v1; emotion gating is a future hook) ---
        [Header("Emotion")]
        public EmotionVector emotionvector;

        [Header("Debug")]
        public bool drawGizmos = true;

        private enum Zone { Water, Plain, Cliff }

        // Flattened species registry, rebuilt each Populate().
        private readonly List<GameObject> _allSpecies = new();
        private int _waterStart, _waterCount;
        private int _plainStart, _plainCount;
        private int _cliffStart, _cliffCount;

        private List<TreeData> _lastResult;

        // ---Instantiate---
        [Header("Instantiation")]
        private readonly List<GameObject> _spawnedTrees = new();
        private readonly List<float> _showAtThresholds = new();
        public Transform treeParent;
        public bool populateOnStart = true;

        public void SetRainfall(float rainfall)
        {
            for (int i = 0; i < _spawnedTrees.Count; i++)
            {
                if (_spawnedTrees[i] == null) continue;

                _spawnedTrees[i].SetActive(rainfall >= _showAtThresholds[i]);
            }
        }


        private void Awake()
        {
            if (terrain == null) terrain = FindFirstObjectByType<TerrainGenerator>();
            if (water == null && terrain != null) water = terrain.GetComponentInChildren<WaterGenerator>();
        }

        // instantiate
        private void SpawnfromData(List<TreeData> trees)
        {
            foreach(var t in trees)
            {
                if (t.speciesIndex < 0 || t.speciesIndex >= _allSpecies.Count) continue;

                var prefab = _allSpecies[t.speciesIndex];
                if (prefab == null) continue;


                // instantiate
                var go = Instantiate(prefab, t.position, Quaternion.identity, treeParent != null ? treeParent : transform);

                _spawnedTrees.Add(go);
                _showAtThresholds.Add(t.showAt);
            }
        }

        // =====================================================================
        //  Rejection sampling
        // =====================================================================
        public List<TreeData> Populate()
        {
            Random.InitState(seed);
            BuildSpeciesRegistry();

            var trees = new List<TreeData>();
            GetSampleBounds(out float minX, out float maxX, out float minZ, out float maxZ);

            for (int i = 0; i < attempts; i++)
            {
                float x = Random.Range(minX, maxX);
                float z = Random.Range(minZ, maxZ);

                if (!TrySampleGround(x, z, out Vector3 point, out Vector3 normal))
                    continue;   // missed the terrain — outside the mesh footprint

                Zone zone = ZoneAt(point, normal);
                float density = DensityAt(point, normal) * ZoneMultiplier(zone);

                if (Random.value < density && !TooClose(point, trees))
                {
                    float baseThreshold = Random.value;
                    float zonebias = zone switch
                    {
                        Zone.Water => 0.5f, //水边
                        Zone.Cliff => 1.3f,
                        _ => 1.0f, 
                    };
                    trees.Add(new TreeData
                    {
                        position = point,
                        speciesIndex = PickSpecies(zone),
                        showAt = Mathf.Clamp01(baseThreshold * zonebias)   // fixed birth-time threshold for later gating
                    });
                }
            }

            _lastResult = trees;
            return trees;
        }

        // =====================================================================
        //  Density — three lerps, one per natural rule
        // =====================================================================
        private float DensityAt(Vector3 p, Vector3 n)
        {
            float d = 1f;
            d *= Mathf.InverseLerp(maxSlope, 0f, SlopeFromNormal(n));               // steeper => 0
            d *= Mathf.InverseLerp(treeline, treeline - treelineBand, p.y);         // above tree line => 0
            d *= 0.3f + 0.7f * Mathf.Exp(-DistanceToWater(p) / Mathf.Max(0.0001f, waterFalloff)); // near water => bonus
            return Mathf.Clamp01(d);
        }

        private static float SlopeFromNormal(Vector3 n) => Vector3.Angle(n, Vector3.up);

        /// <summary>Horizontal distance (world units) from p to the river's water edge.</summary>
        private float DistanceToWater(Vector3 p)
        {
            if (terrain == null || !terrain.enableRiver) return float.PositiveInfinity;

            Vector3 offsetGrid = terrain.centerMesh
                ? new Vector3(terrain.width * 0.5f, 0f, terrain.depth * 0.5f)
                : Vector3.zero;

            // World -> terrain-local -> grid coordinates (inverse of mesh build transform).
            Vector3 local = terrain.transform.InverseTransformPoint(p);
            float gx = local.x / terrain.scale + offsetGrid.x;
            float gz = local.z / terrain.scale + offsetGrid.z;

            float nz = gz / terrain.depth;
            float centerGridX = terrain.RiverCurve(nz) * terrain.width;
            float halfWidthGrid = terrain.riverWidth * 0.5f * terrain.width;

            float distGrid = Mathf.Max(0f, Mathf.Abs(gx - centerGridX) - halfWidthGrid);
            return distGrid * terrain.scale;   // back to world units
        }

        // =====================================================================
        //  Zones & species (flattened registry)
        // =====================================================================
        private Zone ZoneAt(Vector3 p, Vector3 n)
        {
            if (DistanceToWater(p) <= waterZoneDistance) return Zone.Water;
            if (SlopeFromNormal(n) >= cliffSlopeThreshold) return Zone.Cliff;
            return Zone.Plain;
        }

        private float ZoneMultiplier(Zone zone)
        {
            // Sliders read as "relative density": default values map to ~1.0.
            return zone switch
            {
                Zone.Water => nearTheRiver / 10f,
                Zone.Cliff => onTheClif / 10f,
                _          => inThePlain / 10f,
            };
        }

        /// <summary>Returns a GLOBAL index into the flattened species registry, or -1 if empty.</summary>
        private int PickSpecies(Zone zone)
        {
            // Preferred zone first, then graceful fallback to any populated zone.
            if (TryPickInZone(zone, out int idx)) return idx;
            if (TryPickInZone(Zone.Plain, out idx)) return idx;
            if (TryPickInZone(Zone.Water, out idx)) return idx;
            if (TryPickInZone(Zone.Cliff, out idx)) return idx;
            return -1;
        }

        private bool TryPickInZone(Zone zone, out int globalIndex)
        {
            int start, count;
            switch (zone)
            {
                case Zone.Water: start = _waterStart; count = _waterCount; break;
                case Zone.Cliff: start = _cliffStart; count = _cliffCount; break;
                default:         start = _plainStart; count = _plainCount; break;
            }

            if (count <= 0) { globalIndex = -1; return false; }
            globalIndex = start + Random.Range(0, count);
            return true;
        }

        private void BuildSpeciesRegistry()
        {
            _allSpecies.Clear();

            _waterStart = _allSpecies.Count;
            if (TreeNearTheWater != null) _allSpecies.AddRange(TreeNearTheWater);
            _waterCount = _allSpecies.Count - _waterStart;

            _plainStart = _allSpecies.Count;
            if (TreeInPlain != null) _allSpecies.AddRange(TreeInPlain);
            _plainCount = _allSpecies.Count - _plainStart;

            _cliffStart = _allSpecies.Count;
            if (TreeOnClif != null) _allSpecies.AddRange(TreeOnClif);
            _cliffCount = _allSpecies.Count - _cliffStart;
        }

        // =====================================================================
        //  Geometry helpers
        // =====================================================================
        private bool TrySampleGround(float worldX, float worldZ, out Vector3 point, out Vector3 normal)
        {
            const float rayHeight = 500f;
            var origin = new Vector3(worldX, rayHeight, worldZ);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayHeight * 2f, groundMask))
            {
                point = hit.point;
                normal = hit.normal;
                return true;
            }
            point = Vector3.zero;
            normal = Vector3.up;
            return false;
        }

        private bool TooClose(Vector3 p, List<TreeData> trees)
        {
            if (minDist <= 0f) return false;
            float sq = minDist * minDist;
            for (int i = 0; i < trees.Count; i++)
                if ((trees[i].position - p).sqrMagnitude < sq) return true;
            return false;
        }

        private void GetSampleBounds(out float minX, out float maxX, out float minZ, out float maxZ)
        {
            if (terrain != null && matchTerrainSize)
            {
                Vector3 offsetGrid = terrain.centerMesh
                    ? new Vector3(terrain.width * 0.5f, 0f, terrain.depth * 0.5f)
                    : Vector3.zero;

                // Mesh-local footprint corners, transformed to world space.
                Vector3 c0 = terrain.transform.TransformPoint(
                    (new Vector3(0f, 0f, 0f) - offsetGrid) * terrain.scale);
                Vector3 c1 = terrain.transform.TransformPoint(
                    (new Vector3(terrain.width, 0f, terrain.depth) - offsetGrid) * terrain.scale);

                minX = Mathf.Min(c0.x, c1.x); maxX = Mathf.Max(c0.x, c1.x);
                minZ = Mathf.Min(c0.z, c1.z); maxZ = Mathf.Max(c0.z, c1.z);
            }
            else
            {
                Vector3 c = transform.position;
                minX = c.x - areaSize.x * 0.5f; maxX = c.x + areaSize.x * 0.5f;
                minZ = c.z - areaSize.y * 0.5f; maxZ = c.z + areaSize.y * 0.5f;
            }
        }

        // =====================================================================
        //  Debug
        // =====================================================================
        [ContextMenu("Populate (debug)")]
        private void PopulateDebug()
        {
            var result = Populate();
            Debug.Log($"[EcosystemManager] Populated {result.Count} trees from {attempts} attempts " +
                      $"(seed {seed}). Species pool size {_allSpecies.Count}.");
        }

        [ContextMenu("Populate with real instantiating")]
        private void PopulatewithTree()
        {
            var result = Populate();
            SpawnfromData(result);

            Debug.Log($"[EcosystemManager] Populated {result.Count} trees from {attempts} attempts " +
                      $"(seed {seed}). Species pool size {_allSpecies.Count}.");
        }
        [ContextMenu("Clear")]
        private void ClearTrees()
        {
            foreach(var t in _spawnedTrees)
            {
                if (t != null) DestroyImmediate(t);
            }
            _spawnedTrees.Clear();
        }
         

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || _lastResult == null) return;
            foreach (var t in _lastResult)
            {
                Gizmos.color = t.speciesIndex switch
                {
                    var i when i >= _waterStart && i < _waterStart + _waterCount => Color.cyan,
                    var i when i >= _cliffStart && i < _cliffStart + _cliffCount => Color.red,
                    _ => Color.green,
                };
                Gizmos.DrawSphere(t.position, 0.4f);
            }
        }
    }
}
