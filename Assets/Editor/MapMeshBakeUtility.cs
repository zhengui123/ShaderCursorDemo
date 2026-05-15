#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 地图轮廓烘焙共用：网格可读、顶面三角判定。
/// </summary>
public static class MapMeshBakeUtility
{
    /// <summary>
    /// FBX/Model 网格在访问 uv/vertices 前需开启 Read/Write。
    /// </summary>
    public static void EnsureMeshReadable(Mesh mesh)
    {
        if (mesh == null || mesh.isReadable)
            return;

        string path = AssetDatabase.GetAssetPath(mesh);
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogWarning(
                $"[MapBake] Mesh「{mesh.name}」无资源路径；若后续报错，请使用可读写网格副本或开启模型 Read/Write。");
            return;
        }

        var modelImporter = AssetImporter.GetAtPath(path) as ModelImporter;
        if (modelImporter == null)
        {
            Debug.LogWarning($"[MapBake] 无法为「{path}」设置 Read/Write（非 ModelImporter）。");
            return;
        }

        if (!modelImporter.isReadable)
        {
            modelImporter.isReadable = true;
            modelImporter.SaveAndReimport();
            Debug.Log($"[MapBake] 已为「{path}」开启 Read/Write 并重新导入。");
        }
    }

    public static bool IsTopTriangle(
        Vector3 o0, Vector3 o1, Vector3 o2,
        Vector3 faceNormal,
        Vector3 objUp,
        float maxH,
        float heightEps,
        float topFaceDotMin,
        bool hasVertNormals,
        Vector3 n0, Vector3 n1, Vector3 n2)
    {
        if (faceNormal.sqrMagnitude < 1e-20f)
            return false;

        faceNormal.Normalize();
        bool faceUp = Vector3.Dot(faceNormal, objUp) >= topFaceDotMin;

        float h0 = Vector3.Dot(o0, objUp);
        float h1 = Vector3.Dot(o1, objUp);
        float h2 = Vector3.Dot(o2, objUp);
        bool heightTop = h0 >= maxH - heightEps && h1 >= maxH - heightEps && h2 >= maxH - heightEps;
        // 顶缘竖直侧壁满足高度带但法线水平，排除以免整圈被当成顶面
        bool faceNotWall = Mathf.Abs(Vector3.Dot(faceNormal, objUp)) > 0.35f;

        bool vertUp = false;
        if (hasVertNormals)
        {
            float avgN = (Vector3.Dot(n0, objUp) + Vector3.Dot(n1, objUp) + Vector3.Dot(n2, objUp)) / 3f;
            vertUp = avgN >= topFaceDotMin * 0.85f;
        }

        return faceUp || vertUp || (heightTop && faceNotWall);
    }
}
#endif
