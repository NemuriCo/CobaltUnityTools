using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SleepyCobalt.Tools
{
public static class MaterialDuplicator
{
    private const string MenuDuplicateMaterials = "GameObject/「复制材质」";
    private const string MenuDuplicateMaterialsAndTextures = "GameObject/「复制材质和贴图」";

    private static bool isProcessing;

    [MenuItem(MenuDuplicateMaterials, false, 100)]
    private static void DuplicateMaterialsMenu()
    {
        StartDuplicate(false);
    }

    [MenuItem(MenuDuplicateMaterialsAndTextures, false, 101)]
    private static void DuplicateMaterialsAndTexturesMenu()
    {
        StartDuplicate(true);
    }

    [MenuItem(MenuDuplicateMaterials, true)]
    private static bool ValidateDuplicateMaterialsMenu()
    {
        return Selection.gameObjects.Length > 0;
    }

    [MenuItem(MenuDuplicateMaterialsAndTextures, true)]
    private static bool ValidateDuplicateMaterialsAndTexturesMenu()
    {
        return Selection.gameObjects.Length > 0;
    }

    private static void StartDuplicate(bool duplicateTextures)
    {
        if (isProcessing)
        {
            return;
        }

        if (Selection.gameObjects.Length == 0)
        {
            Debug.LogWarning("No objects selected in Hierarchy.");
            return;
        }

        isProcessing = true;
        EditorApplication.delayCall += () =>
        {
            try
            {
                ProcessSelectedObjects(duplicateTextures);
            }
            finally
            {
                isProcessing = false;
            }
        };
    }

    private static void ProcessSelectedObjects(bool duplicateTextures)
    {
        GameObject[] selectedObjects = Selection.gameObjects;
        Dictionary<string, Material> materialMap = new Dictionary<string, Material>();
        Dictionary<string, Texture> textureMap = new Dictionary<string, Texture>();
        int materialsDuplicated = 0;
        int texturesDuplicated = 0;
        bool useBatchAssetEditing = !duplicateTextures;

        if (useBatchAssetEditing)
        {
            AssetDatabase.StartAssetEditing();
        }
        try
        {
            foreach (GameObject selected in selectedObjects)
            {
                Renderer[] renderers = selected.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer renderer in renderers)
                {
                    Material[] sharedMaterials = renderer.sharedMaterials;
                    if (sharedMaterials == null || sharedMaterials.Length == 0)
                    {
                        continue;
                    }

                    bool changed = false;
                    Material[] newMaterials = new Material[sharedMaterials.Length];
                    for (int i = 0; i < sharedMaterials.Length; i++)
                    {
                        Material originalMaterial = sharedMaterials[i];
                        newMaterials[i] = originalMaterial;
                        if (originalMaterial == null)
                        {
                            continue;
                        }

                        string materialPath = AssetDatabase.GetAssetPath(originalMaterial);
                        if (string.IsNullOrEmpty(materialPath))
                        {
                            continue;
                        }

                        if (!materialMap.TryGetValue(materialPath, out Material duplicatedMaterial))
                        {
                            string newMaterialPath = GetUniqueMaterialCopyPath(originalMaterial, materialPath, "_Copy");
                            if (string.IsNullOrEmpty(newMaterialPath))
                            {
                                Debug.LogWarning("Skip non-project material: " + materialPath);
                                continue;
                            }

                            duplicatedMaterial = new Material(originalMaterial)
                            {
                                name = originalMaterial.name + "_Copy"
                            };
                            AssetDatabase.CreateAsset(duplicatedMaterial, newMaterialPath);
                            materialMap.Add(materialPath, duplicatedMaterial);
                            materialsDuplicated++;

                            if (duplicateTextures)
                            {
                                texturesDuplicated += DuplicateAndRebindTextures(duplicatedMaterial, textureMap);
                            }
                        }

                        if (newMaterials[i] != duplicatedMaterial)
                        {
                            newMaterials[i] = duplicatedMaterial;
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        Undo.RecordObject(renderer, "Duplicate Materials");
                        renderer.sharedMaterials = newMaterials;
                        EditorUtility.SetDirty(renderer);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    }
                }
            }
        }
        finally
        {
            if (useBatchAssetEditing)
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        if (duplicateTextures)
        {
            Debug.Log(string.Format("Duplicated {0} material(s) and {1} texture(s).", materialsDuplicated, texturesDuplicated));
        }
        else
        {
            Debug.Log(string.Format("Duplicated {0} material(s).", materialsDuplicated));
        }
    }

    private static int DuplicateAndRebindTextures(Material material, Dictionary<string, Texture> textureMap)
    {
        int duplicatedCount = 0;
        Shader shader = material.shader;
        if (shader == null)
        {
            return 0;
        }

        int propertyCount = ShaderUtil.GetPropertyCount(shader);
        for (int i = 0; i < propertyCount; i++)
        {
            if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
            {
                continue;
            }

            string propertyName = ShaderUtil.GetPropertyName(shader, i);
            Texture sourceTexture = material.GetTexture(propertyName);
            if (sourceTexture == null)
            {
                continue;
            }

            string texturePath = AssetDatabase.GetAssetPath(sourceTexture);
            if (string.IsNullOrEmpty(texturePath))
            {
                continue;
            }

            if (!textureMap.TryGetValue(texturePath, out Texture duplicatedTexture))
            {
                if (!IsProjectAssetPath(texturePath))
                {
                    Debug.LogWarning("Skip non-project texture: " + texturePath);
                    continue;
                }

                string newTexturePath = GetUniqueCopyPath(texturePath, "_Copy");
                if (string.IsNullOrEmpty(newTexturePath))
                {
                    Debug.LogWarning("Skip texture with unsupported path: " + texturePath);
                    continue;
                }

                if (!AssetDatabase.CopyAsset(texturePath, newTexturePath))
                {
                    Debug.LogWarning("Failed to duplicate texture: " + texturePath);
                    continue;
                }

                duplicatedTexture = AssetDatabase.LoadAssetAtPath<Texture>(newTexturePath);
                if (duplicatedTexture == null)
                {
                    Debug.LogWarning("Duplicated texture cannot be loaded: " + newTexturePath);
                    continue;
                }

                textureMap.Add(texturePath, duplicatedTexture);
                duplicatedCount++;
            }

            material.SetTexture(propertyName, duplicatedTexture);
        }

        EditorUtility.SetDirty(material);
        return duplicatedCount;
    }

    private static string GetUniqueMaterialCopyPath(Material originalMaterial, string originalPath, string copySuffix)
    {
        if (!IsProjectAssetPath(originalPath))
        {
            return null;
        }

        string directory = Path.GetDirectoryName(originalPath);
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        // If material is a sub-asset (for example, inside FBX), use material object name and force .mat extension.
        // If it's a standalone .mat, keep the source file name.
        bool isSubAsset = AssetDatabase.IsSubAsset(originalMaterial);
        string baseName = isSubAsset ? originalMaterial.name : Path.GetFileNameWithoutExtension(originalPath);
        string path = Path.Combine(directory, baseName + copySuffix + ".mat").Replace("\\", "/");
        return AssetDatabase.GenerateUniqueAssetPath(path);
    }

    private static bool IsProjectAssetPath(string path)
    {
        return !string.IsNullOrEmpty(path) && path.StartsWith("Assets/");
    }

    private static string GetUniqueCopyPath(string originalPath, string copySuffix)
    {
        if (!IsProjectAssetPath(originalPath))
        {
            return null;
        }

        string directory = Path.GetDirectoryName(originalPath);
        string extension = Path.GetExtension(originalPath);
        string baseName = Path.GetFileNameWithoutExtension(originalPath);

        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(extension))
        {
            return null;
        }

        string path = Path.Combine(directory, baseName + copySuffix + extension).Replace("\\", "/");
        return AssetDatabase.GenerateUniqueAssetPath(path);
    }
}
}
