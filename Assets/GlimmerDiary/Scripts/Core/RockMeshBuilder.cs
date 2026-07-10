using System;
using System.Collections.Generic;
using UnityEngine;

namespace GlimmerDiary.Environment
{
    /// <summary>
    /// 程序化低模巨石网格生成器（demo2 对齐轮）。
    /// 形体语言与 TerrainGenerator 一致：非共享顶点 + RecalculateNormals = 硬刻面。
    /// 流程：cube-sphere（细分立方体归一化）→ 逐顶点径向噪声位移
    ///       → 非均匀轴缩放（偏扁）→ 底部压平 → 炸开三角形。
    /// 纯网格计算，无场景依赖；编辑器烘焙与运行时均可调用。
    /// </summary>
    public static class RockMeshBuilder
    {
        /// <summary>三档体量：影响细分数与三角形预算。</summary>
        public enum RockSize
        {
            Large,   // ~300-600 tris，崖缝/山环主体量
            Medium,  // ~150-300 tris
            Small    // ~40-120 tris，河岸碎石
        }

        /// <summary>
        /// 生成一块刻面岩石。seed 决定全部随机形状，同种子同网格。
        /// 返回网格以原点为几何中心、底面在 y≈-extent 处，单位尺度（散布时再整体缩放）。
        /// </summary>
        public static Mesh Build(RockSize size, int seed)
        {
            var rng = new System.Random(seed);

            int subdiv = size switch
            {
                RockSize.Large => 3,   // 6*3*3*2 = 108 面 → 炸开后 ~324-648 tris 视压平剔除
                RockSize.Medium => 2,  // 6*2*2*2 = 48 面
                _ => 1                 // 6*1*1*2 = 12 面（近似碎石晶体）
            };

            // --- 1. cube-sphere 基础形 ---------------------------------------
            List<Vector3> verts;
            List<int> tris;
            BuildCubeSphere(subdiv, out verts, out tris);

            // --- 2. 径向噪声位移（两层：大起伏 + 小凿痕） ---------------------
            // Perlin 采样偏移由种子决定，保证不同 seed 形状不同
            float noiseOffX = (float)rng.NextDouble() * 100f;
            float noiseOffY = (float)rng.NextDouble() * 100f;
            float bigAmp = Mathf.Lerp(0.25f, 0.40f, (float)rng.NextDouble());
            float smallAmp = size == RockSize.Small ? 0.10f : 0.16f;

            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 dir = verts[i].normalized;
                // 用方向向量做 2D 采样域（球面连续，避免接缝跳变）
                float u = noiseOffX + dir.x * 1.7f + dir.z * 1.3f;
                float v = noiseOffY + dir.y * 1.9f + dir.z * 0.8f;
                float big = (Mathf.PerlinNoise(u, v) - 0.5f) * 2f * bigAmp;
                float small = (Mathf.PerlinNoise(u * 3.1f, v * 3.3f) - 0.5f) * 2f * smallAmp;
                verts[i] = dir * (1f + big + small);
            }

            // --- 3. 非均匀轴缩放：偏扁、偏长，破除球感 ------------------------
            float sx = Mathf.Lerp(0.8f, 1.3f, (float)rng.NextDouble());
            float sz = Mathf.Lerp(0.8f, 1.3f, (float)rng.NextDouble());
            float sy = Mathf.Lerp(0.6f, 1.0f, (float)rng.NextDouble());
            for (int i = 0; i < verts.Count; i++)
                verts[i] = Vector3.Scale(verts[i], new Vector3(sx, sy, sz));

            // --- 4. 底部压平：让岩石能"坐"在地上（散布时还会再下沉） ----------
            float minY = float.MaxValue;
            for (int i = 0; i < verts.Count; i++) minY = Mathf.Min(minY, verts[i].y);
            float flattenPlane = minY + (Mathf.Abs(minY)) * 0.22f;   // 压掉底部 ~22%
            for (int i = 0; i < verts.Count; i++)
                if (verts[i].y < flattenPlane)
                    verts[i] = new Vector3(verts[i].x, flattenPlane, verts[i].z);

            // --- 5. 炸开为非共享顶点 → 硬刻面（同 TerrainGenerator 模式） ----
            var mesh = ExplodeToFlatShaded(verts, tris);
            mesh.name = $"Rock_{size}_{seed}";
            return mesh;
        }

        // ---------------------------------------------------------------------
        // cube-sphere：六面各 subdiv×subdiv 网格，顶点归一化到单位球。
        // 顶点在面内共享，面间不共享——反正最后会炸开，无需焊接。
        static void BuildCubeSphere(int subdiv, out List<Vector3> verts, out List<int> tris)
        {
            verts = new List<Vector3>();
            tris = new List<int>();

            // 六个面：法线方向 + 面内两个切向
            Vector3[] normals = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            foreach (var n in normals)
            {
                Vector3 tangentA = new Vector3(n.y, n.z, n.x);   // 与 n 正交的轮换轴
                Vector3 tangentB = Vector3.Cross(n, tangentA);

                int baseIndex = verts.Count;
                for (int y = 0; y <= subdiv; y++)
                {
                    for (int x = 0; x <= subdiv; x++)
                    {
                        Vector2 p = new Vector2(x, y) / subdiv * 2f - Vector2.one;  // -1..1
                        Vector3 point = n + tangentA * p.x + tangentB * p.y;
                        verts.Add(point.normalized);   // 立方体面 → 单位球面
                    }
                }
                for (int y = 0; y < subdiv; y++)
                {
                    for (int x = 0; x < subdiv; x++)
                    {
                        int i = baseIndex + y * (subdiv + 1) + x;
                        tris.Add(i); tris.Add(i + subdiv + 1); tris.Add(i + 1);
                        tris.Add(i + 1); tris.Add(i + subdiv + 1); tris.Add(i + subdiv + 2);
                    }
                }
            }
        }

        // 每三角形独立三顶点 + RecalculateNormals → 每面一个硬法线。
        // 退化三角形（底部压平产生的零面积面）在此剔除。
        // 绕序修正：cube-sphere 六面的切向手性不一致，一半面片天生朝内——
        // 形状围绕原点凸出，以"质心方向=外"为参考统一翻正，否则受光全错（岩石读作黑块）。
        static Mesh ExplodeToFlatShaded(List<Vector3> verts, List<int> tris)
        {
            var outVerts = new List<Vector3>(tris.Count);
            var outUVs = new List<Vector2>(tris.Count);
            var outTris = new List<int>(tris.Count);

            for (int t = 0; t < tris.Count; t += 3)
            {
                Vector3 a = verts[tris[t]];
                Vector3 b = verts[tris[t + 1]];
                Vector3 c = verts[tris[t + 2]];

                Vector3 faceN = Vector3.Cross(b - a, c - a);

                // 压平产生的共线/零面积三角形直接丢弃
                if (faceN.sqrMagnitude < 1e-8f) continue;

                // 法线朝内 → 交换 b/c 翻转绕序
                Vector3 centroid = (a + b + c) / 3f;
                if (Vector3.Dot(faceN, centroid) < 0f)
                {
                    (b, c) = (c, b);
                }

                int i0 = outVerts.Count;
                outVerts.Add(a); outVerts.Add(b); outVerts.Add(c);
                // 简易平面 UV（Glimmer/Toon 无贴图时用不到，留作 future-proof）
                outUVs.Add(new Vector2(a.x, a.z));
                outUVs.Add(new Vector2(b.x, b.z));
                outUVs.Add(new Vector2(c.x, c.z));
                outTris.Add(i0); outTris.Add(i0 + 1); outTris.Add(i0 + 2);
            }

            var mesh = new Mesh();
            mesh.SetVertices(outVerts);
            mesh.SetUVs(0, outUVs);
            mesh.SetTriangles(outTris, 0);
            mesh.RecalculateNormals();   // 非共享顶点 → 逐面硬法线
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
