using UnityEngine;

/// <summary>
/// 痕迹 prop 网格工厂（Layer 3）：土堆 / 脚印 / 羽毛 / 记号 / 压草椭圆。
/// 全部程序化平面着色小网格（非共享顶点 + RecalculateNormals，同 RockMeshBuilder 风格），
/// 静态缓存——痕迹总量是几十个，池化 GameObject 足够，不需要 instancing。
/// 材质由 GlimmerTraceSetup 烘焙（Glimmer/Toon，无贴图无 alpha），WorldTraceBinder 组装。
/// </summary>
public static class TraceKit
{
    private static Mesh _mound, _moundCollapsed, _footprint, _feather, _mark, _pressedOval;

    /// <summary>新翻土堆：低矮八面锥丘，顶部略偏斜。</summary>
    public static Mesh Mound => _mound != null ? _mound
        : _mound = BuildMound("Trace_Mound", height: 0.22f, radius: 0.38f, dimple: 0f);

    /// <summary>塌陷旧洞：同土堆轮廓但中心下凹（永久痕迹用）。</summary>
    public static Mesh MoundCollapsed => _moundCollapsed != null ? _moundCollapsed
        : _moundCollapsed = BuildMound("Trace_MoundCollapsed", height: 0.10f, radius: 0.42f, dimple: -0.12f);

    /// <summary>脚印：小椭圆贴地片（一串摆出足迹）。</summary>
    public static Mesh Footprint => _footprint != null ? _footprint
        : _footprint = BuildDisc("Trace_Footprint", segments: 6, rx: 0.09f, rz: 0.14f);

    /// <summary>羽毛：窄长卡片，带一点中脊折角（不透明，硬边符合平面着色语言）。</summary>
    public static Mesh Feather => _feather != null ? _feather : _feather = BuildFeather();

    /// <summary>记号：不规则五边形小暗斑。</summary>
    public static Mesh Mark => _mark != null ? _mark
        : _mark = BuildDisc("Trace_Mark", segments: 5, rx: 0.16f, rz: 0.13f);

    /// <summary>压草椭圆：大而扁的淡色贴地片（QuietConvergence 歇息处，主表达靠 trample）。</summary>
    public static Mesh PressedOval => _pressedOval != null ? _pressedOval
        : _pressedOval = BuildDisc("Trace_PressedOval", segments: 10, rx: 0.85f, rz: 0.60f);

    // ── 网格构造 ─────────────────────────────────────────────────

    /// <summary>低矮锥丘：环形裙边 + 中心顶点（dimple&lt;0 时中心下凹成塌陷洞）。</summary>
    private static Mesh BuildMound(string name, float height, float radius, float dimple)
    {
        const int SEG = 8;
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris  = new System.Collections.Generic.List<int>();

        float apexY = dimple < 0f ? dimple : height;
        var apex = new Vector3(0.06f, apexY, -0.03f);   // 顶点偏心，避免完美对称

        for (int i = 0; i < SEG; i++)
        {
            float a0 = i / (float)SEG * Mathf.PI * 2f;
            float a1 = (i + 1) / (float)SEG * Mathf.PI * 2f;
            // 半径微扰打破正多边形感（确定性：只依赖 i）
            float r0 = radius * (1f + 0.12f * Mathf.Sin(i * 2.7f));
            float r1 = radius * (1f + 0.12f * Mathf.Sin((i + 1) * 2.7f));
            var v0 = new Vector3(Mathf.Cos(a0) * r0, 0f, Mathf.Sin(a0) * r0);
            var v1 = new Vector3(Mathf.Cos(a1) * r1, 0f, Mathf.Sin(a1) * r1);

            if (dimple < 0f)
            {
                // 塌陷：外缘先抬到小唇高再落向中心凹点
                float lipY = height * 0.6f;
                var l0 = Vector3.Lerp(v0, apex, 0.35f); l0.y = lipY;
                var l1 = Vector3.Lerp(v1, apex, 0.35f); l1.y = lipY;
                AddTri(verts, tris, v0, l1, l0);
                AddTri(verts, tris, v0, v1, l1);
                AddTri(verts, tris, l0, l1, apex);
            }
            else
            {
                AddTri(verts, tris, v0, v1, apex);
            }
        }
        return Bake(name, verts, tris);
    }

    /// <summary>贴地多边形圆盘（顶面朝上单面即可，微高避免 z-fight 由摆放层负责）。</summary>
    private static Mesh BuildDisc(string name, int segments, float rx, float rz)
    {
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris  = new System.Collections.Generic.List<int>();
        var center = Vector3.zero;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i / (float)segments * Mathf.PI * 2f;
            float a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
            float w0 = 1f + 0.15f * Mathf.Sin(i * 3.1f);
            float w1 = 1f + 0.15f * Mathf.Sin((i + 1) * 3.1f);
            var v0 = new Vector3(Mathf.Cos(a0) * rx * w0, 0f, Mathf.Sin(a0) * rz * w0);
            var v1 = new Vector3(Mathf.Cos(a1) * rx * w1, 0f, Mathf.Sin(a1) * rz * w1);
            AddTri(verts, tris, center, v1, v0);
        }
        return Bake(name, verts, tris);
    }

    /// <summary>羽毛卡片：两片沿中脊折起的细长四边形 + 尖端。</summary>
    private static Mesh BuildFeather()
    {
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris  = new System.Collections.Generic.List<int>();

        const float LEN = 0.30f, HALF = 0.035f, RIDGE = 0.02f;
        var root = new Vector3(0f, 0.005f, 0f);
        var tip  = new Vector3(0f, 0.01f, LEN);
        var midL = new Vector3(-HALF, 0f, LEN * 0.45f);
        var midR = new Vector3( HALF, 0f, LEN * 0.45f);
        var spine= new Vector3(0f, RIDGE, LEN * 0.45f);   // 中脊拱起 → 折角高光

        // 左右两瓣（上面）
        AddTri(verts, tris, root, spine, midL);
        AddTri(verts, tris, midL, spine, tip);
        AddTri(verts, tris, root, midR, spine);
        AddTri(verts, tris, midR, tip, spine);
        // 背面（翻绕序），落在草地上双面可见
        AddTri(verts, tris, root, midL, spine);
        AddTri(verts, tris, midL, tip, spine);
        AddTri(verts, tris, root, spine, midR);
        AddTri(verts, tris, midR, spine, tip);

        return Bake("Trace_Feather", verts, tris);
    }

    // 非共享顶点逐三角形追加 → RecalculateNormals 得到平面着色
    private static void AddTri(System.Collections.Generic.List<Vector3> verts,
                               System.Collections.Generic.List<int> tris,
                               Vector3 a, Vector3 b, Vector3 c)
    {
        int i = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c);
        tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
    }

    private static Mesh Bake(string name, System.Collections.Generic.List<Vector3> verts,
                             System.Collections.Generic.List<int> tris)
    {
        var mesh = new Mesh { name = name };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
