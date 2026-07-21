using System.Collections.Generic;
using UnityEngine;

namespace GlimmerDiary.Flora
{
    /// <summary>
    /// 中央英雄猴面包树 —— 手工骨架（非 L-System，2026-07-18 设计者决议）。
    /// 固定三级结构：瓶状巨干（5 段）→ 主枝（6 根，方位/仰角逐根指定）→
    /// 侧枝（每主枝 2 根）。所有形态参数是本文件里的手写数据 —— 这棵树是
    /// 一个角色，不是一类物种的随机样本。
    /// 输出 BranchSegment 列表喂给 TreeMeshBuilder（共享圆环管线，与 L-System
    /// 产物同一条网格路径），branchPath 稳定（"0/i/j"），供未来断枝/生长
    /// 事件按枝寻址（见 memory flora-per-branch-customization）。
    /// 抖动只用确定性 hash（同一结构每次重建逐顶点一致 —— 英雄树必须稳定）。
    /// </summary>
    public static class BaobabBuilder
    {
        // —— 主干轮廓：自地面到冠底 13m，半径 2.55→1.10（瓶状 taper，
        //    TreeMeshBuilder 的 baseBulge 再叠加根部膨胀）。
        //    猴面包的高径比约 4~6:1 —— 远看必须是"柱子"不是"杆子" ——
        static readonly Vector3[] TrunkPts =
        {
            new Vector3(0.00f,  0.0f, 0.00f),
            new Vector3(0.10f,  3.0f, 0.05f),
            new Vector3(0.22f,  6.0f, 0.12f),
            new Vector3(0.30f,  9.0f, 0.16f),
            new Vector3(0.34f, 11.0f, 0.18f),
            new Vector3(0.38f, 13.0f, 0.20f),
        };
        static readonly float[] TrunkR = { 2.55f, 2.26f, 1.95f, 1.64f, 1.36f, 1.10f };

        // —— 主枝：挂点=干轮廓节点索引，az=方位角(度)，el=离水平仰角(度)，
        //    len=总长(米)，r0=基径。猴面包的枝像倒栽的根：仰角普遍陡峭 ——
        struct Limb { public int at; public float az, el, len, r0;
                      public Limb(int a, float z, float e, float l, float r) { at=a; az=z; el=e; len=l; r0=r; } }
        static readonly Limb[] Limbs =
        {
            new Limb(5,  10f, 42f, 7.5f, 0.72f),   // 冠顶长臂（画面横展）
            new Limb(5, 130f, 48f, 7.0f, 0.68f),
            new Limb(5, 250f, 45f, 7.2f, 0.70f),
            new Limb(4,  65f, 52f, 6.5f, 0.62f),
            new Limb(4, 180f, 55f, 6.2f, 0.60f),
            new Limb(4, 300f, 50f, 6.4f, 0.62f),
            new Limb(3,  30f, 60f, 5.6f, 0.52f),
            new Limb(3, 150f, 62f, 5.2f, 0.50f),
            new Limb(3, 275f, 58f, 5.4f, 0.52f),
        };

        /// <summary>总高（根到最高枝端），用于 normalizedPosition 归一</summary>
        public const float TotalHeight = 22f;

        public static List<TurtleInterpreter3D.BranchSegment> BuildSkeleton()
        {
            var segs = new List<TurtleInterpreter3D.BranchSegment>(48);

            // —— 干：depth 0，链式父子 ——
            for (int i = 0; i < TrunkPts.Length - 1; i++)
            {
                segs.Add(MakeSeg(TrunkPts[i], TrunkPts[i + 1], TrunkR[i], TrunkR[i + 1],
                                 depth: 0, parent: i - 1, path: "0"));
            }

            // —— 主枝 + 侧枝 ——
            for (int i = 0; i < Limbs.Length; i++)
            {
                var L = Limbs[i];
                Vector3 root = TrunkPts[L.at];
                int parentIdx = L.at - 1;                    // 挂点为干段 L.at-1 的终点
                string limbPath = $"0/{i}";

                // 主枝两段：中段带确定性弯折（抖动只来自 hash，重建稳定）
                float j1 = Hash(i, 0) - 0.5f;                // 方位微摆 ±9°
                float j2 = Hash(i, 1) - 0.5f;                // 仰角微摆 ±7°
                Vector3 dir0 = AzEl(L.az + j1 * 18f, L.el + j2 * 14f);
                Vector3 dir1 = AzEl(L.az + j1 * 26f, L.el + 16f + j2 * 10f); // 越走越翘
                Vector3 mid = root + dir0 * (L.len * 0.55f);
                Vector3 tip = mid + dir1 * (L.len * 0.45f);

                int limbSeg0 = segs.Count;
                segs.Add(MakeSeg(root, mid, L.r0, L.r0 * 0.62f, 1, parentIdx, limbPath));
                int limbSeg1 = segs.Count;
                segs.Add(MakeSeg(mid, tip, L.r0 * 0.62f, L.r0 * 0.34f, 1, limbSeg0, limbPath));

                // 侧枝 3 根：从主枝末端分叉，更陡更细，像根须指尖
                for (int j = 0; j < 3; j++)
                {
                    float spread = (j - 1) * (28f + Hash(i, 10 + j) * 16f) + (j == 2 ? 14f : 0f);
                    Vector3 tdir = AzEl(L.az + spread, L.el + 20f + Hash(i, 20 + j) * 14f);
                    float tlen = L.len * (0.32f + Hash(i, 30 + j) * 0.14f);
                    Vector3 twigTip = tip + tdir * tlen;
                    segs.Add(MakeSeg(tip, twigTip, L.r0 * 0.30f, L.r0 * 0.13f, 2, limbSeg1, $"0/{i}/{j}"));
                }
            }
            return segs;
        }

        static TurtleInterpreter3D.BranchSegment MakeSeg(
            Vector3 a, Vector3 b, float ra, float rb, int depth, int parent, string path)
        {
            float distA = depth == 0 ? a.y : 0f;   // 干用高度近似根距；枝不精确但只影响 UV2 生长序
            return new TurtleInterpreter3D.BranchSegment
            {
                start = a,
                end = b,
                orientation = Quaternion.LookRotation((b - a).normalized),
                startRadius = ra,
                endRadius = rb,
                depth = depth,
                branchIndex = 0,
                branchPath = path,
                normalizedPosition = Mathf.Clamp01((distA + Vector3.Distance(a, b) * 0.5f) / TotalHeight),
                distanceFromRoot = distA + Vector3.Distance(a, b),
                parentSegmentIndex = parent,
            };
        }

        // 方位角 az(度,绕 Y,0=+Z)、仰角 el(度,离水平) → 单位方向
        static Vector3 AzEl(float azDeg, float elDeg)
        {
            float az = azDeg * Mathf.Deg2Rad;
            float el = elDeg * Mathf.Deg2Rad;
            float ce = Mathf.Cos(el);
            return new Vector3(Mathf.Sin(az) * ce, Mathf.Sin(el), Mathf.Cos(az) * ce).normalized;
        }

        // 确定性 hash（不碰 UnityEngine.Random 状态）
        static float Hash(int a, int b)
        {
            float s = Mathf.Sin(a * 127.1f + b * 311.7f) * 43758.5453f;
            return s - Mathf.Floor(s);
        }
    }
}
