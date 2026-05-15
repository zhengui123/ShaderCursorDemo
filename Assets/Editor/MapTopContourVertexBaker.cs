#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 用顶点、三角索引与面法线识别「顶面」三角，再用顶面区域的边界边得到外轮廓顶点，
/// 计算每个顶面顶点到轮廓在物体空间 XZ 平面上的欧氏距离，归一化后写入 Color.r（边为 0，向内增大）。
/// Color.b = 0 表示已烘焙，供 Shader 轮廓模式 5 识别；非顶面顶点 R=1 避免侧壁出现整圈高亮。
/// </summary>
public static class MapTopContourVertexBaker
{
    const float BakedBlueChannel = 0f;

    [MenuItem("Tools/地图/烘焙顶面轮廓到顶点色", priority = 10)]
    static void BakeFromSelection()
    {
        var go = Selection.activeGameObject;
        if (go == null)
        {
            EditorUtility.DisplayDialog("顶面轮廓烘焙", "请先选中带 MeshFilter 的物体。", "确定");
            return;
        }

        var mf = go.GetComponent<MeshFilter>();
        if (mf == null)
        {
            EditorUtility.DisplayDialog("顶面轮廓烘焙", "选中物体上没有 MeshFilter。", "确定");
            return;
        }

        var mesh = mf.sharedMesh;
        if (mesh == null)
        {
            EditorUtility.DisplayDialog("顶面轮廓烘焙", "MeshFilter 上没有 Mesh。", "确定");
            return;
        }

        MapMeshBakeUtility.EnsureMeshReadable(mesh);
        if (!mesh.isReadable)
        {
            EditorUtility.DisplayDialog("顶面轮廓烘焙",
                "网格不可读。请等待模型 Read/Write 重新导入完成后再试。", "确定");
            return;
        }

        if (!EditorUtility.DisplayDialog("顶面轮廓烘焙",
                "将写入 Mesh 的顶点色（R=到顶面外轮廓距离归一化，B=0 表示已烘焙）。\n" +
                "若为工程内共享 Mesh 资源，会自动复制为可写实例并赋给 MeshFilter。\n\n是否继续？",
                "烘焙", "取消"))
            return;

        Mesh work = PrepareWritableMesh(mf, mesh);
        Undo.RecordObject(work, "Bake top contour vertex colors");
        Undo.RecordObject(mf, "Bake top contour vertex colors");

        try
        {
            Bake(work, mf.transform, 0.92f, Vector3.up);
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog("烘焙失败", e.Message, "确定");
            return;
        }

        EditorUtility.SetDirty(work);
        if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(work)))
            AssetDatabase.SaveAssets();
        Debug.Log($"[MapTopContourVertexBaker] 完成：{work.name}，顶点数 {work.vertexCount}。请将材质「轮廓模式」设为 5。");
    }

    [MenuItem("Tools/地图/批量烘焙 sd_map 全部板块顶面轮廓", priority = 12)]
    static void BakeAllSdMapTiles()
    {
        var root = Selection.activeGameObject;
        if (root == null)
        {
            var found = GameObject.Find("sd_map/polySurface1");
            if (found == null)
                found = GameObject.Find("sd_map");
            root = found;
        }

        if (root == null)
        {
            EditorUtility.DisplayDialog("批量顶面轮廓烘焙", "请选中 sd_map 或其子节点 polySurface1。", "确定");
            return;
        }

        var filters = root.GetComponentsInChildren<MeshFilter>(true);
        var targets = new List<MeshFilter>();
        foreach (var mf in filters)
        {
            if (mf.sharedMesh != null)
                targets.Add(mf);
        }

        if (targets.Count == 0)
        {
            EditorUtility.DisplayDialog("批量顶面轮廓烘焙", "未找到带 Mesh 的子物体。", "确定");
            return;
        }

        if (!EditorUtility.DisplayDialog("批量顶面轮廓烘焙",
                $"将在「{root.name}」下烘焙 {targets.Count} 个 Mesh 的顶面轮廓顶点色。\n" +
                "材质请使用轮廓模式 5（Custom/MapShader 或 SOC_HologramMap）。\n\n继续？",
                "烘焙", "取消"))
            return;

        int ok = 0;
        foreach (var mf in targets)
        {
            try
            {
                MapMeshBakeUtility.EnsureMeshReadable(mf.sharedMesh);
                if (!mf.sharedMesh.isReadable)
                {
                    Debug.LogWarning($"[MapTopContourVertexBaker] 跳过（不可读）: {mf.name}", mf);
                    continue;
                }

                Mesh work = PrepareWritableMesh(mf, mf.sharedMesh);
                Undo.RecordObject(work, "Batch bake top contour");
                Undo.RecordObject(mf, "Batch bake top contour");
                Bake(work, mf.transform, 0.92f, Vector3.up);
                EditorUtility.SetDirty(work);
                ok++;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[MapTopContourVertexBaker] 跳过 {mf.name}: {e.Message}", mf);
            }
        }

        AssetDatabase.SaveAssets();
        EnsureContourMode5OnRenderers(targets);
        Debug.Log($"[MapTopContourVertexBaker] 批量完成：{ok}/{targets.Count}。已尝试将 Renderer 材质轮廓模式设为 5。");
    }

    static void EnsureContourMode5OnRenderers(List<MeshFilter> filters)
    {
        var mats = new HashSet<Material>();
        foreach (var mf in filters)
        {
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr == null)
                continue;
            foreach (var m in mr.sharedMaterials)
            {
                if (m != null && m.HasProperty("_ContourMode"))
                    mats.Add(m);
            }
        }

        foreach (var m in mats)
        {
            m.SetFloat("_ContourMode", 5f);
            m.SetFloat("_UVRectAssist", 0f);
            EditorUtility.SetDirty(m);
        }
    }

    static Mesh PrepareWritableMesh(MeshFilter mf, Mesh src)
    {
        string path = AssetDatabase.GetAssetPath(src);
        if (!string.IsNullOrEmpty(path))
        {
            var inst = Object.Instantiate(src);
            inst.name = src.name + "_ContourBaked";
            Undo.RecordObject(mf, "Contour mesh instance");
            mf.mesh = inst;
            return inst;
        }

        return src;
    }

    /// <summary>
    /// 运行时也可调用：将顶面轮廓距离写入 mesh.colors。
    /// </summary>
    public static void Bake(Mesh mesh, Transform transform, float topFaceDotMin, Vector3 worldUp)
    {
        var verts = mesh.vertices;
        var tris = mesh.triangles;
        if (verts == null || tris == null || tris.Length < 3)
            throw new System.InvalidOperationException("Mesh 数据无效。");

        Vector3 objUp = Quaternion.Inverse(transform.rotation) * worldUp.normalized;

        float maxH = float.MinValue, minH = float.MaxValue;
        for (int i = 0; i < verts.Length; i++)
        {
            float h = Vector3.Dot(verts[i], objUp);
            if (h > maxH) maxH = h;
            if (h < minH) minH = h;
        }

        float height = maxH - minH;
        // 薄挤出体（sd_map 板块 y 厚度约 0.002）：单靠面法线 ≥0.92 往往只命中十几个三角
        float heightEps = Mathf.Max(height * 0.35f, 1e-5f);
        var meshNormals = mesh.normals;
        bool hasVertNormals = meshNormals != null && meshNormals.Length == verts.Length;

        var topTris = new List<int>();
        for (int t = 0; t < tris.Length; t += 3)
        {
            int ia = tris[t], ib = tris[t + 1], ic = tris[t + 2];
            Vector3 o0 = verts[ia], o1 = verts[ib], o2 = verts[ic];
            Vector3 fn = Vector3.Cross(o1 - o0, o2 - o0);
            Vector3 n0 = hasVertNormals ? meshNormals[ia] : Vector3.zero;
            Vector3 n1 = hasVertNormals ? meshNormals[ib] : Vector3.zero;
            Vector3 n2 = hasVertNormals ? meshNormals[ic] : Vector3.zero;

            if (MapMeshBakeUtility.IsTopTriangle(o0, o1, o2, fn, objUp, maxH, heightEps, topFaceDotMin,
                    hasVertNormals, n0, n1, n2))
                topTris.Add(t);
        }

        if (topTris.Count == 0)
            throw new System.InvalidOperationException("未找到「顶面」三角（请检查法线、厚度或顶面判定阈值）。");

        Debug.Log($"[MapTopContourVertexBaker] {mesh.name}: 顶面三角 {topTris.Count}/{tris.Length / 3}，厚度 {height:F5}");

        var topVerts = new HashSet<int>();
        var edgeCount = new Dictionary<(int, int), int>();

        void AddEdge(int a, int b)
        {
            if (a > b)
            {
                int s = a;
                a = b;
                b = s;
            }

            var k = (a, b);
            edgeCount.TryGetValue(k, out int c);
            edgeCount[k] = c + 1;
        }

        foreach (int tBase in topTris)
        {
            int ia = tris[tBase], ib = tris[tBase + 1], ic = tris[tBase + 2];
            topVerts.Add(ia);
            topVerts.Add(ib);
            topVerts.Add(ic);
            AddEdge(ia, ib);
            AddEdge(ib, ic);
            AddEdge(ic, ia);
        }

        var boundaryVerts = new HashSet<int>();
        foreach (var kv in edgeCount)
        {
            if (kv.Value != 1)
                continue;
            boundaryVerts.Add(kv.Key.Item1);
            boundaryVerts.Add(kv.Key.Item2);
        }

        if (boundaryVerts.Count == 0)
            throw new System.InvalidOperationException("未找到顶面外轮廓边（顶面三角是否封闭且无孤立？）。");

        var boundaryEdges = new List<(int a, int b)>();
        foreach (var kv in edgeCount)
        {
            if (kv.Value != 1)
                continue;
            boundaryEdges.Add((kv.Key.Item1, kv.Key.Item2));
        }

        var boundarySeg = new List<(Vector2 p0, Vector2 p1)>(boundaryEdges.Count);
        foreach (var e in boundaryEdges)
        {
            Vector2 a = new Vector2(verts[e.a].x, verts[e.a].z);
            Vector2 b = new Vector2(verts[e.b].x, verts[e.b].z);
            boundarySeg.Add((a, b));
        }

        var minD = new float[verts.Length];
        for (int i = 0; i < verts.Length; i++)
            minD[i] = float.MaxValue;

        foreach (int vi in topVerts)
        {
            Vector2 p = new Vector2(verts[vi].x, verts[vi].z);
            float best = float.MaxValue;
            foreach (var seg in boundarySeg)
            {
                float d = DistancePointToSegmentXZ(p, seg.p0, seg.p1);
                if (d < best)
                    best = d;
            }

            minD[vi] = best;
        }

        float maxDist = 0f;
        foreach (int vi in topVerts)
        {
            if (minD[vi] > maxDist)
                maxDist = minD[vi];
        }

        if (maxDist < 1e-6f)
            maxDist = 1e-6f;

        var r = new float[verts.Length];
        for (int i = 0; i < r.Length; i++)
            r[i] = 0f;

        // sqrt 拉开内外对比，避免 Laplacian 后整面 R→0 导致顶面全高光
        foreach (int vi in topVerts)
            r[vi] = Mathf.Sqrt(Mathf.Clamp01(minD[vi] / maxDist));

        foreach (int vi in boundaryVerts)
            r[vi] = 0f;

        var adjTop = BuildTopAdjacency(tris, topTris, topVerts);
        LaplacianSmoothR(r, topVerts, boundaryVerts, adjTop, iterations: 4, lambda: 0.15f);

        foreach (int vi in topVerts)
        {
            if (boundaryVerts.Contains(vi))
                continue;
            r[vi] = Mathf.Clamp(r[vi], 0.12f, 1f);
        }

        foreach (int vi in boundaryVerts)
            r[vi] = 0f;

        var cols = new Color[verts.Length];
        for (int i = 0; i < cols.Length; i++)
            cols[i] = new Color(0f, 0f, 1f, 1f);

        foreach (int vi in topVerts)
            cols[vi] = new Color(r[vi], 0f, BakedBlueChannel, 1f);

        mesh.colors = cols;
    }

    static float DistancePointToSegmentXZ(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float sql = ab.sqrMagnitude;
        if (sql < 1e-12f)
            return Vector2.Distance(p, a);
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / sql);
        Vector2 c = a + t * ab;
        return Vector2.Distance(p, c);
    }

    static Dictionary<int, List<int>> BuildTopAdjacency(int[] tris, List<int> topTris, HashSet<int> topVerts)
    {
        var adj = new Dictionary<int, List<int>>();
        void Link(int a, int b)
        {
            if (!topVerts.Contains(a) || !topVerts.Contains(b))
                return;
            if (!adj.TryGetValue(a, out var la))
            {
                la = new List<int>(6);
                adj[a] = la;
            }

            if (!la.Contains(b))
                la.Add(b);
            if (!adj.TryGetValue(b, out var lb))
            {
                lb = new List<int>(6);
                adj[b] = lb;
            }

            if (!lb.Contains(a))
                lb.Add(a);
        }

        foreach (int tBase in topTris)
        {
            int ia = tris[tBase], ib = tris[tBase + 1], ic = tris[tBase + 2];
            Link(ia, ib);
            Link(ib, ic);
            Link(ic, ia);
        }

        return adj;
    }

    static void LaplacianSmoothR(float[] r, HashSet<int> topVerts, HashSet<int> boundaryVerts,
        Dictionary<int, List<int>> adjTop, int iterations, float lambda)
    {
        for (int it = 0; it < iterations; it++)
        {
            var next = (float[])r.Clone();
            foreach (int vi in topVerts)
            {
                if (boundaryVerts.Contains(vi))
                {
                    next[vi] = 0f;
                    continue;
                }

                if (!adjTop.TryGetValue(vi, out var nb) || nb.Count == 0)
                    continue;

                float s = 0f;
                foreach (int j in nb)
                    s += r[j];

                float avg = s / nb.Count;
                next[vi] = Mathf.Lerp(r[vi], avg, lambda);
            }

            for (int i = 0; i < r.Length; i++)
                r[i] = next[i];

            foreach (int b in boundaryVerts)
                r[b] = 0f;
        }
    }
}
#endif
