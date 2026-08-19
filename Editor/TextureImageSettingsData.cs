#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SleepyCobalt.Tools.TextureTools
{
    [Serializable]
    internal sealed class TextureCommonSettingsSnapshot
    {
        public TextureImporterType textureType = TextureImporterType.Default;
        public TextureImporterShape textureShape = TextureImporterShape.Texture2D;
        public bool sRGBTexture = true;
        public TextureImporterAlphaSource alphaSource = TextureImporterAlphaSource.FromInput;
        public bool alphaIsTransparency;
        public TextureImporterNPOTScale npotScale = TextureImporterNPOTScale.None;
        public bool readable;
        public bool virtualTextureOnly;
        public bool ignorePngGamma;
        public TextureImporterSwizzle swizzleR = TextureImporterSwizzle.R;
        public TextureImporterSwizzle swizzleG = TextureImporterSwizzle.G;
        public TextureImporterSwizzle swizzleB = TextureImporterSwizzle.B;
        public TextureImporterSwizzle swizzleA = TextureImporterSwizzle.A;

        public SpriteImportMode spriteImportMode = SpriteImportMode.Single;
        public float spritePixelsPerUnit = 100f;
        public SpriteMeshType spriteMeshType = SpriteMeshType.Tight;
        public int spriteExtrude = 1;
        public int spriteAlignment = (int)SpriteAlignment.Center;
        public Vector2 spritePivot = new Vector2(0.5f, 0.5f);
        public bool spriteGenerateFallbackPhysicsShape = true;

        public bool convertToNormalMap;
        public float heightmapScale = 0.25f;
        public TextureImporterNormalFilter normalMapFilter = TextureImporterNormalFilter.Standard;
        public bool flipGreenChannel;
        public TextureImporterSingleChannelComponent singleChannelComponent = TextureImporterSingleChannelComponent.Alpha;

        public bool mipmapEnabled;
        public bool ignoreMipmapLimit;
        public string mipmapLimitGroupName = string.Empty;
        public TextureImporterMipFilter mipmapFilter = TextureImporterMipFilter.BoxFilter;
        public bool mipMapsPreserveCoverage;
        public float alphaTestReferenceValue = 0.5f;
        public bool borderMipmap;
        public bool fadeOut;
        public int mipmapFadeDistanceStart = 1;
        public int mipmapFadeDistanceEnd = 3;
        public bool streamingMipmaps;
        public int streamingMipmapsPriority;
        public float mipmapBias;

        public bool separateWrapModes;
        public TextureWrapMode wrapMode = TextureWrapMode.Repeat;
        public TextureWrapMode wrapModeU = TextureWrapMode.Repeat;
        public TextureWrapMode wrapModeV = TextureWrapMode.Repeat;
        public FilterMode filterMode = FilterMode.Bilinear;
        public int anisoLevel = 1;

        public TextureCommonSettingsSnapshot Clone()
        {
            return (TextureCommonSettingsSnapshot)MemberwiseClone();
        }
    }

    [Serializable]
    internal sealed class TexturePlatformSettingsSnapshot
    {
        public bool overridden;
        public int maxTextureSize = 2048;
        public TextureResizeAlgorithm resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
        public TextureImporterFormat format = TextureImporterFormat.Automatic;
        public TextureImporterCompression compression = TextureImporterCompression.Compressed;
        public bool useCrunchCompression;
        public int compressionQuality = 50;
        public bool allowsAlphaSplitting;
        public int androidEtc2FallbackOverride;

        public TexturePlatformSettingsSnapshot Clone()
        {
            return (TexturePlatformSettingsSnapshot)MemberwiseClone();
        }
    }

    [Serializable]
    internal sealed class TextureImagePresetRecord
    {
        public string name;
        public bool hasCommonSettings = true;
        public TextureCommonSettingsSnapshot commonSettings = new TextureCommonSettingsSnapshot();
        public TexturePlatformSettingsSnapshot defaultSettings = new TexturePlatformSettingsSnapshot();
        public TexturePlatformSettingsSnapshot standaloneSettings = new TexturePlatformSettingsSnapshot();
        public TexturePlatformSettingsSnapshot androidSettings = new TexturePlatformSettingsSnapshot();
    }

    [Serializable]
    internal sealed class TextureImagePresetCollection
    {
        public List<TextureImagePresetRecord> presets = new List<TextureImagePresetRecord>();
    }

    // Matches the former v1 EditorPrefs JSON shape so existing user presets can be migrated.
    [Serializable]
    internal sealed class LegacyTexturePlatformPreset
    {
        public bool overridden;
        public int maxTextureSize = 2048;
        public TextureResizeAlgorithm resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
        public TextureImporterFormat format = TextureImporterFormat.Automatic;
        public TextureImporterCompression compression = TextureImporterCompression.Compressed;
        public bool useCrunchCompression;
        public int compressionQuality = 50;
        public int androidEtc2FallbackOverride;

        public TexturePlatformSettingsSnapshot ToSnapshot()
        {
            return new TexturePlatformSettingsSnapshot
            {
                overridden = overridden,
                maxTextureSize = maxTextureSize,
                resizeAlgorithm = resizeAlgorithm,
                format = format,
                compression = compression,
                useCrunchCompression = useCrunchCompression,
                compressionQuality = compressionQuality,
                androidEtc2FallbackOverride = androidEtc2FallbackOverride
            };
        }
    }

    [Serializable]
    internal sealed class LegacyTexturePlatformPresetRecord
    {
        public string name;
        public LegacyTexturePlatformPreset defaultSettings = new LegacyTexturePlatformPreset();
        public LegacyTexturePlatformPreset standaloneSettings = new LegacyTexturePlatformPreset();
        public LegacyTexturePlatformPreset androidSettings = new LegacyTexturePlatformPreset();
    }

    [Serializable]
    internal sealed class LegacyTexturePlatformPresetCollection
    {
        public List<LegacyTexturePlatformPresetRecord> presets = new List<LegacyTexturePlatformPresetRecord>();
    }

    internal static class TextureImageSettingsUtility
    {
        internal const string DefaultPlatformName = "DefaultTexturePlatform";
        internal const string StandalonePlatformName = "Standalone";
        internal const string AndroidPlatformName = "Android";

        internal static void ReadTextureImporter(
            TextureImporter importer,
            out TextureCommonSettingsSnapshot common,
            out TexturePlatformSettingsSnapshot defaultSettings,
            out TexturePlatformSettingsSnapshot standaloneSettings,
            out TexturePlatformSettingsSnapshot androidSettings)
        {
            if (importer == null)
                throw new ArgumentNullException(nameof(importer));

            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);

            common = new TextureCommonSettingsSnapshot
            {
                textureType = settings.textureType,
                textureShape = settings.textureShape,
                sRGBTexture = settings.sRGBTexture,
                alphaSource = settings.alphaSource,
                alphaIsTransparency = settings.alphaIsTransparency,
                npotScale = settings.npotScale,
                readable = settings.readable,
                virtualTextureOnly = settings.vtOnly,
                ignorePngGamma = settings.ignorePngGamma,
                swizzleR = settings.swizzleR,
                swizzleG = settings.swizzleG,
                swizzleB = settings.swizzleB,
                swizzleA = settings.swizzleA,
                spriteImportMode = (SpriteImportMode)settings.spriteMode,
                spritePixelsPerUnit = settings.spritePixelsPerUnit,
                spriteMeshType = settings.spriteMeshType,
                spriteExtrude = (int)settings.spriteExtrude,
                spriteAlignment = settings.spriteAlignment,
                spritePivot = settings.spritePivot,
                spriteGenerateFallbackPhysicsShape = settings.spriteGenerateFallbackPhysicsShape,
                convertToNormalMap = settings.convertToNormalMap,
                heightmapScale = settings.heightmapScale,
                normalMapFilter = settings.normalMapFilter,
                flipGreenChannel = settings.flipGreenChannel,
                singleChannelComponent = settings.singleChannelComponent,
                mipmapEnabled = settings.mipmapEnabled,
                ignoreMipmapLimit = settings.ignoreMipmapLimit,
                mipmapLimitGroupName = importer.mipmapLimitGroupName,
                mipmapFilter = settings.mipmapFilter,
                mipMapsPreserveCoverage = settings.mipMapsPreserveCoverage,
                alphaTestReferenceValue = settings.alphaTestReferenceValue,
                borderMipmap = settings.borderMipmap,
                fadeOut = settings.fadeOut,
                mipmapFadeDistanceStart = settings.mipmapFadeDistanceStart,
                mipmapFadeDistanceEnd = settings.mipmapFadeDistanceEnd,
                streamingMipmaps = settings.streamingMipmaps,
                streamingMipmapsPriority = settings.streamingMipmapsPriority,
                mipmapBias = settings.mipmapBias,
                wrapMode = settings.wrapMode,
                wrapModeU = settings.wrapModeU,
                wrapModeV = settings.wrapModeV,
                filterMode = settings.filterMode,
                anisoLevel = settings.aniso
            };

            common.separateWrapModes = common.wrapModeU != common.wrapModeV;
            defaultSettings = FromPlatformSettings(importer.GetPlatformTextureSettings(DefaultPlatformName));
            standaloneSettings = FromPlatformSettings(importer.GetPlatformTextureSettings(StandalonePlatformName));
            androidSettings = FromPlatformSettings(importer.GetPlatformTextureSettings(AndroidPlatformName));
        }

        internal static void ApplyTextureImporter(
            TextureImporter importer,
            TextureCommonSettingsSnapshot common,
            TexturePlatformSettingsSnapshot defaultSettings,
            TexturePlatformSettingsSnapshot standaloneSettings,
            TexturePlatformSettingsSnapshot androidSettings)
        {
            if (importer == null)
                throw new ArgumentNullException(nameof(importer));
            if (common == null)
                throw new ArgumentNullException(nameof(common));

            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);

            settings.textureType = common.textureType;
            settings.textureShape = common.textureShape;
            settings.sRGBTexture = common.sRGBTexture;
            settings.alphaSource = common.alphaSource;
            settings.alphaIsTransparency = common.alphaIsTransparency;
            settings.npotScale = common.npotScale;
            settings.readable = common.readable;
            settings.vtOnly = common.virtualTextureOnly;
            settings.ignorePngGamma = common.ignorePngGamma;
            settings.swizzleR = common.swizzleR;
            settings.swizzleG = common.swizzleG;
            settings.swizzleB = common.swizzleB;
            settings.swizzleA = common.swizzleA;
            settings.spriteMode = (int)common.spriteImportMode;
            settings.spritePixelsPerUnit = Mathf.Max(1f, common.spritePixelsPerUnit);
            settings.spriteMeshType = common.spriteMeshType;
            settings.spriteExtrude = (uint)Mathf.Clamp(common.spriteExtrude, 0, 32);
            settings.spriteAlignment = common.spriteAlignment;
            settings.spritePivot = common.spritePivot;
            settings.spriteGenerateFallbackPhysicsShape = common.spriteGenerateFallbackPhysicsShape;
            settings.convertToNormalMap = common.convertToNormalMap;
            settings.heightmapScale = Mathf.Max(0f, common.heightmapScale);
            settings.normalMapFilter = common.normalMapFilter;
            settings.flipGreenChannel = common.flipGreenChannel;
            settings.singleChannelComponent = common.singleChannelComponent;
            settings.mipmapEnabled = common.mipmapEnabled;
            settings.ignoreMipmapLimit = common.ignoreMipmapLimit;
            settings.mipmapFilter = common.mipmapFilter;
            settings.mipMapsPreserveCoverage = common.mipMapsPreserveCoverage;
            settings.alphaTestReferenceValue = Mathf.Clamp01(common.alphaTestReferenceValue);
            settings.borderMipmap = common.borderMipmap;
            settings.fadeOut = common.fadeOut;
            settings.mipmapFadeDistanceStart = Mathf.Max(0, common.mipmapFadeDistanceStart);
            settings.mipmapFadeDistanceEnd = Mathf.Max(settings.mipmapFadeDistanceStart, common.mipmapFadeDistanceEnd);
            settings.streamingMipmaps = common.streamingMipmaps;
            settings.streamingMipmapsPriority = Mathf.Clamp(common.streamingMipmapsPriority, -128, 127);
            settings.mipmapBias = common.mipmapBias;

            if (common.separateWrapModes)
            {
                settings.wrapModeU = common.wrapModeU;
                settings.wrapModeV = common.wrapModeV;
            }
            else
            {
                settings.wrapMode = common.wrapMode;
                settings.wrapModeU = common.wrapMode;
                settings.wrapModeV = common.wrapMode;
            }

            settings.filterMode = common.filterMode;
            settings.aniso = Mathf.Clamp(common.anisoLevel, 0, 16);
            importer.SetTextureSettings(settings);
            importer.mipmapLimitGroupName = common.mipmapLimitGroupName ?? string.Empty;

            ApplyDefaultTextureSettings(importer, defaultSettings);
            ApplyPlatformTextureSettings(importer, StandalonePlatformName, standaloneSettings, false);
            ApplyPlatformTextureSettings(importer, AndroidPlatformName, androidSettings, true);
        }

        internal static void ApplySpriteAtlas(
            AssetImporter importer,
            TexturePlatformSettingsSnapshot defaultSettings,
            TexturePlatformSettingsSnapshot standaloneSettings,
            TexturePlatformSettingsSnapshot androidSettings)
        {
            ApplyAtlasPlatformSettings(importer, DefaultPlatformName, defaultSettings, false, true);
            ApplyAtlasPlatformSettings(importer, StandalonePlatformName, standaloneSettings, false, false);
            ApplyAtlasPlatformSettings(importer, AndroidPlatformName, androidSettings, true, false);
        }

        private static TexturePlatformSettingsSnapshot FromPlatformSettings(TextureImporterPlatformSettings settings)
        {
            return new TexturePlatformSettingsSnapshot
            {
                overridden = settings.overridden,
                maxTextureSize = settings.maxTextureSize,
                resizeAlgorithm = settings.resizeAlgorithm,
                format = settings.format,
                compression = settings.textureCompression,
                useCrunchCompression = settings.crunchedCompression,
                compressionQuality = settings.compressionQuality,
                allowsAlphaSplitting = settings.allowsAlphaSplitting,
                androidEtc2FallbackOverride = GetAndroidEtc2FallbackOverride(settings)
            };
        }

        private static void ApplyDefaultTextureSettings(TextureImporter importer, TexturePlatformSettingsSnapshot snapshot)
        {
            TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(DefaultPlatformName);
            CopyToPlatformSettings(settings, DefaultPlatformName, snapshot, false, true);
            importer.SetPlatformTextureSettings(settings);

            importer.maxTextureSize = snapshot.maxTextureSize;
            importer.textureCompression = snapshot.compression;
            importer.crunchedCompression = snapshot.useCrunchCompression;
            importer.compressionQuality = snapshot.compressionQuality;
        }

        private static void ApplyPlatformTextureSettings(
            TextureImporter importer,
            string platformName,
            TexturePlatformSettingsSnapshot snapshot,
            bool isAndroid)
        {
            TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platformName);
            CopyToPlatformSettings(settings, platformName, snapshot, isAndroid, false);
            importer.SetPlatformTextureSettings(settings);
        }

        private static void ApplyAtlasPlatformSettings(
            AssetImporter importer,
            string platformName,
            TexturePlatformSettingsSnapshot snapshot,
            bool isAndroid,
            bool isDefault)
        {
            TextureImporterPlatformSettings settings = GetAtlasPlatformSettings(importer, platformName);
            CopyToPlatformSettings(settings, platformName, snapshot, isAndroid, isDefault);
            SetAtlasPlatformSettings(importer, settings);
        }

        private static void CopyToPlatformSettings(
            TextureImporterPlatformSettings settings,
            string platformName,
            TexturePlatformSettingsSnapshot snapshot,
            bool isAndroid,
            bool isDefault)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            settings.name = platformName;
            settings.overridden = !isDefault && snapshot.overridden;
            settings.maxTextureSize = snapshot.maxTextureSize;
            settings.resizeAlgorithm = snapshot.resizeAlgorithm;
            settings.format = snapshot.format;
            settings.textureCompression = snapshot.compression;
            settings.crunchedCompression = snapshot.useCrunchCompression;
            settings.compressionQuality = Mathf.Clamp(snapshot.compressionQuality, 0, 100);
            settings.allowsAlphaSplitting = isAndroid && snapshot.allowsAlphaSplitting;

            if (isAndroid)
                SetAndroidEtc2FallbackOverride(settings, snapshot.androidEtc2FallbackOverride);
        }

        private static TextureImporterPlatformSettings GetAtlasPlatformSettings(AssetImporter importer, string platformName)
        {
            if (importer == null)
                throw new ArgumentNullException(nameof(importer));

            MethodInfo method = importer.GetType().GetMethod(
                "GetPlatformSettings",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string) },
                null);

            if (method == null)
                throw new MissingMethodException(importer.GetType().FullName, "GetPlatformSettings");

            return (TextureImporterPlatformSettings)method.Invoke(importer, new object[] { platformName });
        }

        private static void SetAtlasPlatformSettings(AssetImporter importer, TextureImporterPlatformSettings settings)
        {
            MethodInfo method = importer.GetType().GetMethod(
                "SetPlatformSettings",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(TextureImporterPlatformSettings) },
                null);

            if (method == null)
                throw new MissingMethodException(importer.GetType().FullName, "SetPlatformSettings");

            method.Invoke(importer, new object[] { settings });
        }

        private static int GetAndroidEtc2FallbackOverride(TextureImporterPlatformSettings settings)
        {
            PropertyInfo property = typeof(TextureImporterPlatformSettings).GetProperty("androidETC2FallbackOverride");
            if (property == null || !property.CanRead)
                return 0;

            object value = property.GetValue(settings, null);
            return value == null ? 0 : Convert.ToInt32(value);
        }

        private static void SetAndroidEtc2FallbackOverride(TextureImporterPlatformSettings settings, int value)
        {
            PropertyInfo property = typeof(TextureImporterPlatformSettings).GetProperty("androidETC2FallbackOverride");
            if (property == null || !property.CanWrite)
                return;

            object convertedValue = Enum.ToObject(property.PropertyType, value);
            property.SetValue(settings, convertedValue, null);
        }
    }
}
#endif
