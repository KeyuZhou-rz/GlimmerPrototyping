using System.Collections.Generic;
using UnityEngine;

namespace GlimmerDiary.Flora
{
    /// <summary>
    /// Generates foliage (leaves, needles, fronds) and attaches them to trees.
    /// Supports multiple leaf shapes and clustering patterns.
    ///
    /// Each leaf shape generates explicit double-sided geometry (front + back faces
    /// with correct normals) so the Vegetation shader can use Cull Back and still
    /// light both sides correctly. Tip vertices include a slight Z curvature for
    /// a natural 3D curl effect.
    /// </summary>
    public class FoliageGenerator
    {
        public struct FoliageResult
        {
            public Mesh combinedMesh;
            public List<Matrix4x4> instanceMatrices;  // For GPU instancing
            public Bounds bounds;
        }

        private readonly List<Vector3> _vertices = new();
        private readonly List<Vector3> _normals = new();
        private readonly List<Vector2> _uvs = new();
        private readonly List<Vector2> _uv2s = new();
        private readonly List<Color> _colors = new();
        private readonly List<int> _triangles = new();

        /// <summary>
        /// Generate foliage mesh from attachment points
        /// </summary>
        public FoliageResult Generate(
            List<TreeMeshBuilder.FoliageAttachmentPoint> attachmentPoints,
            PlantDefinition plant,
            int seed = 0)
        {
            var foliage = plant.foliage;

            return Generate(
                attachmentPoints,
                foliage.leafShape,
                foliage.leafSizeRange,
                foliage.leavesPerCluster,
                foliage.clusterSpread,
                foliage.leafColorBase,
                foliage.leafColorTip,
                seed
            );
        }

        public FoliageResult Generate(
            List<TreeMeshBuilder.FoliageAttachmentPoint> attachmentPoints,
            LeafShape shape,
            Vector2 sizeRange,
            int leavesPerCluster,
            float clusterSpread,
            Color colorBase,
            Color colorTip,
            int seed = 0)
        {
            Clear();

            var random = new System.Random(seed);
            var instanceMatrices = new List<Matrix4x4>();

            foreach (var point in attachmentPoints)
            {
                for (int i = 0; i < leavesPerCluster; i++)
                {
                    // Random position within cluster
                    Vector3 offset = new Vector3(
                        (float)(random.NextDouble() * 2 - 1),
                        (float)(random.NextDouble() * 2 - 1),
                        (float)(random.NextDouble() * 2 - 1)
                    ) * clusterSpread * point.scale;

                    Vector3 position = point.position + offset;

                    // Random rotation
                    Quaternion rotation = point.rotation * Quaternion.Euler(
                        (float)(random.NextDouble() * 60 - 30),
                        (float)(random.NextDouble() * 360),
                        (float)(random.NextDouble() * 30 - 15)
                    );

                    // Random size within range
                    float size = Mathf.Lerp(sizeRange.x, sizeRange.y, (float)random.NextDouble()) * point.scale;

                    // Color variation
                    float colorLerp = (float)random.NextDouble();
                    Color leafColor = Color.Lerp(colorBase, colorTip, colorLerp);

                    // Growth value based on attachment point
                    float growthValue = 0.8f + point.branchDepth * 0.05f;

                    // Add leaf geometry
                    AddLeaf(shape, position, rotation, size, leafColor, growthValue);

                    // Store instance matrix for GPU instancing option
                    instanceMatrices.Add(Matrix4x4.TRS(position, rotation, Vector3.one * size));
                }
            }

            var mesh = new Mesh { name = "Foliage" };

            if (_vertices.Count > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            if (_vertices.Count > 0)
            {
                mesh.SetVertices(_vertices);
                mesh.SetNormals(_normals);
                mesh.SetUVs(0, _uvs);
                mesh.SetUVs(1, _uv2s);
                mesh.SetColors(_colors);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
            }

            return new FoliageResult
            {
                combinedMesh = mesh,
                instanceMatrices = instanceMatrices,
                bounds = mesh.bounds
            };
        }

        private void AddLeaf(LeafShape shape, Vector3 position, Quaternion rotation, float size, Color color, float growthValue)
        {
            switch (shape)
            {
                case LeafShape.Oval:
                    AddOvalLeaf(position, rotation, size, color, growthValue);
                    break;
                case LeafShape.Pointed:
                    AddPointedLeaf(position, rotation, size, color, growthValue);
                    break;
                case LeafShape.Round:
                    AddRoundLeaf(position, rotation, size, color, growthValue);
                    break;
                case LeafShape.Needle:
                    AddNeedleLeaf(position, rotation, size, color, growthValue);
                    break;
                case LeafShape.Fan:
                    AddFanLeaf(position, rotation, size, color, growthValue);
                    break;
                case LeafShape.Heart:
                    AddHeartLeaf(position, rotation, size, color, growthValue);
                    break;
                case LeafShape.Compound:
                    AddCompoundLeaf(position, rotation, size, color, growthValue);
                    break;
            }
        }

        // ─── Helper: duplicate front-face geometry as a back face with flipped normals ───

        /// <summary>
        /// Copies vertices [frontBaseIndex .. frontBaseIndex+count) with negated normals,
        /// then appends the same triangles in reversed winding for a proper back face.
        /// </summary>
        private void AddBackFaceFromFront(int frontBaseIndex, int frontVertexCount,
                                          int frontTriangleStart, int frontTriangleCount)
        {
            int backBaseIndex  = _vertices.Count;
            int indexOffset    = backBaseIndex - frontBaseIndex;

            // Duplicate vertices with negated normals
            for (int i = 0; i < frontVertexCount; i++)
            {
                int src = frontBaseIndex + i;
                _vertices.Add(_vertices[src]);
                _normals.Add(-_normals[src]);
                _uvs.Add(_uvs[src]);
                _uv2s.Add(_uv2s[src]);
                _colors.Add(_colors[src]);
            }

            // Reverse each triangle's winding
            for (int i = 0; i < frontTriangleCount; i += 3)
            {
                int i0 = _triangles[frontTriangleStart + i]     + indexOffset;
                int i1 = _triangles[frontTriangleStart + i + 1] + indexOffset;
                int i2 = _triangles[frontTriangleStart + i + 2] + indexOffset;
                _triangles.Add(i0);
                _triangles.Add(i2);   // swap i1 / i2 to reverse winding
                _triangles.Add(i1);
            }
        }

        // ─── Leaf shapes ───────────────────────────────────────────────────────────────

        private void AddOvalLeaf(Vector3 pos, Quaternion rot, float size, Color color, float growth)
        {
            int frontBase    = _vertices.Count;
            int frontTriBase = _triangles.Count;

            Vector3 normal = rot * Vector3.back;

            // Slight Z curvature at tip for a natural 3D curl
            Vector3[] localVerts = {
                new Vector3(-0.5f, 0f,  0f   ),
                new Vector3( 0.5f, 0f,  0f   ),
                new Vector3( 0.5f, 1f,  0.02f),
                new Vector3(-0.5f, 1f,  0.02f)
            };

            foreach (var v in localVerts)
            {
                _vertices.Add(pos + rot * (v * size));
                _normals.Add(normal);
                _colors.Add(color);
                _uv2s.Add(new Vector2(0, growth));
            }

            _uvs.Add(new Vector2(0, 0));
            _uvs.Add(new Vector2(1, 0));
            _uvs.Add(new Vector2(1, 1));
            _uvs.Add(new Vector2(0, 1));

            _triangles.Add(frontBase);
            _triangles.Add(frontBase + 2);
            _triangles.Add(frontBase + 1);
            _triangles.Add(frontBase);
            _triangles.Add(frontBase + 3);
            _triangles.Add(frontBase + 2);

            AddBackFaceFromFront(frontBase, _vertices.Count - frontBase,
                                 frontTriBase, _triangles.Count - frontTriBase);
        }

        private void AddPointedLeaf(Vector3 pos, Quaternion rot, float size, Color color, float growth)
        {
            int frontBase    = _vertices.Count;
            int frontTriBase = _triangles.Count;

            Vector3 normal = rot * Vector3.back;

            // Slight Z curvature at tip
            Vector3[] localVerts = {
                new Vector3( 0f,    0f,   0f   ),   // Base
                new Vector3(-0.3f,  0.5f, 0f   ),   // Left
                new Vector3( 0f,    1.2f, 0.03f),   // Tip (curled)
                new Vector3( 0.3f,  0.5f, 0f   )    // Right
            };

            foreach (var v in localVerts)
            {
                _vertices.Add(pos + rot * (v * size));
                _normals.Add(normal);
                _colors.Add(color);
                _uv2s.Add(new Vector2(0, growth));
            }

            _uvs.Add(new Vector2(0.5f, 0));
            _uvs.Add(new Vector2(0,    0.5f));
            _uvs.Add(new Vector2(0.5f, 1));
            _uvs.Add(new Vector2(1,    0.5f));

            _triangles.Add(frontBase);
            _triangles.Add(frontBase + 1);
            _triangles.Add(frontBase + 2);
            _triangles.Add(frontBase);
            _triangles.Add(frontBase + 2);
            _triangles.Add(frontBase + 3);

            AddBackFaceFromFront(frontBase, _vertices.Count - frontBase,
                                 frontTriBase, _triangles.Count - frontTriBase);
        }

        private void AddRoundLeaf(Vector3 pos, Quaternion rot, float size, Color color, float growth)
        {
            int frontBase    = _vertices.Count;
            int frontTriBase = _triangles.Count;

            int segments = 8;
            Vector3 normal = rot * Vector3.back;

            // Center vertex — slight dome curvature
            _vertices.Add(pos + rot * new Vector3(0f, 0.5f, 0.02f) * size);
            _normals.Add(normal);
            _uvs.Add(new Vector2(0.5f, 0.5f));
            _uv2s.Add(new Vector2(0, growth));
            _colors.Add(color);

            // Edge vertices — flat rim
            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2;
                float x = Mathf.Cos(angle) * 0.5f;
                float y = Mathf.Sin(angle) * 0.5f + 0.5f;

                _vertices.Add(pos + rot * new Vector3(x, y, 0f) * size);
                _normals.Add(normal);
                _uvs.Add(new Vector2(x + 0.5f, y));
                _uv2s.Add(new Vector2(0, growth));
                _colors.Add(color);
            }

            for (int i = 0; i < segments; i++)
            {
                _triangles.Add(frontBase);
                _triangles.Add(frontBase + i + 1);
                _triangles.Add(frontBase + i + 2);
            }

            AddBackFaceFromFront(frontBase, _vertices.Count - frontBase,
                                 frontTriBase, _triangles.Count - frontTriBase);
        }

        private void AddNeedleLeaf(Vector3 pos, Quaternion rot, float size, Color color, float growth)
        {
            int frontBase    = _vertices.Count;
            int frontTriBase = _triangles.Count;

            float length = size * 2f;
            float width  = size * 0.1f;

            Vector3 normal = rot * Vector3.back;

            // Slight Z curvature at tip
            Vector3[] localVerts = {
                new Vector3(-width, 0,      0f   ),
                new Vector3( width, 0,      0f   ),
                new Vector3( 0f,   length,  0.02f)
            };

            foreach (var v in localVerts)
            {
                _vertices.Add(pos + rot * v);
                _normals.Add(normal);
                _colors.Add(color);
                _uv2s.Add(new Vector2(0, growth));
            }

            _uvs.Add(new Vector2(0,    0));
            _uvs.Add(new Vector2(1,    0));
            _uvs.Add(new Vector2(0.5f, 1));

            _triangles.Add(frontBase);
            _triangles.Add(frontBase + 2);
            _triangles.Add(frontBase + 1);

            AddBackFaceFromFront(frontBase, _vertices.Count - frontBase,
                                 frontTriBase, _triangles.Count - frontTriBase);
        }

        private void AddFanLeaf(Vector3 pos, Quaternion rot, float size, Color color, float growth)
        {
            int frontBase    = _vertices.Count;
            int frontTriBase = _triangles.Count;

            int   segments = 5;
            float spread   = 60f;

            Vector3 normal = rot * Vector3.back;

            // Stem base
            _vertices.Add(pos);
            _normals.Add(normal);
            _uvs.Add(new Vector2(0.5f, 0));
            _uv2s.Add(new Vector2(0, growth));
            _colors.Add(color);

            for (int i = 0; i <= segments; i++)
            {
                float t         = (float)i / segments;
                float angle     = Mathf.Lerp(-spread / 2, spread / 2, t);
                Vector3 dir     = Quaternion.Euler(0, 0, angle) * Vector3.up;
                // Slight Z curl at tips
                Vector3 tip     = dir * size * 1.5f + new Vector3(0, 0, 0.02f);

                _vertices.Add(pos + rot * tip);
                _normals.Add(normal);
                _uvs.Add(new Vector2(t, 1));
                _uv2s.Add(new Vector2(0, growth));
                _colors.Add(Color.Lerp(color, color * 0.8f, t));
            }

            for (int i = 0; i < segments; i++)
            {
                _triangles.Add(frontBase);
                _triangles.Add(frontBase + i + 1);
                _triangles.Add(frontBase + i + 2);
            }

            AddBackFaceFromFront(frontBase, _vertices.Count - frontBase,
                                 frontTriBase, _triangles.Count - frontTriBase);
        }

        private void AddHeartLeaf(Vector3 pos, Quaternion rot, float size, Color color, float growth)
        {
            int frontBase    = _vertices.Count;
            int frontTriBase = _triangles.Count;

            Vector3 normal = rot * Vector3.back;

            // Slight Z curvature at top lobes
            Vector3[] localVerts = {
                new Vector3( 0f,    0f,   0f   ),   // Bottom tip
                new Vector3(-0.5f,  0.6f, 0f   ),   // Left lobe
                new Vector3(-0.25f, 1f,   0.02f),   // Left top
                new Vector3( 0f,    0.8f, 0.01f),   // Center dip
                new Vector3( 0.25f, 1f,   0.02f),   // Right top
                new Vector3( 0.5f,  0.6f, 0f   )    // Right lobe
            };

            foreach (var v in localVerts)
            {
                _vertices.Add(pos + rot * (v * size));
                _normals.Add(normal);
                _colors.Add(color);
                _uv2s.Add(new Vector2(0, growth));
            }

            _uvs.Add(new Vector2(0.5f, 0));
            _uvs.Add(new Vector2(0,    0.6f));
            _uvs.Add(new Vector2(0.25f, 1));
            _uvs.Add(new Vector2(0.5f, 0.8f));
            _uvs.Add(new Vector2(0.75f, 1));
            _uvs.Add(new Vector2(1,    0.6f));

            int[] indices = { 0, 1, 3, 1, 2, 3, 0, 3, 5, 3, 4, 5 };
            foreach (int idx in indices)
                _triangles.Add(frontBase + idx);

            AddBackFaceFromFront(frontBase, _vertices.Count - frontBase,
                                 frontTriBase, _triangles.Count - frontTriBase);
        }

        private void AddCompoundLeaf(Vector3 pos, Quaternion rot, float size, Color color, float growth)
        {
            // Multiple leaflets — each call to AddOvalLeaf already generates back faces
            int leaflets   = 5;
            float stemLength = size * 1.2f;

            for (int i = 0; i < leaflets; i++)
            {
                float t          = (float)(i + 1) / (leaflets + 1);
                Vector3 stemPos  = pos + rot * Vector3.up * (stemLength * t);
                float sideOffset = (i % 2 == 0 ? -1 : 1) * 0.15f;
                Vector3 leafletPos = stemPos + rot * Vector3.right * sideOffset * size;
                float leafletSize  = size * 0.4f * (1f - t * 0.3f);
                Quaternion leafletRot = rot * Quaternion.Euler(0, 0, sideOffset * 30);

                AddOvalLeaf(leafletPos, leafletRot, leafletSize, color, growth);
            }

            // Terminal leaflet
            Vector3 tipPos = pos + rot * Vector3.up * stemLength;
            AddOvalLeaf(tipPos, rot, size * 0.5f, color, growth);
        }

        private void Clear()
        {
            _vertices.Clear();
            _normals.Clear();
            _uvs.Clear();
            _uv2s.Clear();
            _colors.Clear();
            _triangles.Clear();
        }

        /// <summary>
        /// Generate a single leaf mesh for GPU instancing
        /// </summary>
        public static Mesh CreateLeafPrefabMesh(LeafShape shape, float size = 1f)
        {
            var generator = new FoliageGenerator();
            var points = new List<TreeMeshBuilder.FoliageAttachmentPoint>
            {
                new TreeMeshBuilder.FoliageAttachmentPoint
                {
                    position   = Vector3.zero,
                    normal     = Vector3.up,
                    rotation   = Quaternion.identity,
                    scale      = 1f,
                    branchDepth = 0
                }
            };

            var result = generator.Generate(
                points,
                shape,
                new Vector2(size, size),
                1,
                0f,
                Color.white,
                Color.white,
                0
            );

            return result.combinedMesh;
        }
    }
}
