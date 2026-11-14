using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RagdollFootball.Field.EditorTools
{
    /// <summary>
    /// Collects geometry for the generated field meshes. Flat shapes lie on a horizontal plane
    /// and always face up; 2D points are (x, z). Panels are double-sided vertical or horizontal quads.
    /// </summary>
    internal sealed class FieldMeshBuilder
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<List<int>> submeshes = new List<List<int>>();
        readonly float uvMetres;

        /// <param name="submeshCount">One submesh per material.</param>
        /// <param name="uvMetres">World metres per UV unit on flat shapes (texture tile size).</param>
        public FieldMeshBuilder(int submeshCount = 1, float uvMetres = 1f)
        {
            for (int i = 0; i < submeshCount; i++) submeshes.Add(new List<int>());
            this.uvMetres = uvMetres;
        }

        public int VertexCount => vertices.Count;

        /// <summary>Axis-aligned rectangle between two corners, in any order.</summary>
        public void AddRect(float x0, float z0, float x1, float z1, float y, int submesh = 0)
        {
            float minX = Mathf.Min(x0, x1), maxX = Mathf.Max(x0, x1);
            float minZ = Mathf.Min(z0, z1), maxZ = Mathf.Max(z0, z1);
            if (maxX - minX <= 0f || maxZ - minZ <= 0f) return;
            AddFlatQuad(new Vector2(minX, minZ), new Vector2(minX, maxZ), new Vector2(maxX, maxZ), new Vector2(maxX, minZ), y, submesh);
        }

        /// <summary>Band between two radii around a centre, from angle a0 to a1 (radians, 0 = +X, PI/2 = +Z).</summary>
        public void AddArcBand(Vector2 center, float innerRadius, float outerRadius, float a0, float a1, int segments, float y, int submesh = 0)
        {
            AddBand(
                t => center + Polar(innerRadius, Mathf.Lerp(a0, a1, t)),
                t => center + Polar(outerRadius, Mathf.Lerp(a0, a1, t)),
                segments, y, submesh);
        }

        /// <summary>
        /// Band whose inner and outer edges are given as curves over t in [0, 1].
        /// Lets the two edges span different angles, e.g. an arc clipped by a straight line.
        /// </summary>
        public void AddBand(Func<float, Vector2> inner, Func<float, Vector2> outer, int segments, float y, int submesh = 0)
        {
            segments = Mathf.Max(1, segments);
            for (int i = 0; i < segments; i++)
            {
                float t0 = (float)i / segments, t1 = (float)(i + 1) / segments;
                AddFlatQuad(inner(t0), outer(t0), outer(t1), inner(t1), y, submesh);
            }
        }

        /// <summary>Filled disc.</summary>
        public void AddDisc(Vector2 center, float radius, int segments, float y, int submesh = 0)
        {
            segments = Mathf.Max(3, segments);
            for (int i = 0; i < segments; i++)
            {
                float a0 = 2f * Mathf.PI * i / segments, a1 = 2f * Mathf.PI * (i + 1) / segments;
                AddFlatTriangle(center, center + Polar(radius, a0), center + Polar(radius, a1), y, submesh);
            }
        }

        /// <summary>Quad with corners going round its edge (either direction), facing up.</summary>
        public void AddFlatQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float y, int submesh = 0)
        {
            if (UpCross(a, b, c) < 0f) (b, d) = (d, b);
            int i = vertices.Count;
            AddFlatVertex(a, y);
            AddFlatVertex(b, y);
            AddFlatVertex(c, y);
            AddFlatVertex(d, y);
            List<int> tris = submeshes[submesh];
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        public void AddFlatTriangle(Vector2 a, Vector2 b, Vector2 c, float y, int submesh = 0)
        {
            if (UpCross(a, b, c) < 0f) (b, c) = (c, b);
            int i = vertices.Count;
            AddFlatVertex(a, y);
            AddFlatVertex(b, y);
            AddFlatVertex(c, y);
            List<int> tris = submeshes[submesh];
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        /// <summary>
        /// Double-sided quad (front and back faces with their own normals), for nets.
        /// Corners go round the edge; uv0..uv3 match p0..p3.
        /// </summary>
        public void AddPanel(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
            Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3, int submesh = 0)
        {
            Vector3 n = Vector3.Cross(p1 - p0, p2 - p0).normalized;
            List<int> tris = submeshes[submesh];

            int i = vertices.Count;
            AddVertex(p0, n, uv0); AddVertex(p1, n, uv1); AddVertex(p2, n, uv2); AddVertex(p3, n, uv3);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);

            int j = vertices.Count;
            AddVertex(p0, -n, uv0); AddVertex(p1, -n, uv1); AddVertex(p2, -n, uv2); AddVertex(p3, -n, uv3);
            tris.Add(j); tris.Add(j + 2); tris.Add(j + 1);
            tris.Add(j); tris.Add(j + 3); tris.Add(j + 2);
        }

        /// <summary>Replaces the contents of a mesh (usually a mesh asset) with the collected geometry.</summary>
        public void WriteTo(Mesh mesh)
        {
            mesh.Clear();
            mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = submeshes.Count;
            for (int i = 0; i < submeshes.Count; i++) mesh.SetTriangles(submeshes[i], i, true);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
        }

        public static Vector2 Polar(float radius, float angle) =>
            new Vector2(radius * Mathf.Cos(angle), radius * Mathf.Sin(angle));

        // Y component of (b - a) x (c - a) for points on the XZ plane. Positive means the
        // triangle a, b, c faces +Y with Unity's clockwise front-face winding.
        static float UpCross(Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 u = b - a, v = c - a;
            return u.y * v.x - u.x * v.y;
        }

        void AddFlatVertex(Vector2 p, float y) =>
            AddVertex(new Vector3(p.x, y, p.y), Vector3.up, p / uvMetres);

        void AddVertex(Vector3 position, Vector3 normal, Vector2 uv)
        {
            vertices.Add(position);
            normals.Add(normal);
            uvs.Add(uv);
        }
    }
}
