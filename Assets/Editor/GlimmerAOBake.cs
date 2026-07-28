using System.IO;
using UnityEditor;
using UnityEngine;

// 批次4（2026-07-28）：地形烘焙 AO——把"地形的凹陷程度"预计算成一张俯视贴图。
// 低洼处环境光天生稀薄、脊线略亮；地形/草着色时按世界 XZ 采样，只乘 ToonCore 的
// ambient（直射光不动，正午对比依然干净）。
// 只读 TerrainGenerator 生成的 MeshCollider，不修改生成器本体（用户禁改）。
// 地形由 TerrainGenerator.Start() 运行时生成 → 本菜单需在 play 模式执行（play-safe：
// 只读场景 + 写贴图/材质资产，不动世界状态）。
public static class GlimmerAOBake
{
    const int RES = 512;
    const string TEX_DIR = "Assets/Textures";
    const string TEX_PATH = "Assets/Textures/TerrainAO.png";
    const string TERRAIN_MAT = "Assets/Materials/Glimmer/Terrain_Glimmer.mat";
    const string GRASS_MAT = "Assets/Materials/Glimmer/Grass_Glimmer.mat";

    // 三个八度的影响半径（米）与权重：河床/石脚级、沟谷级、台地级。
    static readonly float[] OCTAVE_METERS  = { 4f, 14f, 45f };
    static readonly float[] OCTAVE_WEIGHTS = { 0.50f, 0.35f, 0.15f };
    // 凹度（米）→ 贴图明暗的曝光系数：越大洼越暗。调参主旋钮。
    // 0.12 首版在主取景区（中央台地，天然平坦）几乎不可读 → 0.22 让缓起伏也参与雕塑。
    const float EXPOSURE = 0.22f;

    [MenuItem("Tools/Glimmer/Bake Terrain AO")]
    public static void Bake()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[GlimmerAOBake] 地形由 TerrainGenerator.Start() 运行时生成——请先进入 play 模式再烘焙。");
            return;
        }
        var tg = Object.FindFirstObjectByType<TerrainGenerator>();
        var col = tg != null ? tg.GetComponent<MeshCollider>() : null;
        if (col == null || col.sharedMesh == null)
        {
            Debug.LogWarning("[GlimmerAOBake] 没找到已生成地形的 MeshCollider。");
            return;
        }

        Bounds b = col.bounds;
        float size = Mathf.Max(b.size.x, b.size.z);
        float texel = size / RES;

        // ---- 1. 高度场：自上而下 raycast（只认地形 collider，树石挡住的点标记后填邻值） ----
        var h = new float[RES, RES];
        var valid = new bool[RES, RES];
        int missed = 0;
        float y0 = b.max.y + 100f;
        for (int iz = 0; iz < RES; iz++)
        for (int ix = 0; ix < RES; ix++)
        {
            float wx = b.min.x + (ix + 0.5f) * texel;
            float wz = b.min.z + (iz + 0.5f) * texel;
            if (Physics.Raycast(new Vector3(wx, y0, wz), Vector3.down, out var hit, 1000f)
                && hit.collider == col)
            {
                h[ix, iz] = hit.point.y;
                valid[ix, iz] = true;
            }
            else missed++;
        }
        // 被树/石挡住的 texel：用左邻（或右邻）有效高度填平，避免假凹陷
        for (int iz = 0; iz < RES; iz++)
        for (int ix = 0; ix < RES; ix++)
        {
            if (valid[ix, iz]) continue;
            float fill = b.min.y;
            for (int k = ix - 1; k >= 0; k--) if (valid[k, iz]) { fill = h[k, iz]; break; }
            if (fill == b.min.y)
                for (int k = ix + 1; k < RES; k++) if (valid[k, iz]) { fill = h[k, iz]; break; }
            h[ix, iz] = fill;
        }

        // ---- 2. 多八度凹度：c = Σ w·(blur_r(h) − h)。正值=洼，负值=脊 ----
        float[,] accum = new float[RES, RES];
        for (int o = 0; o < OCTAVE_METERS.Length; o++)
        {
            int r = Mathf.Max(1, Mathf.RoundToInt(OCTAVE_METERS[o] / texel));
            float[,] blur = BoxBlur(h, r);
            for (int iz = 0; iz < RES; iz++)
            for (int ix = 0; ix < RES; ix++)
                accum[ix, iz] += OCTAVE_WEIGHTS[o] * (blur[ix, iz] - h[ix, iz]);
        }

        // ---- 3. 编码贴图：0.5=平地基准；洼 → <0.5（shader ×2 解码后 <1 压暗），脊 → >0.5 ----
        var tex = new Texture2D(RES, RES, TextureFormat.RGB24, false, true);
        float minT = 1f, maxT = 0f, sumT = 0f;
        for (int iz = 0; iz < RES; iz++)
        for (int ix = 0; ix < RES; ix++)
        {
            float t = valid[ix, iz] ? Mathf.Clamp01(0.5f - accum[ix, iz] * EXPOSURE) : 0.5f;
            minT = Mathf.Min(minT, t); maxT = Mathf.Max(maxT, t); sumT += t;
            tex.SetPixel(ix, iz, new Color(t, t, t));
        }
        tex.Apply();

        if (!Directory.Exists(TEX_DIR)) Directory.CreateDirectory(TEX_DIR);
        File.WriteAllBytes(TEX_PATH, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(TEX_PATH);
        var imp = (TextureImporter)AssetImporter.GetAtPath(TEX_PATH);
        imp.sRGBTexture = false;                 // 数据贴图，0.5 必须仍是 0.5
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.filterMode = FilterMode.Bilinear;
        imp.mipmapEnabled = false;
        imp.SaveAndReimport();
        var aoTex = AssetDatabase.LoadAssetAtPath<Texture2D>(TEX_PATH);

        // ---- 4. 写材质（贴图/边界/强度；烘焙菜单是这组参数的唯一写入者） ----
        // 强度定版 1.0/0.8：0.7/0.55 在正午直射下几乎不可读（AO 只乘 ambient，正午 ambient 是
        // 少数派）；2.0 超档过戏剧化。注意同一强度在黄昏/暴雨（ambient 主导）会读得更强。
        WriteMat(TERRAIN_MAT, aoTex, b, size, 1.00f);
        WriteMat(GRASS_MAT, aoTex, b, size, 0.80f);
        AssetDatabase.SaveAssets();

        Debug.Log($"[GlimmerAOBake] 完成：{TEX_PATH}  bounds=({b.min.x:F1},{b.min.z:F1}) size={size:F1}  " +
                  $"ao t∈[{minT:F3},{maxT:F3}] mean={sumT / (RES * RES):F3}  遮挡texel={missed}");
    }

    static void WriteMat(string path, Texture2D tex, Bounds b, float size, float strength)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { Debug.LogWarning($"[GlimmerAOBake] 材质不存在：{path}"); return; }
        mat.SetTexture("_TerrainAO", tex);
        mat.SetVector("_TerrainAOBounds", new Vector4(b.min.x, b.min.z, size, strength));
        EditorUtility.SetDirty(mat);
    }

    // 可分离盒式模糊（两趟一维），半径 r texel
    static float[,] BoxBlur(float[,] src, int r)
    {
        var tmp = new float[RES, RES];
        var dst = new float[RES, RES];
        int w = 2 * r + 1;
        for (int iz = 0; iz < RES; iz++)
        {
            float acc = 0f;
            for (int ix = -r; ix <= r; ix++) acc += src[Mathf.Clamp(ix, 0, RES - 1), iz];
            for (int ix = 0; ix < RES; ix++)
            {
                tmp[ix, iz] = acc / w;
                int add = Mathf.Min(ix + r + 1, RES - 1), sub = Mathf.Max(ix - r, 0);
                acc += src[add, iz] - src[sub, iz];
            }
        }
        for (int ix = 0; ix < RES; ix++)
        {
            float acc = 0f;
            for (int iz = -r; iz <= r; iz++) acc += tmp[ix, Mathf.Clamp(iz, 0, RES - 1)];
            for (int iz = 0; iz < RES; iz++)
            {
                dst[ix, iz] = acc / w;
                int add = Mathf.Min(iz + r + 1, RES - 1), sub = Mathf.Max(iz - r, 0);
                acc += tmp[ix, add] - tmp[ix, sub];
            }
        }
        return dst;
    }
}
