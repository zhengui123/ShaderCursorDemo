#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 为 sd_map 等板块批量创建 MapLine 材质（存于 Materials/MapLine）并赋给 MeshRenderer。
/// </summary>
public static class MapLineMaterialSetup
{
    const string MapLineFolder = "Assets/Materials/MapLine";
    const string TemplatePath = "Assets/Shaders/Custom_MapShader.mat";

    [MenuItem("Tools/地图/批量创建 MapLine 材质并赋值", priority = 13)]
    static void BatchCreateAndAssign()
    {
        var root = ResolveMapRoot();
        if (root == null)
        {
            EditorUtility.DisplayDialog("MapLine 材质", "请选中 sd_map、polySurface1，或其任意子物体。", "确定");
            return;
        }

        var renderers = new List<MeshRenderer>();
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null)
                continue;
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr != null)
                renderers.Add(mr);
        }

        if (renderers.Count == 0)
        {
            EditorUtility.DisplayDialog("MapLine 材质", "未找到带 Mesh 的 MeshRenderer。", "确定");
            return;
        }

        bool overwrite = EditorUtility.DisplayDialog("MapLine 材质",
            $"将在「{MapLineFolder}」下为 {renderers.Count} 个物体各创建/更新材质并赋到 Renderer。\n\n" +
            "若已存在同名 .mat，是否覆盖？（取消 = 跳过已存在，仅新建）",
            "覆盖已有", "仅新建");

        EnsureFolderExists();

        var template = AssetDatabase.LoadAssetAtPath<Material>(TemplatePath);
        var shader = Shader.Find("Custom/SOC_HologramMap");
        if (shader == null)
            shader = Shader.Find("Custom/MapShader");

        if (template == null && shader == null)
        {
            EditorUtility.DisplayDialog("MapLine 材质", "找不到 Custom_MapShader 模板或 Hologram/Map Shader。", "确定");
            return;
        }

        int created = 0, updated = 0, assigned = 0, skipped = 0;

        foreach (var mr in renderers)
        {
            string matName = "MapLine_" + SanitizeFileName(mr.gameObject.name);
            string assetPath = $"{MapLineFolder}/{matName}.mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (existing != null && !overwrite)
            {
                AssignMaterial(mr, existing, mr.GetComponent<MeshFilter>()?.sharedMesh);
                skipped++;
                assigned++;
                continue;
            }

            Material mat;
            if (existing != null)
            {
                mat = existing;
                updated++;
            }
            else
            {
                mat = template != null ? new Material(template) : new Material(shader);
                mat.name = matName;
                AssetDatabase.CreateAsset(mat, assetPath);
                created++;
            }

            ApplyMapLineDefaults(mat, mr.GetComponent<MeshFilter>()?.sharedMesh);
            EditorUtility.SetDirty(mat);

            Undo.RecordObject(mr, "Assign MapLine material");
            AssignMaterial(mr, mat, mr.GetComponent<MeshFilter>()?.sharedMesh);
            assigned++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("MapLine 材质",
            $"完成。\n新建: {created}\n覆盖更新: {updated}\n跳过(仅赋值): {skipped}\n已赋值 Renderer: {assigned}\n目录: {MapLineFolder}",
            "确定");

        Debug.Log($"[MapLineMaterialSetup] 新建 {created}，更新 {updated}，赋值 {assigned}，目录 {MapLineFolder}");
    }

    static GameObject ResolveMapRoot()
    {
        if (Selection.activeGameObject != null)
            return Selection.activeGameObject;

        var found = GameObject.Find("sd_map/polySurface1");
        if (found != null)
            return found;
        return GameObject.Find("sd_map");
    }

    static void EnsureFolderExists()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(MapLineFolder))
            AssetDatabase.CreateFolder("Assets/Materials", "MapLine");
    }

    static void AssignMaterial(MeshRenderer mr, Material mat, Mesh mesh)
    {
        var slots = mr.sharedMaterials;
        if (slots == null || slots.Length == 0)
            slots = new Material[1];
        slots[0] = mat;
        mr.sharedMaterials = slots;
        EditorUtility.SetDirty(mr);

        if (PrefabUtility.IsPartOfPrefabInstance(mr.gameObject))
            PrefabUtility.RecordPrefabInstancePropertyModifications(mr);
    }

    /// <summary>
    /// 写入 MapLine 默认参数；若找到同目录下的 *_ContourSDF.png 则自动绑为模式 2。
    /// </summary>
    static void ApplyMapLineDefaults(Material mat, Mesh mesh)
    {
        if (mat == null || !mat.HasProperty("_ContourMode"))
            return;

        mat.SetFloat("_UVRectAssist", 0f);
        mat.SetFloat("_TopFaceCos", 0.92f);
        mat.SetFloat("_GeoEdgeBoost", 0.35f);

        bool hasSdf = TryBindContourSdf(mat, mesh);
        if (hasSdf)
        {
            mat.SetFloat("_ContourMode", 2f);
            mat.SetFloat("_ContourSdfAmp", 1f);
            mat.SetFloat("_LocalContourScale", 1f);
            mat.SetFloat("_ContourTexInvert", 0f);
        }
        else
        {
            mat.SetFloat("_ContourMode", 5f);
            mat.SetFloat("_VertexContourAmp", 0.5f);
            mat.SetFloat("_LocalContourScale", 0.68f);
            if (mat.HasProperty("_ContourTex"))
                mat.SetTexture("_ContourTex", null);
        }
    }

    static bool TryBindContourSdf(Material mat, Mesh mesh)
    {
        if (mesh == null || !mat.HasProperty("_ContourTex"))
            return false;

        string meshPath = AssetDatabase.GetAssetPath(mesh);
        if (string.IsNullOrEmpty(meshPath))
            return false;

        string dir = Path.GetDirectoryName(meshPath)?.Replace('\\', '/') ?? "";
        string baseName = Path.GetFileNameWithoutExtension(meshPath);
        string texPath = $"{dir}/{baseName}_ContourSDF.png";

        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        if (tex == null)
            return false;

        mat.SetTexture("_ContourTex", tex);
        return true;
    }

    static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "Unnamed";
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }
}
#endif
