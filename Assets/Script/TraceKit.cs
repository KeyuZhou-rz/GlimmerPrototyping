using UnityEngine;

/// <summary>
/// 痕迹 prop 网格工厂（Layer 3）：土堆 / 脚印 / 羽毛 / 记号 / 压草椭圆。
/// 全部程序化平面着色小网格（非共享顶点 + RecalculateNormals，同 RockMeshBuilder 风格），
/// 静态缓存——痕迹总量是几十个，池化 GameObject 足够，不需要 instancing。
/// 材质由 GlimmerTraceSetup 烘焙（Glimmer/Toon，无贴图无 alpha），WorldTraceBinder 组装。
/// </summary>
public static class TraceKit
{
    private static Mesh _mound, _moundCollapsed, _footprint, _feather, _mark, _pressedOval, _earthCrack, _sprout;

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

    /// <summary>地裂：一条主缝 + 两条支缝的锯齿条带（旱痕 §5.5，贴地暗色，摆放层 yaw/缩放打散）。</summary>
    public static Mesh EarthCrack => _earthCrack != null ? _earthCrack : _earthCrack = BuildEarthCrack();

    /// <summary>新绒苗：细茎（十字双卡片，双面）+ 顶点绒球（八面体）。蒲公英落种 N 日后冒出（§5.3）。</summary>
    public static Mesh Sprout => _sprout != null ? _sprout : _sprout = BuildSprout();

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

    /// <summary>地裂网格：主缝 6 段锯齿 + 两条支缝，端头收窄。</summary>
    private static Mesh BuildEarthCrack()
    {
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris  = new System.Collections.Generic.List<int>();

        AddCrackLine(verts, tris, new Vector2(-0.62f, 0.02f), new Vector2(0.66f, 0.10f), 6, 0.055f, 0.5f);  // 主缝
        AddCrackLine(verts, tris, new Vector2(-0.08f, 0.03f), new Vector2(0.16f, 0.46f), 3, 0.038f, 0.4f);   // 支缝一
        AddCrackLine(verts, tris, new Vector2(0.10f, 0.01f), new Vector2(0.34f, -0.40f), 3, 0.032f, 0.4f);   // 支缝二
        return Bake("Trace_EarthCrack", verts, tris);
    }

    /// <summary>锯齿条带：from→to 分 segs 段逐段交替侧移，宽度随 t 向末端收窄（taper）。</summary>
    private static void AddCrackLine(System.Collections.Generic.List<Vector3> verts,
                                     System.Collections.Generic.List<int> tris,
                                     Vector2 from, Vector2 to, int segs, float width, float taper)
    {
        Vector2 dir  = (to - from).normalized;
        Vector2 perp = new(-dir.y, dir.x);
        Vector2 prevL = default, prevR = default;

        for (int i = 0; i <= segs; i++)
        {
            float t = i / (float)segs;
            Vector2 c = Vector2.Lerp(from, to, t);
            // 锯齿：交替侧移 + 确定性微扰（只依赖 i，重启复现）
            float jag = (i % 2 == 0 ? 1f : -1f) * 0.055f * Mathf.Abs(Mathf.Sin(i * 1.7f + 0.9f));
            c += perp * jag;
            float hw = width * Mathf.Lerp(1f, taper, t) * 0.5f;
            if (i == 0 || i == segs) hw *= 0.35f;   // 端头收尖
            Vector2 l = c + perp * hw;
            Vector2 r = c - perp * hw;
            if (i > 0)
            {
                AddTri(verts, tris, Flat(prevL), Flat(l), Flat(prevR));
                AddTri(verts, tris, Flat(prevR), Flat(l), Flat(r));
            }
            prevL = l; prevR = r;
        }
    }

    /// <summary>绒苗网格：十字细茎 + 顶点小八面体绒球（绕序同 Mound 环例，法线朝上/外）。</summary>
    private static Mesh BuildSprout()
    {
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris  = new System.Collections.Generic.List<int>();

        const float STEM_H = 0.15f, STEM_W = 0.010f, PUFF_R = 0.042f;
        var tip = new Vector3(0.008f, STEM_H, 0.005f);   // 顶端微偏（同 Mound 偏心手法）

        // 茎：十字交叉两张窄三角卡片（双面绕序，任何角度可见）
        var l0 = new Vector3(-STEM_W, 0f, 0f); var r0 = new Vector3(STEM_W, 0f, 0f);
        AddTri(verts, tris, l0, r0, tip); AddTri(verts, tris, r0, l0, tip);
        var f0 = new Vector3(0f, 0f, -STEM_W); var b0 = new Vector3(0f, 0f, STEM_W);
        AddTri(verts, tris, f0, b0, tip); AddTri(verts, tris, b0, f0, tip);

        // 绒球：八面体（顶/底 + 赤道上 4 点，半径微扰破对称）
        var top = tip + new Vector3(0f, PUFF_R, 0f);
        var bot = tip - new Vector3(0f, PUFF_R * 0.6f, 0f);
        var eq = new Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI * 0.5f + 0.35f;   // 旋一点，不和茎卡片对齐
            float r = PUFF_R * (0.85f + 0.15f * Mathf.Sin(i * 2.3f));
            eq[i] = tip + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
        }
        for (int i = 0; i < 4; i++)
        {
            int n = (i + 1) % 4;
            AddTri(verts, tris, eq[i], eq[n], top);   // 上半（绕序同 Mound：环点→环邻→顶）
            AddTri(verts, tris, eq[n], eq[i], bot);   // 下半（反绕）
        }
        return Bake("Trace_Sprout", verts, tris);
    }

    private static Vector3 Flat(Vector2 v) => new(v.x, 0f, v.y);

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
