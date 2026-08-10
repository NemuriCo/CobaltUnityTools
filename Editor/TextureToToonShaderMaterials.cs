#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SleepyCobalt.Tools
{
/// <summary>
/// Project窗口中右键贴图：从贴图创建ToonShader材质。
/// 生成位置：贴图同目录。
/// 命名规则：M_贴图名.mat。
/// Shader：UnityToon.shader（Shader "Toon"）。
/// 仅内置 M_DefaultToonShader.mat 相对 Toon shader 默认值调整过的参数，不依赖外部材质球。
/// </summary>
public static class TextureToToonShaderMaterials
{
    private const string MenuPath = "Assets/从贴图创建ToonShader材质";
    private const string ToonShaderName = "Toon";
    private const int DefaultRenderQueue = 2000;

    private static readonly string[] ColorTextureProperties =
    {
        "_BaseMap",
        "_MainTex",
        "_1st_ShadeMap",
        "_2nd_ShadeMap"
    };

    private static readonly Dictionary<string, string[]> TextureRoleProperties = new Dictionary<string, string[]>
    {
        { "color", ColorTextureProperties },
        { "base", ColorTextureProperties },
        { "basecolor", ColorTextureProperties },
        { "basecolormap", ColorTextureProperties },
        { "albedo", ColorTextureProperties },
        { "diffuse", ColorTextureProperties },
        { "normal", new[] { "_NormalMap" } },
        { "normalmap", new[] { "_NormalMap" } },
        { "bump", new[] { "_NormalMap" } },
        { "emission", new[] { "_Emissive_Tex", "_EmissionMap", "_EmissiveColorMap" } },
        { "emissionmap", new[] { "_Emissive_Tex", "_EmissionMap", "_EmissiveColorMap" } },
        { "emissive", new[] { "_Emissive_Tex", "_EmissionMap", "_EmissiveColorMap" } },
        { "emissivetex", new[] { "_Emissive_Tex" } },
        { "emissivecolor", new[] { "_EmissiveColorMap", "_Emissive_Tex" } },
        { "emissivecolormap", new[] { "_EmissiveColorMap", "_Emissive_Tex" } },
        { "shade", new[] { "_1st_ShadeMap", "_2nd_ShadeMap" } },
        { "shade1", new[] { "_1st_ShadeMap" } },
        { "shade2", new[] { "_2nd_ShadeMap" } },
        { "1stshade", new[] { "_1st_ShadeMap" } },
        { "1stshademap", new[] { "_1st_ShadeMap" } },
        { "2ndshade", new[] { "_2nd_ShadeMap" } },
        { "2ndshademap", new[] { "_2nd_ShadeMap" } },
        { "highcolor", new[] { "_HighColor_Tex" } },
        { "highcolortex", new[] { "_HighColor_Tex" } },
        { "matcap", new[] { "_MatCap_Sampler" } },
        { "mask", new[] { "_MaskMap", "_ClippingMask" } },
        { "maskmap", new[] { "_MaskMap" } },
        { "clippingmask", new[] { "_ClippingMask" } },
        { "outline", new[] { "_Outline_Sampler", "_OutlineTex" } },
        { "outlinesampler", new[] { "_Outline_Sampler" } },
        { "outlinetex", new[] { "_OutlineTex" } }
    };

    private static readonly string[] EnabledKeywords =
    {
        "_EMISSIVE_SIMPLE",
        "_IS_ANGELRING_OFF",
        "_IS_CLIPPING_TRANSMODE",
        "_IS_OUTLINE_CLIPPING_YES",
        "_OUTLINE_NML"
    };

    private static readonly string[] DisabledKeywords =
    {
        "_DISABLE_OUTLINE"
    };

    private static readonly string[] DisabledPasses =
    {
        "ALWAYS",
        "SRPDEFAULTUNLIT"
    };

    private static readonly FloatProperty[] ChangedFloatProperties =
    {
        new FloatProperty("_1st2nd_Shades_Feather", 0.218f),
        new FloatProperty("_1st_Brightness", 0.56f),
        new FloatProperty("_1st_Saturation", 0.98f),
        new FloatProperty("_1st_ShadeColor_Feather", 0.397f),
        new FloatProperty("_1st_ShadeColor_Step", 0.761f),
        new FloatProperty("_2nd_Brightness", 0.68f),
        new FloatProperty("_2nd_Saturation", 1.06f),
        new FloatProperty("_2nd_ShadeColor_Feather", 0.218f),
        new FloatProperty("_2nd_ShadeColor_Step", 0.887f),
        new FloatProperty("_AutoRenderQueue", 0f),
        new FloatProperty("_BaseColor_Step", 0.761f),
        new FloatProperty("_BaseShade_Feather", 0.397f),
        new FloatProperty("_ClippingMode", 2f),
        new FloatProperty("_HighColor_Power", 0.592f),
        new FloatProperty("_Is_BlendAddToHiColor", 1f),
        new FloatProperty("_Is_SpecularToHighColor", 1f),
        new FloatProperty("_LightDirection_MaskOn", 1f),
        new FloatProperty("_Outline_Width", 15f),
        new FloatProperty("_RimLight", 1f),
        new FloatProperty("_RimLight_InsideMask", 0.132f),
        new FloatProperty("_RimLight_Power", 1f),
        new FloatProperty("_ShadeColor_Step", 0.887f),
        new FloatProperty("_StencilComp", 0f),
        new FloatProperty("_Tweak_SystemShadowsLevel", -0.144f)
    };

    private static readonly ColorProperty[] ChangedColorProperties =
    {
        new ColorProperty("_HighColor", 0.4433962f, 0.4433962f, 0.4433962f, 1f),
        new ColorProperty("_Outline_Color", 0.5943396f, 0.25914755f, 0f, 1f),
        new ColorProperty("_RimLightColor", 0.5754717f, 0.47525454f, 0.2795924f, 1f)
    };

    [MenuItem(MenuPath, priority = 2100)]
    private static void CreateToonMaterialsFromSelectedTextures()
    {
        Object[] selections = Selection.objects;
        if (selections == null || selections.Length == 0)
        {
            EditorUtility.DisplayDialog("创建 ToonShader 材质", "请先在 Project 窗口中选择一个或多个贴图文件。", "确定");
            return;
        }

        Shader toonShader = FindToonShader();
        if (toonShader == null)
        {
            EditorUtility.DisplayDialog(
                "创建 ToonShader 材质失败",
                "找不到 UnityToon.shader 对应的 Shader。\n\n请确认 UnityToon.shader 已导入工程，并且 shader 内名称为：Shader \"Toon\"。",
                "确定");
            return;
        }

        Dictionary<string, MaterialTextureGroup> textureGroups = CreateTextureGroups(selections);
        if (textureGroups.Count == 0)
        {
            EditorUtility.DisplayDialog("创建 ToonShader 材质", "请选择贴图文件。", "确定");
            return;
        }

        int createdCount = 0;
        int skippedCount = 0;

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (MaterialTextureGroup textureGroup in textureGroups.Values)
            {
                string materialPath = $"{textureGroup.FolderPath}/M_{textureGroup.MaterialName}.mat";

                Material existingMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (existingMaterial != null)
                {
                    skippedCount++;
                    continue;
                }

                Material material = new Material(toonShader)
                {
                    name = $"M_{textureGroup.MaterialName}"
                };

                ApplyDefaultToonSettings(material);
                ApplyTextureGroup(material, textureGroup);
                AssetDatabase.CreateAsset(material, materialPath);
                Undo.RegisterCreatedObjectUndo(material, "Create ToonShader Material");
                createdCount++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[TextureToToonShaderMaterials] Created: {createdCount}, Skipped(existing): {skippedCount}.");
    }

    [MenuItem(MenuPath, validate = true)]
    private static bool ValidateCreateToonMaterialsFromSelectedTextures()
    {
        foreach (Object obj in Selection.objects)
        {
            if (obj is Texture)
                return true;
        }

        return false;
    }

    private static Shader FindToonShader()
    {
        Shader shader = Shader.Find(ToonShaderName);
        if (shader != null)
            return shader;

        string[] shaderGuids = AssetDatabase.FindAssets("UnityToon t:Shader");
        foreach (string guid in shaderGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Shader assetShader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (assetShader != null)
                return assetShader;
        }

        return null;
    }

    private static Dictionary<string, MaterialTextureGroup> CreateTextureGroups(Object[] selections)
    {
        Dictionary<string, MaterialTextureGroup> textureGroups = new Dictionary<string, MaterialTextureGroup>();

        foreach (Object obj in selections)
        {
            Texture texture = obj as Texture;
            if (texture == null)
                continue;

            string texturePath = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(texturePath))
                continue;

            string folderPath = Path.GetDirectoryName(texturePath)?.Replace("\\", "/");
            if (string.IsNullOrEmpty(folderPath))
                continue;

            string textureName = Path.GetFileNameWithoutExtension(texturePath);
            TextureNameInfo textureNameInfo = ParseTextureName(textureName);
            string groupKey = $"{folderPath}/{textureNameInfo.MaterialName}";

            if (!textureGroups.TryGetValue(groupKey, out MaterialTextureGroup textureGroup))
            {
                textureGroup = new MaterialTextureGroup(folderPath, textureNameInfo.MaterialName);
                textureGroups.Add(groupKey, textureGroup);
            }

            textureGroup.AddTexture(textureNameInfo.Role, texture);
        }

        return textureGroups;
    }

    private static TextureNameInfo ParseTextureName(string textureName)
    {
        string materialName = textureName.StartsWith("T_") ? textureName.Substring(2) : textureName;
        string role = "color";
        int separatorIndex = -1;

        for (int i = 0; i < materialName.Length; i++)
        {
            if (materialName[i] != '_' || i >= materialName.Length - 1)
                continue;

            string suffix = NormalizeTextureRole(materialName.Substring(i + 1));
            if (TextureRoleProperties.ContainsKey(suffix))
            {
                role = suffix;
                separatorIndex = i;
                break;
            }
        }

        if (separatorIndex >= 0)
            materialName = materialName.Substring(0, separatorIndex);

        return new TextureNameInfo(materialName, role);
    }

    private static string NormalizeTextureRole(string role)
    {
        return role.Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace("_", string.Empty)
            .ToLowerInvariant();
    }

    private static void ApplyDefaultToonSettings(Material material)
    {
        material.enableInstancing = true;
        material.doubleSidedGI = false;
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        material.renderQueue = DefaultRenderQueue;
        material.SetOverrideTag("IgnoreProjection", "False");
        material.SetOverrideTag("RenderType", "TransparentCutout");

        foreach (string keyword in EnabledKeywords)
        {
            material.EnableKeyword(keyword);
        }

        foreach (string keyword in DisabledKeywords)
        {
            material.DisableKeyword(keyword);
        }

        foreach (string passName in DisabledPasses)
        {
            material.SetShaderPassEnabled(passName, false);
        }

        foreach (FloatProperty property in ChangedFloatProperties)
        {
            if (material.HasProperty(property.Name))
                material.SetFloat(property.Name, property.Value);
        }

        foreach (ColorProperty property in ChangedColorProperties)
        {
            if (material.HasProperty(property.Name))
                material.SetColor(property.Name, property.Value);
        }
    }

    private static void ApplyTextureGroup(Material material, MaterialTextureGroup textureGroup)
    {
        foreach (KeyValuePair<string, Texture> textureSlot in textureGroup.Textures)
        {
            SetTextureForRole(material, textureSlot.Key, textureSlot.Value);
        }
    }

    private static void SetTextureForRole(Material material, string role, Texture texture)
    {
        if (!TextureRoleProperties.TryGetValue(role, out string[] propertyNames))
            propertyNames = ColorTextureProperties;

        foreach (string propertyName in propertyNames)
        {
            if (!material.HasProperty(propertyName))
                continue;

            material.SetTexture(propertyName, texture);
            material.SetTextureScale(propertyName, Vector2.one);
            material.SetTextureOffset(propertyName, Vector2.zero);
        }
    }

    private readonly struct FloatProperty
    {
        public readonly string Name;
        public readonly float Value;

        public FloatProperty(string name, float value)
        {
            Name = name;
            Value = value;
        }
    }

    private readonly struct ColorProperty
    {
        public readonly string Name;
        public readonly Color Value;

        public ColorProperty(string name, float r, float g, float b, float a)
        {
            Name = name;
            Value = new Color(r, g, b, a);
        }
    }

    private readonly struct TextureNameInfo
    {
        public readonly string MaterialName;
        public readonly string Role;

        public TextureNameInfo(string materialName, string role)
        {
            MaterialName = materialName;
            Role = role;
        }
    }

    private sealed class MaterialTextureGroup
    {
        public readonly string FolderPath;
        public readonly string MaterialName;
        public readonly Dictionary<string, Texture> Textures = new Dictionary<string, Texture>();

        public MaterialTextureGroup(string folderPath, string materialName)
        {
            FolderPath = folderPath;
            MaterialName = materialName;
        }

        public void AddTexture(string role, Texture texture)
        {
            Textures[role] = texture;
        }
    }
}
}
#endif
