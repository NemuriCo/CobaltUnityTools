
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace SleepyCobalt.Tools
{
public static class RendererFinderTool
{
    [MenuItem("CobaltTools/查找Mesh Renderer", false, 10)]
    private static void FindMeshRenderers()
    {
        SelectObjectsOfType<MeshRenderer>();
    }

    [MenuItem("CobaltTools/查找Skinned Mesh Renderer", false, 20)]
    private static void FindSkinnedMeshRenderers()
    {
        SelectObjectsOfType<SkinnedMeshRenderer>();
    }

    private static void SelectObjectsOfType<T>() where T : Renderer
    {
        List<GameObject> foundObjects = new List<GameObject>();
        T[] renderers = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None); // true = 包括未激活的对象

        foreach (T renderer in renderers)
        {
            if (renderer != null && renderer.gameObject != null)
            {
                foundObjects.Add(renderer.gameObject);
            }
        }

        if (foundObjects.Count > 0)
        {
            Selection.objects = foundObjects.ToArray();
            Debug.Log($"选中了 {foundObjects.Count} 个 {typeof(T).Name} 对应的 GameObject。");
        }
        else
        {
            Debug.LogWarning($"未找到任何 {typeof(T).Name}。");
        }
    }
}
}
