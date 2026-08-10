#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SleepyCobalt.Tools
{
public static class MeshMergeTool
{
    private const string MenuRoot = "GameObject/「合并所有Mesh」";
    private const string MenuPath = MenuRoot;
    private const string MenuPathReloadFromDisk = MenuRoot + "/「从磁盘重绑Mesh」";
    private const string OutputRoot = "Assets/Art/Levels/MergedMeshes";
    private static readonly bool ExportFbxByDefault = false;

    [MenuItem(MenuPath, false, 103)]
    private static void MergeAllMesh(MenuCommand command)
    {
        GameObject sourceRoot = command.context as GameObject;
        if (sourceRoot == null)
        {
            sourceRoot = Selection.activeGameObject;
        }

        if (sourceRoot == null)
        {
            Debug.LogWarning("未找到目标物体，请在 Hierarchy 中右键一个物体后执行。");
            return;
        }

        try
        {
            MergeInternal(sourceRoot);
        }
        catch (Exception ex)
        {
            Debug.LogError($"合并失败: {ex}");
        }
    }

    public static bool MergeForRoot(GameObject sourceRoot)
    {
        if (sourceRoot == null)
        {
            return false;
        }

        try
        {
            MergeInternal(sourceRoot);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{sourceRoot.name}] 合并失败: {ex}");
            return false;
        }
    }

    public static bool MergeForRoot(GameObject sourceRoot, string exportBaseNameOverride)
    {
        if (sourceRoot == null)
        {
            return false;
        }

        try
        {
            MergeInternal(sourceRoot, exportBaseNameOverride);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{sourceRoot.name}] 合并失败: {ex}");
            return false;
        }
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateMergeAllMesh(MenuCommand command)
    {
        if (command.context is GameObject)
        {
            return true;
        }

        return Selection.activeGameObject != null;
    }

    [MenuItem(MenuPathReloadFromDisk, false, 105)]
    private static void ReloadMeshFromDisk(MenuCommand command)
    {
        GameObject go = command.context as GameObject;
        if (go == null)
        {
            go = Selection.activeGameObject;
        }

        if (go == null)
        {
            return;
        }

        int reboundCount = 0;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf == null || mf.sharedMesh == null)
            {
                continue;
            }

            string path = AssetDatabase.GetAssetPath(mf.sharedMesh);
            if (string.IsNullOrEmpty(path) || !path.StartsWith(OutputRoot))
            {
                continue;
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Mesh diskMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (diskMesh != null)
            {
                mf.sharedMesh = diskMesh;
                EditorUtility.SetDirty(mf);
                reboundCount++;
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"[MeshMerge] 从磁盘重绑完成: {reboundCount} 个 MeshFilter");
    }

    [MenuItem(MenuPathReloadFromDisk, true)]
    private static bool ValidateReloadMeshFromDisk(MenuCommand command)
    {
        GameObject go = command.context as GameObject;
        if (go == null)
        {
            go = Selection.activeGameObject;
        }

        return go != null;
    }

    private static void MergeInternal(GameObject sourceRoot)
    {
        MergeInternal(sourceRoot, null);
    }

    private static void MergeInternal(GameObject sourceRoot, string exportBaseNameOverride)
    {
        var materialSlots = new List<Material>();
        var combineBySlot = new List<List<CombineInstance>>();
        var temporaryMeshes = new List<Mesh>();
        GameObject tempRoot = null;

        try
        {
            tempRoot = UnityEngine.Object.Instantiate(sourceRoot);
            tempRoot.name = sourceRoot.name + "_MergeTemp";
            tempRoot.hideFlags = HideFlags.HideAndDontSave;

            BakeSkinnedToMeshRenderers(tempRoot, temporaryMeshes, false, materialSlots, combineBySlot);

            var tempMeshRenderers = tempRoot.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var meshRenderer in tempMeshRenderers)
            {
                if (meshRenderer == null || !meshRenderer.enabled || !meshRenderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var meshFilter = meshRenderer.GetComponent<MeshFilter>();
                if (meshFilter == null || meshFilter.sharedMesh == null)
                {
                    continue;
                }

                AppendRenderer(
                    meshFilter.sharedMesh,
                    meshRenderer.sharedMaterials,
                    tempRoot.transform.worldToLocalMatrix * meshFilter.transform.localToWorldMatrix,
                    materialSlots,
                    combineBySlot,
                    temporaryMeshes);
            }
        }
        finally
        {
            if (tempRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(tempRoot);
            }
        }

        if (materialSlots.Count == 0)
        {
            foreach (var tempMesh in temporaryMeshes)
            {
                if (tempMesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(tempMesh);
                }
            }

            Debug.LogWarning($"[{sourceRoot.name}] 没有可合并的 MeshRenderer 或 SkinnedMeshRenderer。");
            return;
        }

        string exportBaseName = string.IsNullOrWhiteSpace(exportBaseNameOverride)
            ? GetExportBaseName(sourceRoot)
            : SanitizeFileName(exportBaseNameOverride);
        Mesh mergedMesh = BuildMergedMesh(sourceRoot.name, combineBySlot);
        OptimizeMergedMesh(mergedMesh);
        string meshAssetPath = SaveMergedMeshAsset(mergedMesh, exportBaseName);

        string mergedObjectName = sourceRoot.name + "_Merged";
        RemoveExistingMergedSibling(sourceRoot, mergedObjectName);

        var mergedGo = new GameObject(mergedObjectName);
        Undo.RegisterCreatedObjectUndo(mergedGo, "合并所有Mesh");

        Transform mergedTransform = mergedGo.transform;
        mergedTransform.SetParent(sourceRoot.transform.parent, false);
        mergedTransform.position = sourceRoot.transform.position;
        mergedTransform.rotation = sourceRoot.transform.rotation;
        mergedTransform.localScale = sourceRoot.transform.localScale;
        mergedTransform.SetSiblingIndex(sourceRoot.transform.GetSiblingIndex() + 1);

        mergedGo.layer = sourceRoot.layer;
        mergedGo.tag = sourceRoot.tag;

        var meshFilterOut = mergedGo.AddComponent<MeshFilter>();
        var meshRendererOut = mergedGo.AddComponent<MeshRenderer>();

        Mesh savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshAssetPath);
        if (savedMesh == null)
        {
            Debug.LogError($"[{sourceRoot.name}] 无法从磁盘加载合并网格: {meshAssetPath}");
            meshFilterOut.sharedMesh = mergedMesh;
        }
        else
        {
            meshFilterOut.sharedMesh = savedMesh;
        }
        meshRendererOut.sharedMaterials = materialSlots.ToArray();

        if (sourceRoot.name == "终点" && mergedGo.GetComponent<TransparencyControllerByMaterial>() == null)
        {
            mergedGo.AddComponent<TransparencyControllerByMaterial>();
        }

        if (ExportFbxByDefault)
        {
            TryExportFbxWithPlugin(mergedGo, exportBaseName);
        }

        Undo.RecordObject(sourceRoot, "隐藏原始物体");
        sourceRoot.SetActive(false);

        foreach (var tempMesh in temporaryMeshes)
        {
            if (tempMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(tempMesh);
            }
        }

        Selection.activeGameObject = mergedGo;
        Debug.Log($"[{sourceRoot.name}] 合并完成。Mesh: {meshAssetPath}");
    }

    private static void AppendRenderer(
        Mesh mesh,
        Material[] sharedMaterials,
        Matrix4x4 matrix,
        List<Material> materialSlots,
        List<List<CombineInstance>> combineBySlot,
        List<Mesh> temporaryMeshes)
    {
        if (mesh == null)
        {
            return;
        }

        bool mirrored = matrix.determinant < 0f;
        Mesh transformedMesh = CreateTransformedMesh(mesh, matrix, mirrored);
        temporaryMeshes.Add(transformedMesh);

        int subMeshCount = transformedMesh.subMeshCount;
        for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
        {
            Material mat = null;
            if (sharedMaterials != null && subMeshIndex < sharedMaterials.Length)
            {
                mat = sharedMaterials[subMeshIndex];
            }

            int slotIndex = FindOrCreateSlot(mat, materialSlots, combineBySlot);
            combineBySlot[slotIndex].Add(new CombineInstance
            {
                mesh = transformedMesh,
                subMeshIndex = subMeshIndex,
                transform = Matrix4x4.identity
            });
        }
    }

    private static int FindOrCreateSlot(
        Material material,
        List<Material> materialSlots,
        List<List<CombineInstance>> combineBySlot)
    {
        for (int i = 0; i < materialSlots.Count; i++)
        {
            if (materialSlots[i] == material)
            {
                return i;
            }
        }

        materialSlots.Add(material);
        combineBySlot.Add(new List<CombineInstance>());
        return materialSlots.Count - 1;
    }

    private static Mesh BuildMergedMesh(string sourceName, List<List<CombineInstance>> combineBySlot)
    {
        var perSlotMeshes = new List<Mesh>();
        var finalCombines = new List<CombineInstance>();
        int mergedEstimatedVertexCount = 0;

        for (int i = 0; i < combineBySlot.Count; i++)
        {
            var slotCombines = combineBySlot[i];
            if (slotCombines.Count == 0)
            {
                continue;
            }

            var slotMesh = new Mesh
            {
                name = sourceName + "_Slot" + i
            };
            int slotEstimatedVertexCount = EstimateVertexCount(slotCombines);
            slotMesh.indexFormat = slotEstimatedVertexCount <= 65535 ? IndexFormat.UInt16 : IndexFormat.UInt32;
            slotMesh.CombineMeshes(slotCombines.ToArray(), true, true, false);

            perSlotMeshes.Add(slotMesh);
            mergedEstimatedVertexCount += slotMesh.vertexCount;
            finalCombines.Add(new CombineInstance
            {
                mesh = slotMesh,
                subMeshIndex = 0,
                transform = Matrix4x4.identity
            });
        }

        var merged = new Mesh
        {
            name = sourceName + "_MergedMesh"
        };
        merged.indexFormat = mergedEstimatedVertexCount <= 65535 ? IndexFormat.UInt16 : IndexFormat.UInt32;
        merged.CombineMeshes(finalCombines.ToArray(), false, false, false);
        merged.RecalculateBounds();

        foreach (var slotMesh in perSlotMeshes)
        {
            if (slotMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(slotMesh);
            }
        }

        return merged;
    }

    private static int EstimateVertexCount(List<CombineInstance> combines)
    {
        if (combines == null)
        {
            return 0;
        }

        int count = 0;
        foreach (var ci in combines)
        {
            if (ci.mesh != null)
            {
                count += ci.mesh.vertexCount;
            }
        }
        return count;
    }

    private static void BakeSkinnedToMeshRenderers(
        GameObject tempRoot,
        List<Mesh> temporaryMeshes,
        bool logDetails,
        List<Material> materialSlots,
        List<List<CombineInstance>> combineBySlot)
    {
        var skinnedRenderers = tempRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var skinnedRenderer in skinnedRenderers)
        {
            if (skinnedRenderer == null || !skinnedRenderer.enabled || !skinnedRenderer.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (skinnedRenderer.sharedMesh == null)
            {
                continue;
            }

            var bakedMesh = new Mesh
            {
                name = skinnedRenderer.name + "_Baked"
            };
#if UNITY_2019_1_OR_NEWER
            skinnedRenderer.BakeMesh(bakedMesh, false);
#else
            skinnedRenderer.BakeMesh(bakedMesh);
#endif

            // Skinned 在负缩放链路下 Bake 后常出现法向/绕序反向，这里做一次显式纠正。
            if (IsMirroredTransform(skinnedRenderer.transform))
            {
                Vector3 sign = GetScaleSign(skinnedRenderer.transform.lossyScale);
                FixMirroredBakedMeshInPlace(bakedMesh, sign);
            }
            temporaryMeshes.Add(bakedMesh);

            // 关键：烘焙结果已包含缩放形变，最终只叠加位置+旋转，避免缩放重复作用。
            Matrix4x4 posRotOnlyInRoot = tempRoot.transform.worldToLocalMatrix *
                                         Matrix4x4.TRS(
                                             skinnedRenderer.transform.position,
                                             skinnedRenderer.transform.rotation,
                                             Vector3.one);

            AppendRenderer(
                bakedMesh,
                skinnedRenderer.sharedMaterials,
                posRotOnlyInRoot,
                materialSlots,
                combineBySlot,
                temporaryMeshes);

            if (logDetails)
            {
                string path = GetPath(skinnedRenderer.transform, tempRoot.transform);
                Debug.Log(
                    $"[BakeDebug] {path} -> {skinnedRenderer.gameObject.name}_BakedMesh | vtx={bakedMesh.vertexCount} sub={bakedMesh.subMeshCount} " +
                    $"lossyScale={skinnedRenderer.transform.lossyScale}");
            }

            skinnedRenderer.enabled = false;
        }
    }

    private static string GetPath(Transform current, Transform root)
    {
        if (current == null)
        {
            return string.Empty;
        }

        if (current == root)
        {
            return current.name;
        }

        var stack = new Stack<string>();
        Transform t = current;
        while (t != null)
        {
            stack.Push(t.name);
            if (t == root)
            {
                break;
            }
            t = t.parent;
        }

        return string.Join("/", stack.ToArray());
    }

    private static Mesh CreateTransformedMesh(Mesh sourceMesh, Matrix4x4 matrix, bool mirrored)
    {
        Mesh dst = UnityEngine.Object.Instantiate(sourceMesh);
        dst.name = sourceMesh.name + "_Transformed";

        Vector3[] vertices = dst.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
        }
        dst.vertices = vertices;

        var normalMatrix = matrix.inverse.transpose;

        Vector3[] normals = dst.normals;
        if (normals != null && normals.Length == vertices.Length)
        {
            for (int i = 0; i < normals.Length; i++)
            {
                normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
            }
            dst.normals = normals;
        }

        Vector4[] tangents = dst.tangents;
        if (tangents != null && tangents.Length == vertices.Length)
        {
            for (int i = 0; i < tangents.Length; i++)
            {
                Vector3 t = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                t = matrix.MultiplyVector(t).normalized;
                float w = tangents[i].w;
                if (mirrored)
                {
                    w = -w;
                }
                tangents[i] = new Vector4(t.x, t.y, t.z, w);
            }
            dst.tangents = tangents;
        }

        if (mirrored)
        {
            int subMeshCount = dst.subMeshCount;
            for (int s = 0; s < subMeshCount; s++)
            {
                int[] triangles = dst.GetTriangles(s);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    int tmp = triangles[i];
                    triangles[i] = triangles[i + 1];
                    triangles[i + 1] = tmp;
                }
                dst.SetTriangles(triangles, s, false);
            }
        }

        dst.RecalculateBounds();
        return dst;
    }

    private static bool IsMirroredTransform(Transform t)
    {
        if (t == null)
        {
            return false;
        }

        return t.localToWorldMatrix.determinant < 0f;
    }

    private static Vector3 GetScaleSign(Vector3 lossyScale)
    {
        float sx = lossyScale.x < 0f ? -1f : 1f;
        float sy = lossyScale.y < 0f ? -1f : 1f;
        float sz = lossyScale.z < 0f ? -1f : 1f;
        return new Vector3(sx, sy, sz);
    }

    private static void FixMirroredBakedMeshInPlace(Mesh mesh, Vector3 sign)
    {
        if (mesh == null)
        {
            return;
        }

        // 1) 反转三角绕序，修复背面裁剪方向
        int subMeshCount = mesh.subMeshCount;
        for (int s = 0; s < subMeshCount; s++)
        {
            int[] triangles = mesh.GetTriangles(s);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int tmp = triangles[i];
                triangles[i] = triangles[i + 1];
                triangles[i + 1] = tmp;
            }
            mesh.SetTriangles(triangles, s, false);
        }

        // 2) 按镜像轴符号修正法向（不能简单整体取反）
        Vector3[] normals = mesh.normals;
        if (normals != null && normals.Length > 0)
        {
            for (int i = 0; i < normals.Length; i++)
            {
                normals[i] = new Vector3(
                    normals[i].x * sign.x,
                    normals[i].y * sign.y,
                    normals[i].z * sign.z
                ).normalized;
            }
            mesh.normals = normals;
        }

        // 3) 切线方向按轴符号修正，手性翻转
        Vector4[] tangents = mesh.tangents;
        if (tangents != null && tangents.Length > 0)
        {
            for (int i = 0; i < tangents.Length; i++)
            {
                Vector3 t = new Vector3(
                    tangents[i].x * sign.x,
                    tangents[i].y * sign.y,
                    tangents[i].z * sign.z
                ).normalized;
                tangents[i].x = t.x;
                tangents[i].y = t.y;
                tangents[i].z = t.z;
                tangents[i].w = -tangents[i].w;
            }
            mesh.tangents = tangents;
        }

        mesh.RecalculateBounds();
    }

    private static string SaveMergedMeshAsset(Mesh mesh, string sourceName)
    {
        EnsureFolder(OutputRoot);

        string fileName = sourceName + "_Merged.asset";
        string targetPath = OutputRoot + "/" + fileName;
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(targetPath);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, targetPath);
            EditorUtility.SetDirty(mesh);
        }
        else
        {
            // 覆写现有资产内容，保留 GUID，避免引用漂移。
            // 先 Clear 可避免顶点/索引缓冲在某些版本下残留旧数据。
            existing.Clear(false);
            EditorUtility.CopySerialized(mesh, existing);
            existing.name = mesh.name;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(mesh);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();
        return targetPath;
    }

    private static void OptimizeMergedMesh(Mesh mesh)
    {
        if (mesh == null)
        {
            return;
        }

        mesh.OptimizeIndexBuffers();
        mesh.OptimizeReorderVertexBuffer();
        mesh.RecalculateBounds();
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string[] parts = folderPath.Split('/');
        if (parts.Length == 0)
        {
            return;
        }

        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }

    private static void TryExportFbxWithPlugin(GameObject mergedGo, string sourceName)
    {
        try
        {
            Type exporterType = Type.GetType("UnityEditor.Formats.Fbx.Exporter.ModelExporter, Unity.Formats.Fbx.Editor");
            if (exporterType == null)
            {
                return;
            }

            var exportMethod = exporterType.GetMethod("ExportObject", new[] { typeof(string), typeof(UnityEngine.Object) });
            if (exportMethod == null)
            {
                return;
            }

            string fbxPath = OutputRoot + "/" + sourceName + "_Merged.fbx";
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(fbxPath) != null)
            {
                AssetDatabase.DeleteAsset(fbxPath);
            }

            exportMethod.Invoke(null, new object[] { fbxPath, mergedGo });
            Debug.Log($"[{sourceName}] FBX 导出完成: {fbxPath}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[{sourceName}] FBX 导出失败（已忽略，不影响合并结果）: {ex.Message}");
        }
    }

    private static string GetExportBaseName(GameObject sourceRoot)
    {
        if (sourceRoot == null)
        {
            return "MergedMesh";
        }

        GameObject prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(sourceRoot);
        if (prefabRoot == null)
        {
            prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(sourceRoot);
        }

        string rawName;
        if (sourceRoot.name == "静态")
        {
            rawName = prefabRoot != null ? prefabRoot.name : sourceRoot.transform.root.name;
        }
        else if (sourceRoot.name == "终点")
        {
            string topName = prefabRoot != null ? prefabRoot.name : sourceRoot.transform.root.name;
            rawName = topName + "_终点";
        }
        else
        {
            rawName = BuildRelativePathName(prefabRoot != null ? prefabRoot.transform : sourceRoot.transform.root, sourceRoot.transform);
        }

        if (string.IsNullOrWhiteSpace(rawName))
        {
            rawName = sourceRoot.name;
        }

        return SanitizeFileName(rawName);
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "MergedMesh";
        }

        char[] invalidChars = Path.GetInvalidFileNameChars();
        foreach (char c in invalidChars)
        {
            value = value.Replace(c, '_');
        }

        return value.Trim();
    }

    private static string BuildRelativePathName(Transform root, Transform target)
    {
        if (root == null || target == null)
        {
            return "MergedMesh";
        }

        var names = new List<string>();
        Transform t = target;
        while (t != null)
        {
            names.Add(t.name);
            if (t == root)
            {
                break;
            }
            t = t.parent;
        }

        names.Reverse();
        return string.Join("_", names);
    }

    private static void RemoveExistingMergedSibling(GameObject sourceRoot, string mergedObjectName)
    {
        if (sourceRoot == null || string.IsNullOrEmpty(mergedObjectName))
        {
            return;
        }

        GameObject existing = null;
        Transform parent = sourceRoot.transform.parent;
        if (parent != null)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child == null || child.gameObject == sourceRoot)
                {
                    continue;
                }

                if (child.name == mergedObjectName)
                {
                    existing = child.gameObject;
                    break;
                }
            }
        }
        else
        {
            var roots = sourceRoot.scene.GetRootGameObjects();
            foreach (var root in roots)
            {
                if (root == null || root == sourceRoot)
                {
                    continue;
                }

                if (root.name == mergedObjectName)
                {
                    existing = root;
                    break;
                }
            }
        }

        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing);
        }
    }
}
}
#endif
