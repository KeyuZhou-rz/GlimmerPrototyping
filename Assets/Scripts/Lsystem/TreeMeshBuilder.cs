using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SocialPlatforms.GameCenter;

public static class TreeMeshBuilder
{
    public static Mesh Build(List<TurtleInterpreter.BranchSegment> segments, int radialSegments = 6)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();      // Standard UV
        var uv2s = new List<Vector2>();     // Growth encoding
        var wood_triangles = new List<int>();
        var leaves_triangles = new List<int>();

        foreach (var segment in segments)
        {
            int baseIndex = vertices.Count;
            
            // Build a cylinder/cone for each segment
            AddCylinderSegment(
                vertices, normals, uvs, uv2s, wood_triangles,
                segment.start, segment.end,
                segment.startWidth, segment.endWidth,
                segment.depth / 100f,  // Normalized depth for UV2
                radialSegments,
                baseIndex
            );
            if (segment.leafJudge){
                AddCrossedLeaf(vertices, normals, uvs, uv2s, leaves_triangles, segment.end, 0.5f);
            }
        }

        var mesh = new Mesh();
        mesh.name = "ProceduralTree";
        
        // Use 32-bit indices if we have many vertices
        if (vertices.Count > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, uv2s);  // UV2 for growth shader
        mesh.SetTriangles(wood_triangles, 0);
        
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        mesh.subMeshCount = 2;
        mesh.SetTriangles(wood_triangles,0); // material 0 in charge of trunck
        mesh.SetTriangles(leaves_triangles, 1); // 1 in charge of leaves

        return mesh;
    }

    private static void AddCrossedLeaf(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<Vector2>uv2s,
        List<int>triangles,
        Vector3 center,
        float size
    )
    {
        float halfSize = size*0.5f;
        int baseIndex = vertices.Count;

        vertices.Add(center + new Vector3(-halfSize, -halfSize,0));
        vertices.Add(center + new  Vector3(halfSize, -halfSize,0));
        vertices.Add(center + new Vector3(-halfSize, halfSize, 0));
        vertices.Add(center + new Vector3(halfSize, halfSize,0));

        for (int i = 0;i<4; i++)
        {
            normals.Add(Vector3.up);
            uv2s.Add(new Vector2(1,1));
        }
        uvs.Add(new Vector2(0,0));uvs.Add(new Vector2(1,0));
        uvs.Add(new Vector2(0,1)); uvs.Add(new Vector2(1,1));

        triangles.Add(baseIndex + 0); triangles.Add(baseIndex + 2); triangles.Add(baseIndex + 1);
        triangles.Add(baseIndex + 1); triangles.Add(baseIndex + 2); triangles.Add(baseIndex + 3);
        
        baseIndex += 4;
        vertices.Add(center + new Vector3(0, -halfSize, -halfSize));
        vertices.Add(center + new Vector3(0, -halfSize,  halfSize));
        vertices.Add(center + new Vector3(0,  halfSize, -halfSize));
        vertices.Add(center + new Vector3(0,  halfSize,  halfSize));

        for(int i=0; i<4; i++) {
        normals.Add(Vector3.up); 
        uv2s.Add(new Vector2(1, 1));
    }
    
        uvs.Add(new Vector2(0,0)); 
        uvs.Add(new Vector2(1,0)); 
        uvs.Add(new Vector2(0,1)); 
        uvs.Add(new Vector2(1,1));

        triangles.Add(baseIndex + 0); 
        triangles.Add(baseIndex + 2); 
        triangles.Add(baseIndex + 1);
        
        triangles.Add(baseIndex + 1); 
        triangles.Add(baseIndex + 2); 
        triangles.Add(baseIndex + 3);


    }
    private static void AddCylinderSegment(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<Vector2> uv2s,
        List<int> triangles,
        Vector3 start,
        Vector3 end,
        float startRadius,
        float endRadius,
        float growthValue,
        int segments,
        int baseIndex)
    {
        Vector3 direction = (end - start).normalized;
        
        // Create a coordinate system for the cylinder
        Vector3 perpendicular = Vector3.Cross(direction, Vector3.right);
        if (perpendicular.sqrMagnitude < 0.001f)
            perpendicular = Vector3.Cross(direction, Vector3.forward);
        perpendicular.Normalize();

        Vector3 perpendicular2 = Vector3.Cross(direction, perpendicular).normalized;

        // Generate vertices for both rings
        for (int ring = 0; ring < 2; ring++)
        {
            Vector3 center = ring == 0 ? start : end;
            float radius = ring == 0 ? startRadius : endRadius;
            float v = ring;

            for (int i = 0; i <= segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                Vector3 normal = (perpendicular * cos + perpendicular2 * sin).normalized;
                Vector3 vertex = center + normal * radius;

                vertices.Add(vertex);
                normals.Add(normal);
                uvs.Add(new Vector2((float)i / segments, v));
                uv2s.Add(new Vector2(0, growthValue));  // Y = growth progress encoding
            }
        }

        // Generate triangles connecting the two rings
        int vertsPerRing = segments + 1;
        for (int i = 0; i < segments; i++)
        {
            int bottomLeft = baseIndex + i;
            int bottomRight = baseIndex + i + 1;
            int topLeft = baseIndex + vertsPerRing + i;
            int topRight = baseIndex + vertsPerRing + i + 1;

            // Two triangles per quad
            triangles.Add(bottomLeft);
            triangles.Add(topLeft);
            triangles.Add(bottomRight);

            triangles.Add(bottomRight);
            triangles.Add(topLeft);
            triangles.Add(topRight);
        }
    }

    // Simplified version - uses quads instead of cylinders (better for 2D style)
    public static Mesh BuildFlat(List<TurtleInterpreter.BranchSegment> segments)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var uv2s = new List<Vector2>();
        var triangles = new List<int>();

        foreach (var segment in segments)
        {
            int baseIndex = vertices.Count;
            
            Vector3 direction = (segment.end - segment.start).normalized;
            Vector3 perpendicular = new Vector3(-direction.y, direction.x, 0).normalized;

            // Four corners of the quad
            Vector3 bl = segment.start - perpendicular * segment.startWidth;
            Vector3 br = segment.start + perpendicular * segment.startWidth;
            Vector3 tl = segment.end - perpendicular * segment.endWidth;
            Vector3 tr = segment.end + perpendicular * segment.endWidth;

            float growth = segment.depth / 100f;

            vertices.Add(bl); vertices.Add(br); vertices.Add(tl); vertices.Add(tr);
            
            normals.Add(Vector3.back); normals.Add(Vector3.back);
            normals.Add(Vector3.back); normals.Add(Vector3.back);
            
            uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0));
            uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1));
            
            uv2s.Add(new Vector2(0, growth)); uv2s.Add(new Vector2(0, growth));
            uv2s.Add(new Vector2(0, growth)); uv2s.Add(new Vector2(0, growth));

            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 3);
        }

        var mesh = new Mesh { name = "ProceduralTreeFlat" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, uv2s);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();

        return mesh;
    }
}