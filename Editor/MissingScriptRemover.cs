using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SleepyCobalt.Tools
{
    internal static class MissingScriptRemover
    {
        private const string MenuPath = "Assets/「删除选中物体Missing脚本」";
        private const string UndoName = "Remove Missing Scripts";

        [MenuItem(MenuPath, false, 2100)]
        private static void RemoveFromSelectedPrefabs()
        {
            List<string> prefabPaths = GetSelectedPrefabPaths();
            if (prefabPaths.Count == 0)
            {
                Debug.LogWarning("「删除选中物体Missing脚本」：当前选择中没有可处理的 Prefab/GameObject 资源。");
                return;
            }

            int changedAssetCount = 0;
            int removedScriptCount = 0;
            int skippedAssetCount = 0;
            int failedAssetCount = 0;

            foreach (string prefabPath in prefabPaths)
            {
                if (!AssetDatabase.IsOpenForEdit(prefabPath))
                {
                    skippedAssetCount++;
                    Debug.LogWarning("「删除选中物体Missing脚本」：资源不可编辑，已跳过：" + prefabPath);
                    continue;
                }

                GameObject prefabContentsRoot = null;
                try
                {
                    prefabContentsRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                    if (prefabContentsRoot == null)
                    {
                        failedAssetCount++;
                        Debug.LogError("「删除选中物体Missing脚本」：无法加载 Prefab：" + prefabPath);
                        continue;
                    }

                    int removedForAsset = RemoveMissingScriptsRecursively(prefabContentsRoot);
                    if (removedForAsset == 0)
                        continue;

                    GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(prefabContentsRoot, prefabPath);
                    if (savedPrefab == null)
                    {
                        failedAssetCount++;
                        Debug.LogError("「删除选中物体Missing脚本」：保存 Prefab 失败：" + prefabPath);
                        continue;
                    }

                    changedAssetCount++;
                    removedScriptCount += removedForAsset;
                }
                catch (Exception exception)
                {
                    failedAssetCount++;
                    Debug.LogError("「删除选中物体Missing脚本」：处理失败：" + prefabPath + "\n" + exception);
                }
                finally
                {
                    if (prefabContentsRoot != null)
                        PrefabUtility.UnloadPrefabContents(prefabContentsRoot);
                }
            }

            if (changedAssetCount > 0)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            Debug.Log(
                "「删除选中物体Missing脚本」完成：修改 " + changedAssetCount + " 个 Prefab，" +
                "删除 " + removedScriptCount + " 个 Missing Script，跳过 " + skippedAssetCount + " 个，" +
                "失败 " + failedAssetCount + " 个。");
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateRemoveFromSelectedPrefabs()
        {
            foreach (string prefabPath in GetSelectedPrefabPaths())
            {
                if (AssetDatabase.IsOpenForEdit(prefabPath))
                    return true;
            }

            return false;
        }

        private static List<string> GetSelectedPrefabPaths()
        {
            HashSet<string> uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (UnityEngine.Object selectedObject in Selection.objects)
            {
                if (selectedObject == null)
                    continue;

                string assetPath = AssetDatabase.GetAssetPath(selectedObject);
                if (string.IsNullOrEmpty(assetPath) ||
                    AssetDatabase.IsValidFolder(assetPath) ||
                    !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) != null)
                    uniquePaths.Add(assetPath);
            }

            return new List<string>(uniquePaths);
        }

        private static int RemoveMissingScriptsRecursively(GameObject root)
        {
            int removedScriptCount = 0;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            bool undoRegistered = false;

            foreach (Transform currentTransform in transforms)
            {
                GameObject currentObject = currentTransform.gameObject;
                if (PrefabUtility.IsPartOfImmutablePrefab(currentObject))
                    continue;

                int missingScriptCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(currentObject);
                if (missingScriptCount == 0)
                    continue;

                if (!undoRegistered)
                {
                    Undo.RegisterFullObjectHierarchyUndo(root, UndoName);
                    undoRegistered = true;
                }

                removedScriptCount += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(currentObject);
            }

            return removedScriptCount;
        }
    }
}
