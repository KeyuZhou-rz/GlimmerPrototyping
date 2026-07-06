using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GlimmerDiary.Flora
{
    /// <summary>
    /// GPU 实例化草簇系统（demo2 对齐轮重构）。
    /// · 实心低模草簇（3 交叉锥形面片），Glimmer/Grass 实例化着色器
    /// · [ExecuteAlways] + beginCameraRendering：SceneView / 截图临时相机 / Play 全路径可见
    /// · 地形感知散布：只对地形碰撞体投射，坡度/水线/河道/高度带过滤，
    ///   ValueNoise 簇团门控与地形着色器草甸色斑同频（0.045）——草长在色斑上
    /// · SetEmotionState 保留为 Layer-3 挂钩（将来由绑定器从 E_env 驱动，此处不接线）
    /// </summary>
    [ExecuteAlways]
    public class GrassSystem : MonoBehaviour
    {
        [Header("Terrain wiring (由 Tools/Glimmer/Setup Grass 接线)")]
        public TerrainGenerator terrain;
        public MeshCollider terrainCollider;
        public float waterY = 0f;

        [Header("Distribution (簇/m²)")]
        public int seed = 42;
        public int maxTufts = 32000;
        public float densityLowland = 0.8f;
        public float densityPlains = 1.2f;
        public float densityHighland = 0.3f;
        [Tooltip("坡度上限（度），超过视为崖壁不长草")]
        public float maxSlopeDeg = 25f;
        [Tooltip("水面以上安全边距")]
        public float waterMargin = 0.25f;
        [Tooltip("河水缘外的裸岸宽度（世界单位）")]
        public float riverBankMargin = 1.2f;
        [Tooltip("簇团噪声频率——与 GlimmerTerrain 草甸色斑同频")]
        public float clumpNoiseScale = 0.045f;

        [Header("Tuft shape")]
        public float minHeight = 0.35f;
        public float maxHeight = 0.75f;

        [Header("Rendering")]
        public Material grassMaterial;
        public Mesh grassBladeMesh;
        public bool castShadows = false;

        [Header("Wind")]
        public Vector2 windDirection = new Vector2(1, 0);
        public float windStrength = 0.8f;
        public float windFrequency = 1.2f;

        [Header("Emotion Response (挂钩，当前不接线)")]
        [Range(-1f, 1f)] public float currentValence = 0.5f;
        [Range(0f, 1f)] public float currentArousal = 0.3f;

        // --- v1 EcosystemManager (已禁用) 兼容字段：新散布不读它们 ---------
        // EcosystemManager.SetupGrass() 仍会赋值 (EcosystemManager.cs:408-409)，
        // 保留避免编译错误；实际采样范围 = terrainCollider.bounds，密度 = 每带簇/m²。
        [HideInInspector] public Vector2 areaSize = new Vector2(160f, 160f);
        [HideInInspector] public int grassDensity = 100;

        // Instancing data
        private readonly List<Matrix4x4[]> _instanceBatches = new();
        private MaterialPropertyBlock _propertyBlock;

        // Shader property IDs（属性名与 Glimmer/Grass 对齐）
        private static readonly int WindDirectionID = Shader.PropertyToID("_WindDirection");
        private static readonly int WindStrengthID = Shader.PropertyToID("_WindStrength");
        private static readonly int WindFrequencyID = Shader.PropertyToID("_WindFrequency");

        private void OnEnable()
        {
            _propertyBlock ??= new MaterialPropertyBlock();
            if (grassBladeMesh == null) grassBladeMesh = CreateTuftMesh();
            if (_instanceBatches.Count == 0 && terrainCollider != null) GenerateGrass();

            // Update() 里的 DrawMeshInstanced 对截图临时相机 (cam.Render) 不可靠；
            // URP 的相机回调覆盖 SceneView / GameView / 手动 Render 全部路径。
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        private void Start()
        {
            // Play 模式保险：OnEnable 先于 TerrainGenerator.Start()（其序为 -100 但
            // OnEnable 阶段整体在所有 Start 之前），域重载清空批次后若 OnEnable
            // 采样失败（collider 未就绪），这里在地形重建完成后重试一次。
            if (_instanceBatches.Count == 0) GenerateGrass();
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        }

        private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (grassMaterial == null || grassBladeMesh == null || _instanceBatches.Count == 0) return;
            // 材质缩略图/反射探针相机不画草
            if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;

            _propertyBlock.SetVector(WindDirectionID, new Vector4(windDirection.x, 0, windDirection.y, 0));
            _propertyBlock.SetFloat(WindStrengthID, windStrength * (0.5f + currentArousal));
            _propertyBlock.SetFloat(WindFrequencyID, windFrequency);

            foreach (var batch in _instanceBatches)
            {
                Graphics.DrawMeshInstanced(
                    grassBladeMesh, 0, grassMaterial,
                    batch, batch.Length, _propertyBlock,
                    castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    true, gameObject.layer, cam);
            }
        }

        // =====================================================================
        //  散布
        // =====================================================================
        [ContextMenu("Regenerate Grass")]
        public void GenerateGrass()
        {
            _instanceBatches.Clear();
            if (terrainCollider == null)
            {
                Debug.LogWarning("[GrassSystem] terrainCollider 未接线——先跑 Tools/Glimmer/Setup Grass");
                return;
            }

            var rng = new System.Random(seed);
            Bounds bb = terrainCollider.bounds;
            float area = bb.size.x * bb.size.z;
            float maxDensity = Mathf.Max(densityLowland, Mathf.Max(densityPlains, densityHighland));

            // 拒绝采样：飞镖数 = 面积×最大密度×超采样，接受率 = 局部密度/最大密度
            int darts = Mathf.Min(maxTufts * 6, Mathf.RoundToInt(area * maxDensity * 1.8f));
            float rayTop = bb.max.y + 10f;

            var currentBatch = new List<Matrix4x4>(1023);
            int placed = 0;

            for (int i = 0; i < darts && placed < maxTufts; i++)
            {
                float x = Mathf.Lerp(bb.min.x, bb.max.x, (float)rng.NextDouble());
                float z = Mathf.Lerp(bb.min.z, bb.max.z, (float)rng.NextDouble());

                var ray = new Ray(new Vector3(x, rayTop, z), Vector3.down);
                if (!terrainCollider.Raycast(ray, out RaycastHit hit, bb.size.y + 40f)) continue;

                // --- 过滤：崖壁 / 水下 / 河岸裸带 ---
                if (Vector3.Angle(hit.normal, Vector3.up) > maxSlopeDeg) continue;
                float y = hit.point.y;
                if (y < waterY + waterMargin) continue;
                if (DistanceToWater(hit.point) < riverBankMargin) continue;

                // --- 高度带密度 × 簇团噪声 ---
                float d = DensityAtHeight(y);
                if (d <= 0f) continue;
                d *= ClumpGate(hit.point.x, hit.point.z);
                if ((float)rng.NextDouble() * maxDensity > d) continue;

                // --- TRS：随机朝向 + 微倾 + 矮胖缩放 ---
                float h = Mathf.Lerp(minHeight, maxHeight, (float)rng.NextDouble());
                var rot = Quaternion.Euler(
                    Mathf.Lerp(-6f, 6f, (float)rng.NextDouble()),
                    (float)rng.NextDouble() * 360f,
                    Mathf.Lerp(-6f, 6f, (float)rng.NextDouble()));
                var scale = new Vector3(h * 0.9f, h, h * 0.9f);

                currentBatch.Add(Matrix4x4.TRS(hit.point, rot, scale));
                placed++;

                if (currentBatch.Count >= 1023)   // Graphics.DrawMeshInstanced 上限
                {
                    _instanceBatches.Add(currentBatch.ToArray());
                    currentBatch.Clear();
                }
            }
            if (currentBatch.Count > 0) _instanceBatches.Add(currentBatch.ToArray());

            Debug.Log($"[GrassSystem] {placed} tufts in {_instanceBatches.Count} batches (seed {seed})");
        }

        /// <summary>高度带密度：沙滩/山地 0，低地/平原/高地取各自密度（带边取自地形区域高度）。</summary>
        private float DensityAtHeight(float y)
        {
            if (terrain == null) return densityPlains;

            float s = terrain.scale;
            float mid1 = (terrain.lowlandHeight + terrain.plainsHeight) * 0.5f * s;   // 低地|平原
            float mid2 = (terrain.plainsHeight + terrain.highlandHeight) * 0.5f * s;  // 平原|高地
            float top = terrain.highlandHeight * s + 0.6f;                            // 高地上限→山

            if (y >= top) return 0f;
            if (y >= mid2) return densityHighland;
            if (y >= mid1) return densityPlains;
            return densityLowland;
        }

        /// <summary>
        /// 簇团门控：ValueNoise 公式镜像 GlimmerTerrain.shader（Hash2/ValueNoise, 频率 0.045）,
        /// 让草簇聚落与地面草甸色斑大致对齐。返回 0.15..1 密度乘子。
        /// </summary>
        private float ClumpGate(float x, float z)
        {
            float n = ValueNoise(new Vector2(x, z) * clumpNoiseScale);
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 0.72f, n));
            return 0.15f + 0.85f * t;
        }

        private static float Hash2(Vector2 p)
        {
            float d = p.x * 127.1f + p.y * 311.7f;
            float s = Mathf.Sin(d) * 43758.5453f;
            return s - Mathf.Floor(s);
        }

        private static float ValueNoise(Vector2 p)
        {
            var i = new Vector2(Mathf.Floor(p.x), Mathf.Floor(p.y));
            var f = new Vector2(p.x - i.x, p.y - i.y);
            var u = new Vector2(f.x * f.x * (3f - 2f * f.x), f.y * f.y * (3f - 2f * f.y));
            float a = Hash2(i);
            float b = Hash2(i + Vector2.right);
            float c = Hash2(i + Vector2.up);
            float d = Hash2(i + Vector2.one);
            return Mathf.Lerp(Mathf.Lerp(a, b, u.x), Mathf.Lerp(c, d, u.x), u.y);
        }

        /// <summary>到河水缘的水平距离——镜像自 TreePlacement.DistanceToWater（勿动原文件）。</summary>
        private float DistanceToWater(Vector3 p)
        {
            if (terrain == null || !terrain.enableRiver) return float.PositiveInfinity;

            Vector3 offsetGrid = terrain.centerMesh
                ? new Vector3(terrain.width * 0.5f, 0f, terrain.depth * 0.5f)
                : Vector3.zero;
            Vector3 local = terrain.transform.InverseTransformPoint(p);
            float gx = local.x / terrain.scale + offsetGrid.x;
            float gz = local.z / terrain.scale + offsetGrid.z;

            float nz = gz / terrain.depth;
            float centerGridX = terrain.RiverCurve(nz) * terrain.width;
            float halfWidthGrid = terrain.riverWidth * 0.5f * terrain.width;

            float distGrid = Mathf.Max(0f, Mathf.Abs(gx - centerGridX) - halfWidthGrid);
            return distGrid * terrain.scale;
        }

        // =====================================================================
        //  情绪挂钩（Layer-3：将来由绑定器从 E_env 驱动；本轮不接线）
        // =====================================================================
        public void SetEmotionState(float valence, float arousal)
        {
            currentValence = valence;
            currentArousal = arousal;
            windStrength = Mathf.Lerp(0.3f, 2f, arousal);   // 高唤醒 = 更大的风
        }

        // =====================================================================
        //  草簇网格：3 片交叉锥形面片（demo2 式实心低模簇，无 alpha）
        // =====================================================================
        public static Mesh CreateTuftMesh()
        {
            var mesh = new Mesh { name = "GrassTuft" };

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            const int planes = 3;            // 0° / 60° / 120°
            const float baseHalf = 0.30f;    // 根部半宽
            const float tipHalf = 0.07f;     // 顶部半宽（锥形）

            for (int p = 0; p < planes; p++)
            {
                float ang = p * Mathf.PI / planes;
                var right = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));

                int i0 = vertices.Count;
                vertices.Add(-right * baseHalf);                       // 根左
                vertices.Add(right * baseHalf);                        // 根右
                vertices.Add(-right * tipHalf + Vector3.up);           // 顶左
                vertices.Add(right * tipHalf + Vector3.up);            // 顶右

                // 法线全部朝上：草簇按地面受光，与地形色带融为一体（风格化常用手法）
                for (int k = 0; k < 4; k++) normals.Add(Vector3.up);

                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f));

                triangles.Add(i0); triangles.Add(i0 + 2); triangles.Add(i0 + 1);
                triangles.Add(i0 + 1); triangles.Add(i0 + 2); triangles.Add(i0 + 3);
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
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
