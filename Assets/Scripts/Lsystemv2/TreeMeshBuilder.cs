using System.Collections.Generic;
using UnityEngine;

namespace GlimmerDiary.Flora
{
    /// <summary>
    /// Builds high-quality tree meshes from L-System branch segments.
    /// 
    /// Key fix: Adjacent segments now SHARE vertices at connection points,
    /// eliminating visual gaps and ensuring smooth branch connections.
    /// 
    /// Supports:
    /// - Cylindrical branches with variable radius
    /// - Trunk bulge (for baobab-style trees)
    /// - Surface noise for organic feel
    /// - UV mapping for bark textures
    /// - UV2 encoding for growth animation
    /// - Foliage attachment point generation
    /// </summary>
    public class TreeMeshBuilder
    {
        public struct TreeMeshResult
        {
            public Mesh trunkMesh;
            public List<FoliageAttachmentPoint> foliagePoints;
            public Bounds bounds;
        }
        
        public struct FoliageAttachmentPoint
        {
            public Vector3 position;
            public Vector3 normal;
            public Quaternion rotation;
            public float scale;
            public int branchDepth;
        }
        
        /// <summary>
        /// Stores ring vertex data for sharing between segments
        /// </summary>
        private struct RingData
        {
            public int startVertexIndex;  // First vertex index of this ring
            public Vector3 center;
            public float radius;
            public Vector3 direction;     // Branch direction at this point
            public Vector3 perpendicular;
            public Vector3 perpendicular2;
        }
        
        private readonly List<Vector3> _vertices = new();
        private readonly List<Vector3> _normals = new();
        private readonly List<Vector2> _uvs = new();
        private readonly List<Vector2> _uv2s = new();
        private readonly List<Color> _colors = new();
        private readonly List<int> _triangles = new();
        private readonly List<FoliageAttachmentPoint> _foliagePoints = new();
        
        // Maps segment index -> its end ring data (for vertex sharing)
        private readonly Dictionary<int, RingData> _segmentEndRings = new();
        
        /// <summary>
        /// Build a complete tree mesh from branch segments
        /// </summary>
        public TreeMeshResult Build(
            List<TurtleInterpreter3D.BranchSegment> segments,
            PlantDefinition plant)
        {
            var trunk = plant.trunk;
            
            return Build(
                segments,
                trunk.radialSegments,
                trunk.baseBulge,
                trunk.surfaceNoise,
                trunk.twistPerSegment,
                trunk.barkTiling,
                plant.foliage.distribution,
                plant.foliage.leavesPerCluster
            );
        }
        
        public TreeMeshResult Build(
            List<TurtleInterpreter3D.BranchSegment> segments,
            int radialSegments = 8,
            float baseBulge = 0f,
            float surfaceNoise = 0f,
            float twistPerSegment = 0f,
            float uvTiling = 2f,
            LeafDistribution leafDistribution = LeafDistribution.BranchTips,
            int leavesPerCluster = 5)
        {
            Clear();
            
            if (segments == null || segments.Count == 0)
            {
                return new TreeMeshResult 
                { 
                    trunkMesh = new Mesh { name = "EmptyTree" },
                    foliagePoints = new List<FoliageAttachmentPoint>(),
                    bounds = new Bounds()
                };
            }
            
            // Find max depth for foliage and color calculations
            int maxDepth = 0;
            foreach (var seg in segments)
            {
                maxDepth = Mathf.Max(maxDepth, seg.depth);
            }
            
            // Determine which segments are branch tips
            var isBranchTip = DetermineBranchTips(segments);
            
            // Track accumulated UV length per branch path
            var segmentUVOffset = new Dictionary<int, float>();
            
            // Process each segment
            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                
                // Calculate segment length for UV mapping
                float segmentLength = Vector3.Distance(segment.start, segment.end);
                
                // Get UV offset from parent or start at 0
                float uvOffset = 0f;
                if (segment.parentSegmentIndex >= 0 && segmentUVOffset.ContainsKey(segment.parentSegmentIndex))
                {
                    uvOffset = segmentUVOffset[segment.parentSegmentIndex];
                }
                
                // Apply bulge at base (for baobab-style trunks)
                float startRadiusModified = segment.startRadius;
                float endRadiusModified = segment.endRadius;
                
                if (baseBulge > 0 && segment.depth == 0)
                {
                    float bulgeT = 1f - segment.normalizedPosition;
                    float bulgeFactor = 1f + baseBulge * Mathf.Sin(bulgeT * Mathf.PI * 0.5f);
                    startRadiusModified *= bulgeFactor;
                    endRadiusModified *= bulgeFactor * 0.9f;
                }
                
                // Calculate coordinate frame for this segment
                Vector3 direction = (segment.end - segment.start).normalized;
                if (direction.sqrMagnitude < 0.001f) direction = Vector3.up;
                
                Vector3 perpendicular = GetPerpendicular(direction);
                Vector3 perpendicular2 = Vector3.Cross(direction, perpendicular).normalized;
                
                // Twist accumulation
                float twistAngle = twistPerSegment * segment.depth;
                
                // Check if we can reuse parent's end ring
                bool reuseStartRing = false;
                RingData parentEndRing = default;
                
                if (segment.parentSegmentIndex >= 0 && 
                    _segmentEndRings.TryGetValue(segment.parentSegmentIndex, out parentEndRing))
                {
                    var parentSeg = segments[segment.parentSegmentIndex];
                    
                    // Verify this segment actually connects to parent's end
                    if (Vector3.Distance(segment.start, parentSeg.end) < 0.001f)
                    {
                        reuseStartRing = true;
                    }
                }
                
                int startRingFirstVertex;
                int endRingFirstVertex;
                
                if (reuseStartRing)
                {
                    // Reuse parent's end ring as our start ring
                    startRingFirstVertex = parentEndRing.startVertexIndex;
                    
                    // Only generate the end ring
                    endRingFirstVertex = _vertices.Count;
                    GenerateRing(
                        segment.end,
                        endRadiusModified,
                        direction,
                        perpendicular,
                        perpendicular2,
                        radialSegments,
                        twistAngle,
                        surfaceNoise,
                        segment.normalizedPosition,
                        uvOffset + segmentLength * uvTiling,
                        segment.depth,
                        maxDepth,
                        i  // noise seed
                    );
                    
                    // Generate triangles connecting parent's end ring to our end ring
                    // Need to handle potential direction change at branch point
                    GenerateTrianglesWithDirectionBlend(
                        startRingFirstVertex,
                        endRingFirstVertex,
                        radialSegments,
                        parentEndRing.direction,
                        direction,
                        parentEndRing.perpendicular,
                        perpendicular,
                        parentEndRing.perpendicular2,
                        perpendicular2
                    );
                }
                else
                {
                    // Generate both start and end rings
                    startRingFirstVertex = _vertices.Count;
                    GenerateRing(
                        segment.start,
                        startRadiusModified,
                        direction,
                        perpendicular,
                        perpendicular2,
                        radialSegments,
                        twistAngle,
                        surfaceNoise,
                        segment.normalizedPosition,
                        uvOffset,
                        segment.depth,
                        maxDepth,
                        i * 2  // noise seed
                    );
                    
                    endRingFirstVertex = _vertices.Count;
                    GenerateRing(
                        segment.end,
                        endRadiusModified,
                        direction,
                        perpendicular,
                        perpendicular2,
                        radialSegments,
                        twistAngle,
                        surfaceNoise,
                        segment.normalizedPosition,
                        uvOffset + segmentLength * uvTiling,
                        segment.depth,
                        maxDepth,
                        i * 2 + 1  // noise seed
                    );
                    
                    // Generate triangles connecting the two rings
                    GenerateTrianglesBetweenRings(startRingFirstVertex, endRingFirstVertex, radialSegments);
                }
                
                // Store this segment's end ring for potential reuse by children
                _segmentEndRings[i] = new RingData
                {
                    startVertexIndex = endRingFirstVertex,
                    center = segment.end,
                    radius = endRadiusModified,
                    direction = direction,
                    perpendicular = perpendicular,
                    perpendicular2 = perpendicular2
                };
                
                // Store UV offset for this segment's children
                segmentUVOffset[i] = uvOffset + segmentLength * uvTiling;
                
                // Generate foliage attachment points
                bool shouldAttachFoliage = ShouldAttachFoliage(
                    leafDistribution, 
                    segment, 
                    isBranchTip.Contains(i), 
                    maxDepth
                );
                
                if (shouldAttachFoliage)
                {
                    _foliagePoints.Add(new FoliageAttachmentPoint
                    {
                        position = segment.end,
                        normal = direction,
                        rotation = segment.orientation,
                        scale = Mathf.Lerp(1f, 0.3f, (float)segment.depth / maxDepth),
                        branchDepth = segment.depth
                    });
                }
            }
            
            // Create mesh
            var mesh = new Mesh { name = "ProceduralTree" };
            
            if (_vertices.Count > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.SetUVs(1, _uv2s);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_triangles, 0);
            
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();   // Smooths normals at branch joints (also fixes surfaceNoise shading)
            mesh.RecalculateTangents();
            
            return new TreeMeshResult
            {
                trunkMesh = mesh,
                foliagePoints = new List<FoliageAttachmentPoint>(_foliagePoints),
                bounds = mesh.bounds
            };
        }
        
        /// <summary>
        /// Generate a single ring of vertices
        /// </summary>
        private void GenerateRing(
            Vector3 center,
            float radius,
            Vector3 direction,
            Vector3 perpendicular,
            Vector3 perpendicular2,
            int radialSegments,
            float twistAngle,
            float surfaceNoise,
            float normalizedPosition,
            float vCoord,
            int depth,
            int maxDepth,
            int noiseSeed)
        {
            for (int j = 0; j <= radialSegments; j++)
            {
                float angle = (float)j / radialSegments * Mathf.PI * 2f + twistAngle * Mathf.Deg2Rad;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                
                Vector3 normal = (perpendicular * cos + perpendicular2 * sin).normalized;
                Vector3 vertex = center + normal * radius;
                
                // Apply surface noise for organic feel
                if (surfaceNoise > 0)
                {
                    float noise = Mathf.PerlinNoise(
                        noiseSeed * 0.1f + j * 0.3f,
                        normalizedPosition * 10f
                    ) * 2f - 1f;
                    vertex += normal * noise * surfaceNoise * radius;
                }
                
                _vertices.Add(vertex);
                _normals.Add(normal);
                _uvs.Add(new Vector2((float)j / radialSegments, vCoord));
                
                // UV2 encodes growth progress and branch depth
                float growthValue = normalizedPosition;
                float depthValue = (float)depth / Mathf.Max(1, maxDepth);
                _uv2s.Add(new Vector2(depthValue, growthValue));
                
                // Vertex color encodes wind influence and other data
                float windInfluence = Mathf.Lerp(0.1f, 1f, depthValue);
                _colors.Add(new Color(windInfluence, depthValue, growthValue, 1f));
            }
        }
        
        /// <summary>
        /// Generate triangles between two rings (standard case)
        /// </summary>
        private void GenerateTrianglesBetweenRings(int startRingFirst, int endRingFirst, int radialSegments)
        {
            int vertsPerRing = radialSegments + 1;
            
            for (int j = 0; j < radialSegments; j++)
            {
                int bl = startRingFirst + j;           // bottom left
                int br = startRingFirst + j + 1;       // bottom right
                int tl = endRingFirst + j;             // top left
                int tr = endRingFirst + j + 1;         // top right
                
                // First triangle
                _triangles.Add(bl);
                _triangles.Add(tl);
                _triangles.Add(br);
                
                // Second triangle
                _triangles.Add(br);
                _triangles.Add(tl);
                _triangles.Add(tr);
            }
        }
        
        /// <summary>
        /// Generate triangles when connecting rings with different orientations (at branch points).
        /// This handles the case where a child branch continues from parent's end ring
        /// but may have a different direction.
        /// </summary>
        private void GenerateTrianglesWithDirectionBlend(
            int startRingFirst,
            int endRingFirst,
            int radialSegments,
            Vector3 startDirection,
            Vector3 endDirection,
            Vector3 startPerp,
            Vector3 endPerp,
            Vector3 startPerp2,
            Vector3 endPerp2)
        {
            // Project startPerp onto the plane perpendicular to endDirection
            // to find the rotational offset between the two coordinate frames.
            Vector3 projected = startPerp - Vector3.Dot(startPerp, endDirection) * endDirection;

            if (projected.sqrMagnitude < 0.0001f)
            {
                // Degenerate: startPerp is (nearly) parallel to endDirection — no offset possible
                GenerateTrianglesBetweenRings(startRingFirst, endRingFirst, radialSegments);
                return;
            }

            projected.Normalize();

            // Signed angle from projected startPerp to endPerp, around endDirection axis
            float dot   = Vector3.Dot(projected, endPerp);
            float cross = Vector3.Dot(Vector3.Cross(projected, endPerp), endDirection);
            float angleDeg = Mathf.Atan2(cross, dot) * Mathf.Rad2Deg;

            // Convert to a whole-vertex offset (round to nearest ring slot)
            int vertexOffset = Mathf.RoundToInt(angleDeg / 360f * radialSegments);
            vertexOffset = ((vertexOffset % radialSegments) + radialSegments) % radialSegments;

            // Connect rings: vertex j of start ring → vertex (j+offset) % radialSegments of end ring
            for (int j = 0; j < radialSegments; j++)
            {
                int bl = startRingFirst + j;
                int br = startRingFirst + j + 1;

                // Wrap end ring indices (excludes UV-seam duplicate at radialSegments)
                int jEnd  = (j + vertexOffset)     % radialSegments;
                int jEnd1 = (j + vertexOffset + 1) % radialSegments;
                int tl = endRingFirst + jEnd;
                int tr = endRingFirst + jEnd1;

                _triangles.Add(bl);
                _triangles.Add(tl);
                _triangles.Add(br);

                _triangles.Add(br);
                _triangles.Add(tl);
                _triangles.Add(tr);
            }
        }
        
        /// <summary>
        /// Determine which segments are branch tips (have no children)
        /// </summary>
        private HashSet<int> DetermineBranchTips(List<TurtleInterpreter3D.BranchSegment> segments)
        {
            var isBranchTip = new HashSet<int>();
            var hasChild = new HashSet<int>();
            
            // Mark all segments as potential tips
            for (int i = 0; i < segments.Count; i++)
            {
                isBranchTip.Add(i);
            }
            
            // Remove segments that have children
            for (int i = 0; i < segments.Count; i++)
            {
                int parentIndex = segments[i].parentSegmentIndex;
                if (parentIndex >= 0)
                {
                    isBranchTip.Remove(parentIndex);
                }
            }
            
            return isBranchTip;
        }
        
        private bool ShouldAttachFoliage(
            LeafDistribution distribution, 
            TurtleInterpreter3D.BranchSegment segment,
            bool isTip,
            int maxDepth)
        {
            switch (distribution)
            {
                case LeafDistribution.BranchTips:
                    return isTip;
                    
                case LeafDistribution.AlongBranches:
                    return segment.depth >= maxDepth / 2;
                    
                case LeafDistribution.Clusters:
                    return isTip || (segment.depth >= maxDepth - 1 && Random.value > 0.5f);
                    
                case LeafDistribution.Umbrella:
                    return segment.depth == maxDepth - 1 || isTip;
                    
                case LeafDistribution.Layered:
                    return segment.depth % 2 == 0 && segment.depth >= 2;
                    
                case LeafDistribution.Weeping:
                    return segment.depth >= maxDepth - 2;
                    
                case LeafDistribution.None:
                default:
                    return false;
            }
        }
        
        private Vector3 GetPerpendicular(Vector3 direction)
        {
            // Choose a reference vector that's not parallel to direction
            Vector3 reference = Mathf.Abs(Vector3.Dot(direction, Vector3.right)) < 0.9f 
                ? Vector3.right 
                : Vector3.forward;
            
            Vector3 perp = Vector3.Cross(direction, reference);
            return perp.normalized;
        }
        
        private void Clear()
        {
            _vertices.Clear();
            _normals.Clear();
            _uvs.Clear();
            _uv2s.Clear();
            _colors.Clear();
            _triangles.Clear();
            _foliagePoints.Clear();
            _segmentEndRings.Clear();
        }
        
        /// <summary>
        /// Builds a simplified 2D quad-based tree (for HD-2D style or performance)
        /// </summary>
        public Mesh BuildFlat(List<TurtleInterpreter3D.BranchSegment> segments, float uvTiling = 1f)
        {
            Clear();
            
            if (segments == null || segments.Count == 0)
            {
                return new Mesh { name = "EmptyTreeFlat" };
            }
            
            foreach (var segment in segments)
            {
                int baseIndex = _vertices.Count;
                
                Vector3 direction = (segment.end - segment.start).normalized;
                if (direction.sqrMagnitude < 0.001f) direction = Vector3.up;
                
                // For 2D, use a perpendicular in the XY plane
                Vector3 perpendicular = new Vector3(-direction.y, direction.x, 0);
                if (perpendicular.sqrMagnitude < 0.001f)
                    perpendicular = new Vector3(1, 0, 0);
                perpendicular = perpendicular.normalized;
                
                Vector3 bl = segment.start - perpendicular * segment.startRadius;
                Vector3 br = segment.start + perpendicular * segment.startRadius;
                Vector3 tl = segment.end - perpendicular * segment.endRadius;
                Vector3 tr = segment.end + perpendicular * segment.endRadius;
                
                _vertices.Add(bl);
                _vertices.Add(br);
                _vertices.Add(tl);
                _vertices.Add(tr);
                
                for (int i = 0; i < 4; i++)
                    _normals.Add(Vector3.back);
                
                _uvs.Add(new Vector2(0, 0));
                _uvs.Add(new Vector2(1, 0));
                _uvs.Add(new Vector2(0, uvTiling));
                _uvs.Add(new Vector2(1, uvTiling));
                
                float growth = segment.normalizedPosition;
                for (int i = 0; i < 4; i++)
                    _uv2s.Add(new Vector2(0, growth));
                
                _triangles.Add(baseIndex);
                _triangles.Add(baseIndex + 2);
                _triangles.Add(baseIndex + 1);
                _triangles.Add(baseIndex + 1);
                _triangles.Add(baseIndex + 2);
                _triangles.Add(baseIndex + 3);
            }
            
            var mesh = new Mesh { name = "ProceduralTreeFlat" };
            mesh.SetVertices(_vertices);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.SetUVs(1, _uv2s);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateBounds();
            
            return mesh;
        }
        
        #region Debug Helpers
        
        /// <summary>
        /// Create a debug visualization showing ring connections
        /// </summary>
        public static void DrawDebugRings(Mesh mesh, Transform transform, int radialSegments)
        {
            if (mesh == null) return;
            
            var vertices = mesh.vertices;
            int vertsPerRing = radialSegments + 1;
            int ringCount = vertices.Length / vertsPerRing;
            
            for (int ring = 0; ring < ringCount; ring++)
            {
                Color color = Color.HSVToRGB((float)ring / ringCount, 0.8f, 0.9f);
                Gizmos.color = color;
                
                for (int j = 0; j < radialSegments; j++)
                {
                    int idx1 = ring * vertsPerRing + j;
                    int idx2 = ring * vertsPerRing + j + 1;
                    
                    if (idx1 < vertices.Length && idx2 < vertices.Length)
                    {
                        Vector3 v1 = transform.TransformPoint(vertices[idx1]);
                        Vector3 v2 = transform.TransformPoint(vertices[idx2]);
                        Gizmos.DrawLine(v1, v2);
                    }
                }
            }
        }
        
        #endregion
    }
}