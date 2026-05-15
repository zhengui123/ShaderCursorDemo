#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 在 UV0 平面栅格化「到顶面外轮廓边」的 2D 距离场，保存为 PNG；轮廓由顶面三角边界边在 UV 上的线段表示。
/// 片元按 UV 双线性采样贴图，避免顶点色插值导致的斜纹错乱。并保存烘焙后的 Mesh 为工程资源。
/// </summary>
public static class MapContourSdfMeshSave
{
    const float TopFaceDot = 0.92f;

    [MenuItem("Tools/地图/烘焙 UV 轮廓距离贴图并保存网格", priority = 11)]
    static void Run()
    {
        var go = Selection.activeGameObject;
        if (go == null)
        {
            EditorUtility.DisplayDialog("轮廓贴图烘焙", "请先选中场景中带 MeshFilter 与 MeshRenderer 的物体。", "确定");
            return;
        }

        var mf = go.GetComponent<MeshFilter>();
        var mr = go.GetComponent<MeshRenderer>();
        if (mf == null || mr == null)
        {
            EditorUtility.DisplayDialog("轮廓贴图烘焙", "需要同时存在 MeshFilter 与 MeshRenderer（用于赋材质与贴图）。", "确定");
            return;
        }

        var src = mf.sharedMesh;
        if (src == null)
        {
            EditorUtility.DisplayDialog("轮廓贴图烘焙", "MeshFilter 上没有 Mesh。", "确定");
            return;
        }

        MapMeshBakeUtility.EnsureMeshReadable(src);
        if (!src.isReadable)
        {
            EditorUtility.DisplayDialog("轮廓贴图烘焙",
                "网格不可读。已在模型导入设置中尝试开启 Read/Write，请等待重新导入完成后重试。", "确定");
            return;
        }

        var uvs = src.uv;
        if (uvs == null || uvs.Length != src.vertexCount)
        {
            EditorUtility.DisplayDialog("轮廓贴图烘焙", "Mesh 缺少与顶点数一致的 UV0，无法烘焙 UV 距离场。", "确定");
            return;
        }

        if (!EditorUtility.DisplayDialog("轮廓贴图烘焙",
                "将：\n" +
                "1）复制可写 Mesh 并保存为 .asset；\n" +
                "2）根据顶面外轮廓在 UV0 上生成距离场 PNG；\n" +
                "3）把材质设为「轮廓模式 = 2」并绑定贴图与 UV 缩放。\n\n" +
                "（不再依赖顶点色模式 5，可避免插值条纹。）\n\n继续？",
                "继续", "取消"))
            return;

        string meshSavePath = EditorUtility.SaveFilePanelInProject(
            "保存烘焙后的网格",
            src.name + "_Baked",
            "asset",
            "选择保存 Mesh 资源的工程路径");
        if (string.IsNullOrEmpty(meshSavePath))
            return;

        Mesh work = Object.Instantiate(src);
        work.name = Path.GetFileNameWithoutExtension(meshSavePath);
        bool meshAssetCreated = false;

        Undo.RecordObject(mf, "Contour SDF mesh");
        Undo.RecordObject(mr, "Contour SDF material");

        try
        {
            if (!TryBuildUvBoundarySegments(work, mf.transform, TopFaceDot, Vector3.up, uvs,
                    out var uvSegs, out float uMin, out float uMax, out float vMin, out float vMax))
            {
                Object.DestroyImmediate(work);
                EditorUtility.DisplayDialog("轮廓贴图烘焙", "无法构建顶面 UV 轮廓边（参见控制台）。", "确定");
                return;
            }

            float du = uMax - uMin;
            float dv = vMax - vMin;
            float pad = Mathf.Max(du, dv) * 0.02f;
            uMin -= pad;
            uMax += pad;
            vMin -= pad;
            vMax += pad;
            du = Mathf.Max(uMax - uMin, 1e-4f);
            dv = Mathf.Max(vMax - vMin, 1e-4f);

            int resU = 1024;
            int resV = Mathf.Clamp(Mathf.RoundToInt(resU * dv / du), 128, 2048);

            var tex = new Texture2D(resU, resV, TextureFormat.RGB24, false, true);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            float maxD = 1e-6f;
            var raw = new float[resU, resV];
            for (int iy = 0; iy < resV; iy++)
            {
                float v = vMin + (iy + 0.5f) / resV * dv;
                for (int ix = 0; ix < resU; ix++)
                {
                    float u = uMin + (ix + 0.5f) / resU * du;
                    var p = new Vector2(u, v);
                    float d = float.MaxValue;
                    foreach (var s in uvSegs)
                    {
                        float dSeg = DistancePointToSegment2D(p, s.a, s.b);
                        d = Mathf.Min(d, dSeg);
                    }

                    raw[ix, iy] = d;
                    if (d > maxD)
                        maxD = d;
                }
            }

            for (int iy = 0; iy < resV; iy++)
            {
                for (int ix = 0; ix < resU; ix++)
                {
                    float t = Mathf.Clamp01(raw[ix, iy] / maxD);
                    tex.SetPixel(ix, iy, new Color(t, t, t, 1f));
                }
            }

            // makeNoLongerReadable 必须为 false，否则 EncodeToPNG 会报 Texture is not readable
            tex.Apply(false, false);

            string dir = (Path.GetDirectoryName(meshSavePath) ?? "Assets").Replace('\\', '/');
            string baseName = Path.GetFileNameWithoutExtension(meshSavePath);
            string texRelative = $"{dir}/{baseName}_ContourSDF.png";
            if (!texRelative.StartsWith("Assets/", System.StringComparison.Ordinal))
                texRelative = "Assets/" + texRelative.TrimStart('/');
            string relUnderAssets = texRelative.Substring("Assets/".Length);
            string texFull = Path.Combine(Application.dataPath, relUnderAssets.Replace('/', Path.DirectorySeparatorChar));

            byte[] png = tex.EncodeToPNG();
            Directory.CreateDirectory(Path.GetDirectoryName(texFull)!);
            File.WriteAllBytes(texFull, png);
            Object.DestroyImmediate(tex);

            AssetDatabase.CreateAsset(work, meshSavePath);
            meshAssetCreated = true;
            mf.sharedMesh = work;

            AssetDatabase.Refresh();
            ConfigureTextureImporter(texRelative);

            var loadedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texRelative);
            if (loadedTex == null)
            {
                AssetDatabase.DeleteAsset(texRelative);
                AssetDatabase.DeleteAsset(meshSavePath);
                meshAssetCreated = false;
                mf.sharedMesh = src;
                throw new System.InvalidOperationException("贴图导入失败，已回滚已写入的资源。");
            }

            Vector4 st = new Vector4(1f / du, 1f / dv, -uMin / du, -vMin / dv);
            var mat = mr.sharedMaterial;
            if (mat != null)
            {
                mat.SetTexture("_ContourTex", loadedTex);
                mat.SetVector("_ContourTex_ST", st);
                mat.SetFloat("_ContourMode", 2f);
                mat.SetFloat("_ContourTexInvert", 0f);
                mat.SetFloat("_ContourSdfAmp", 1f);
                mat.SetFloat("_LocalContourScale", 1f);
                mat.SetFloat("_UVRectAssist", 0f);
                EditorUtility.SetDirty(mat);
            }

            var cols = new Color[work.vertexCount];
            for (int i = 0; i < cols.Length; i++)
                cols[i] = Color.white;
            work.colors = cols;

            EditorUtility.SetDirty(work);
            Debug.Log($"[MapContourSdfMeshSave] 完成。\n网格: {meshSavePath}\n贴图: {texRelative}\n材质已设为轮廓模式 2，可按需调「轮廓贴图距离倍率」与渐变宽度。");
        }
        catch (System.Exception e)
        {
            if (!meshAssetCreated && work != null)
                Object.DestroyImmediate(work);
            EditorUtility.DisplayDialog("烘焙失败", e.Message, "确定");
        }
    }

    static void ConfigureTextureImporter(string assetPath)
    {
        var ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (ti == null)
            return;
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = false;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Bilinear;
        ti.mipmapEnabled = false;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.maxTextureSize = 4096;
        ti.isReadable = false;
        ti.SaveAndReimport();
    }

    static bool TryBuildUvBoundarySegments(Mesh mesh, Transform transform, float topFaceDotMin, Vector3 worldUp, Vector2[] uv,
        out List<(Vector2 a, Vector2 b)> uvSegs, out float uMin, out float uMax, out float vMin, out float vMax)
    {
        uvSegs = new List<(Vector2, Vector2)>();
        uMin = uMax = vMin = vMax = 0f;

        var verts = mesh.vertices;
        var tris = mesh.triangles;
        if (verts == null || tris == null || tris.Length < 3)
            return false;

        Vector3 objUp = Quaternion.Inverse(transform.rotation) * worldUp.normalized;

        float maxH = float.MinValue, minH = float.MaxValue;
        for (int i = 0; i < verts.Length; i++)
        {
            float h = Vector3.Dot(verts[i], objUp);
            if (h > maxH) maxH = h;
            if (h < minH) minH = h;
        }

        float height = maxH - minH;
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
        {
            Debug.LogError("[MapContourSdfMeshSave] 未找到顶面三角。");
            return false;
        }

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

        bool anyBoundary = false;
        foreach (var kv in edgeCount)
        {
            if (kv.Value != 1)
                continue;
            anyBoundary = true;
            int ia = kv.Key.Item1, ib = kv.Key.Item2;
            uvSegs.Add((uv[ia], uv[ib]));
        }

        if (!anyBoundary)
        {
            Debug.LogError("[MapContourSdfMeshSave] 未找到顶面轮廓边。");
            return false;
        }

        bool first = true;
        foreach (int vi in topVerts)
        {
            Vector2 t = uv[vi];
            if (first)
            {
                uMin = uMax = t.x;
                vMin = vMax = t.y;
                first = false;
            }
            else
            {
                if (t.x < uMin) uMin = t.x;
                if (t.x > uMax) uMax = t.x;
                if (t.y < vMin) vMin = t.y;
                if (t.y > vMax) vMax = t.y;
            }
        }

        return true;
    }

    static float DistancePointToSegment2D(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float sql = ab.sqrMagnitude;
        if (sql < 1e-12f)
            return Vector2.Distance(p, a);
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / sql);
        Vector2 c = a + t * ab;
        return Vector2.Distance(p, c);
    }
}
#endif
