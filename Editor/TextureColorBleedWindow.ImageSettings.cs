#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SleepyCobalt.Tools.TextureTools
{
    public sealed partial class TextureColorBleedWindow
    {
        private enum PlatformSettingsKind
        {
            Default,
            Standalone,
            Android
        }

        [SerializeField] private Texture2D imageSettingsSource;
        [SerializeField] private TextureCommonSettingsSnapshot imageCommonSettings = new TextureCommonSettingsSnapshot();
        [SerializeField] private TexturePlatformSettingsSnapshot imageDefaultSettings = new TexturePlatformSettingsSnapshot();
        [SerializeField] private TexturePlatformSettingsSnapshot imageStandaloneSettings = new TexturePlatformSettingsSnapshot();
        [SerializeField] private TexturePlatformSettingsSnapshot imageAndroidSettings = new TexturePlatformSettingsSnapshot();
        [SerializeField] private bool imageSettingsIncludeSubfolders = true;
        [SerializeField] private string newImagePresetName = "新预设";
        [SerializeField] private int selectedImagePresetIndex;
        [SerializeField] private Vector2 imageSettingsScrollPosition;
        [SerializeField] private Vector2 platformSettingsScrollPosition;
        [SerializeField] private string loadedImageSettingsSourcePath = string.Empty;
        [SerializeField] private bool migratedToBatchImageSettingsNavigation;

        private const string ImagePresetPrefsKey = "CobaltTools.TextureTools.ImagePresets.v2";
        private TextureImagePresetCollection imagePresetCollection;
        private bool loadedLegacyImagePreset;

        private static readonly string[] WrapModeLabels =
        {
            "Repeat",
            "Clamp",
            "Mirror",
            "Mirror Once",
            "Per-axis"
        };

        private static readonly TextureWrapMode[] WrapModeValues =
        {
            TextureWrapMode.Repeat,
            TextureWrapMode.Clamp,
            TextureWrapMode.Mirror,
            TextureWrapMode.MirrorOnce
        };

        private void OnEnable()
        {
            if (!migratedToBatchImageSettingsNavigation || !Enum.IsDefined(typeof(ToolPage), currentPage))
            {
                currentPage = ToolPage.BatchImageSettings;
                migratedToBatchImageSettingsNavigation = true;
            }

            EnsureImageSettingsObjects();
        }

        private void EnsureImageSettingsObjects()
        {
            if (imageCommonSettings == null)
                imageCommonSettings = new TextureCommonSettingsSnapshot();
            if (imageDefaultSettings == null)
                imageDefaultSettings = new TexturePlatformSettingsSnapshot();
            if (imageStandaloneSettings == null)
                imageStandaloneSettings = new TexturePlatformSettingsSnapshot();
            if (imageAndroidSettings == null)
                imageAndroidSettings = new TexturePlatformSettingsSnapshot();
        }

        private void DrawBatchImageSettingsPage()
        {
            EnsureImageSettingsObjects();

            imageSettingsScrollPosition = EditorGUILayout.BeginScrollView(imageSettingsScrollPosition);
            EditorGUILayout.LabelField("批量图像设置", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "读取一张来源图片的导入参数，或手动配置完整设置，再批量应用到选中的图片和文件夹。" +
                "SpriteAtlas 只会接收下方的 Default、PC 和 Android 平台设置。",
                MessageType.Info);

            DrawImageSettingsSourceControls();
            EditorGUILayout.Space(6f);
            DrawImagePresetControls();
            EditorGUILayout.Space(8f);
            DrawCommonImageSettings();
            EditorGUILayout.Space(8f);
            DrawMipmapImageSettings();
            EditorGUILayout.Space(8f);
            DrawSamplingImageSettings();
            EditorGUILayout.Space(10f);
            DrawAllPlatformImageSettings();
            EditorGUILayout.Space(10f);
            DrawImageSettingsTargets();
            EditorGUILayout.EndScrollView();
        }

        private void DrawImageSettingsSourceControls()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("参数来源", EditorStyles.boldLabel);

            imageSettingsSource = (Texture2D)EditorGUILayout.ObjectField(
                new GUIContent("来源图片", "只用于读取参数，不会改变批量处理目标。"),
                imageSettingsSource,
                typeof(Texture2D),
                false);

            using (new EditorGUI.DisabledScope(imageSettingsSource == null))
            {
                if (GUILayout.Button("读取图片设置"))
                    ReadSettingsFromSourceImage();
            }

            if (!string.IsNullOrEmpty(loadedImageSettingsSourcePath))
                EditorGUILayout.LabelField("已读取", loadedImageSettingsSourcePath, EditorStyles.miniLabel);

            EditorGUILayout.EndVertical();
        }

        private void ReadSettingsFromSourceImage()
        {
            string path = AssetDatabase.GetAssetPath(imageSettingsSource);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                loadedImageSettingsSourcePath = string.Empty;
                EditorUtility.DisplayDialog(
                    "无法读取图片设置",
                    "请选择 Project 窗口中由 TextureImporter 导入的图片资源。",
                    "确定");
                return;
            }

            try
            {
                TextureImageSettingsUtility.ReadTextureImporter(
                    importer,
                    out imageCommonSettings,
                    out imageDefaultSettings,
                    out imageStandaloneSettings,
                    out imageAndroidSettings);

                loadedImageSettingsSourcePath = path;
                loadedLegacyImagePreset = false;
                Repaint();
            }
            catch (Exception exception)
            {
                loadedImageSettingsSourcePath = string.Empty;
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("无法读取图片设置", exception.Message, "确定");
            }
        }

        private void DrawImagePresetControls()
        {
            EnsureImagePresetsLoaded();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("预设", EditorStyles.boldLabel);

            string[] presetNames = GetImagePresetNames();
            if (presetNames.Length > 0)
            {
                selectedImagePresetIndex = Mathf.Clamp(selectedImagePresetIndex, 0, presetNames.Length - 1);
                selectedImagePresetIndex = EditorGUILayout.Popup(
                    new GUIContent("选择预设"),
                    selectedImagePresetIndex,
                    presetNames);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("加载预设"))
                    LoadSelectedImagePreset();
                if (GUILayout.Button("删除预设"))
                    DeleteSelectedImagePreset();
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox("还没有自定义预设。可以读取图片或调整参数后保存。", MessageType.None);
            }

            if (loadedLegacyImagePreset)
            {
                EditorGUILayout.HelpBox(
                    "当前加载的是旧平台预设，只更新了 Default、PC 和 Android 设置；通用图片参数保持不变。重新保存后会升级为完整预设。",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(4f);
            newImagePresetName = EditorGUILayout.TextField("预设名称", newImagePresetName);
            if (GUILayout.Button("保存当前为完整预设"))
                SaveCurrentImagePreset();

            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("载入推荐小体积平台方案"))
                LoadCompactGradientImageSettings();
            if (GUILayout.Button("载入 UI 图集小体积平台方案"))
                LoadCompactUiAtlasImageSettings();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void EnsureImagePresetsLoaded()
        {
            if (imagePresetCollection != null)
                return;

            string json = EditorPrefs.GetString(ImagePresetPrefsKey, string.Empty);
            if (!string.IsNullOrEmpty(json))
                imagePresetCollection = JsonUtility.FromJson<TextureImagePresetCollection>(json);

            if (imagePresetCollection == null)
                imagePresetCollection = new TextureImagePresetCollection();
            if (imagePresetCollection.presets == null)
                imagePresetCollection.presets = new List<TextureImagePresetRecord>();

            if (string.IsNullOrEmpty(json))
                MigrateLegacyImagePresets();
        }

        private void MigrateLegacyImagePresets()
        {
            string legacyJson = EditorPrefs.GetString(PlatformPresetPrefsKey, string.Empty);
            if (string.IsNullOrEmpty(legacyJson))
                return;

            LegacyTexturePlatformPresetCollection legacyCollection =
                JsonUtility.FromJson<LegacyTexturePlatformPresetCollection>(legacyJson);
            if (legacyCollection == null || legacyCollection.presets == null)
                return;

            foreach (LegacyTexturePlatformPresetRecord legacyRecord in legacyCollection.presets)
            {
                if (legacyRecord == null || string.IsNullOrEmpty(legacyRecord.name))
                    continue;

                imagePresetCollection.presets.Add(new TextureImagePresetRecord
                {
                    name = legacyRecord.name,
                    hasCommonSettings = false,
                    commonSettings = null,
                    defaultSettings = legacyRecord.defaultSettings == null
                        ? new TexturePlatformSettingsSnapshot()
                        : legacyRecord.defaultSettings.ToSnapshot(),
                    standaloneSettings = legacyRecord.standaloneSettings == null
                        ? new TexturePlatformSettingsSnapshot()
                        : legacyRecord.standaloneSettings.ToSnapshot(),
                    androidSettings = legacyRecord.androidSettings == null
                        ? new TexturePlatformSettingsSnapshot()
                        : legacyRecord.androidSettings.ToSnapshot()
                });
            }

            if (imagePresetCollection.presets.Count > 0)
                SaveImagePresets();
        }

        private void SaveImagePresets()
        {
            EnsureImagePresetsLoaded();
            EditorPrefs.SetString(ImagePresetPrefsKey, JsonUtility.ToJson(imagePresetCollection));
        }

        private string[] GetImagePresetNames()
        {
            string[] names = new string[imagePresetCollection.presets.Count];
            for (int i = 0; i < names.Length; i++)
            {
                TextureImagePresetRecord preset = imagePresetCollection.presets[i];
                names[i] = preset.hasCommonSettings ? preset.name : preset.name + "（旧平台预设）";
            }

            return names;
        }

        private void SaveCurrentImagePreset()
        {
            EnsureImagePresetsLoaded();
            string presetName = string.IsNullOrEmpty(newImagePresetName)
                ? string.Empty
                : newImagePresetName.Trim();

            if (string.IsNullOrEmpty(presetName))
            {
                EditorUtility.DisplayDialog("无法保存预设", "请输入预设名称。", "确定");
                return;
            }

            TextureImagePresetRecord record = imagePresetCollection.presets.Find(item => item.name == presetName);
            if (record != null && !EditorUtility.DisplayDialog(
                    "覆盖预设",
                    "预设“" + presetName + "”已存在，是否用当前完整设置覆盖？",
                    "覆盖",
                    "取消"))
            {
                return;
            }

            if (record == null)
            {
                record = new TextureImagePresetRecord { name = presetName };
                imagePresetCollection.presets.Add(record);
                selectedImagePresetIndex = imagePresetCollection.presets.Count - 1;
            }

            record.name = presetName;
            record.hasCommonSettings = true;
            record.commonSettings = imageCommonSettings.Clone();
            record.defaultSettings = imageDefaultSettings.Clone();
            record.standaloneSettings = imageStandaloneSettings.Clone();
            record.androidSettings = imageAndroidSettings.Clone();
            loadedLegacyImagePreset = false;

            SaveImagePresets();
            EditorUtility.DisplayDialog("保存预设", "已保存完整预设“" + presetName + "”。", "确定");
        }

        private void LoadSelectedImagePreset()
        {
            if (imagePresetCollection.presets.Count == 0)
                return;

            selectedImagePresetIndex = Mathf.Clamp(
                selectedImagePresetIndex,
                0,
                imagePresetCollection.presets.Count - 1);

            TextureImagePresetRecord record = imagePresetCollection.presets[selectedImagePresetIndex];
            if (record.hasCommonSettings && record.commonSettings != null)
                imageCommonSettings = record.commonSettings.Clone();

            imageDefaultSettings = record.defaultSettings == null
                ? new TexturePlatformSettingsSnapshot()
                : record.defaultSettings.Clone();
            imageStandaloneSettings = record.standaloneSettings == null
                ? new TexturePlatformSettingsSnapshot()
                : record.standaloneSettings.Clone();
            imageAndroidSettings = record.androidSettings == null
                ? new TexturePlatformSettingsSnapshot()
                : record.androidSettings.Clone();
            newImagePresetName = record.name;
            loadedLegacyImagePreset = !record.hasCommonSettings;
            loadedImageSettingsSourcePath = string.Empty;
        }

        private void DeleteSelectedImagePreset()
        {
            if (imagePresetCollection.presets.Count == 0)
                return;

            selectedImagePresetIndex = Mathf.Clamp(
                selectedImagePresetIndex,
                0,
                imagePresetCollection.presets.Count - 1);

            TextureImagePresetRecord record = imagePresetCollection.presets[selectedImagePresetIndex];
            if (!EditorUtility.DisplayDialog(
                    "删除预设",
                    "确定删除预设“" + record.name + "”？",
                    "删除",
                    "取消"))
            {
                return;
            }

            imagePresetCollection.presets.RemoveAt(selectedImagePresetIndex);
            selectedImagePresetIndex = Mathf.Clamp(
                selectedImagePresetIndex,
                0,
                imagePresetCollection.presets.Count - 1);
            loadedLegacyImagePreset = false;
            SaveImagePresets();
        }

        private void LoadCompactGradientImageSettings()
        {
            ConfigureCompactPlatformSettings(TextureImporterFormat.ASTC_8x8, true);
            newImagePresetName = "纯色渐变小体积";
            loadedLegacyImagePreset = false;
        }

        private void LoadCompactUiAtlasImageSettings()
        {
            ConfigureCompactPlatformSettings(TextureImporterFormat.ASTC_8x8, false);
            newImagePresetName = "UI图集小体积";
            loadedLegacyImagePreset = false;
        }

        private void ConfigureCompactPlatformSettings(TextureImporterFormat androidFormat, bool crunchDefault)
        {
            imageDefaultSettings.maxTextureSize = 1024;
            imageDefaultSettings.resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
            imageDefaultSettings.format = TextureImporterFormat.Automatic;
            imageDefaultSettings.compression = crunchDefault
                ? TextureImporterCompression.CompressedLQ
                : TextureImporterCompression.Compressed;
            imageDefaultSettings.useCrunchCompression = crunchDefault;
            imageDefaultSettings.compressionQuality = 50;

            imageStandaloneSettings.overridden = true;
            imageStandaloneSettings.maxTextureSize = 1024;
            imageStandaloneSettings.resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
            imageStandaloneSettings.format = TextureImporterFormat.Automatic;
            imageStandaloneSettings.compression = TextureImporterCompression.Compressed;
            imageStandaloneSettings.useCrunchCompression = false;
            imageStandaloneSettings.compressionQuality = 50;

            imageAndroidSettings.overridden = true;
            imageAndroidSettings.maxTextureSize = 1024;
            imageAndroidSettings.resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
            imageAndroidSettings.format = androidFormat;
            imageAndroidSettings.compression = TextureImporterCompression.Compressed;
            imageAndroidSettings.useCrunchCompression = false;
            imageAndroidSettings.compressionQuality = 50;
            imageAndroidSettings.androidEtc2FallbackOverride = 0;
        }

        private void DrawCommonImageSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("基础设置", EditorStyles.boldLabel);

            imageCommonSettings.textureType = (TextureImporterType)EditorGUILayout.EnumPopup(
                new GUIContent("Texture Type"),
                imageCommonSettings.textureType);

            if (imageCommonSettings.textureType != TextureImporterType.NormalMap)
                imageCommonSettings.sRGBTexture = EditorGUILayout.Toggle("sRGB (Color Texture)", imageCommonSettings.sRGBTexture);

            imageCommonSettings.alphaSource = (TextureImporterAlphaSource)EditorGUILayout.EnumPopup(
                "Alpha Source",
                imageCommonSettings.alphaSource);
            if (imageCommonSettings.alphaSource != TextureImporterAlphaSource.None)
                imageCommonSettings.alphaIsTransparency = EditorGUILayout.Toggle(
                    "Alpha Is Transparency",
                    imageCommonSettings.alphaIsTransparency);

            imageCommonSettings.npotScale = (TextureImporterNPOTScale)EditorGUILayout.EnumPopup(
                "Non Power of 2",
                imageCommonSettings.npotScale);
            imageCommonSettings.readable = EditorGUILayout.Toggle("Read/Write", imageCommonSettings.readable);
            imageCommonSettings.ignorePngGamma = EditorGUILayout.Toggle("Ignore PNG Gamma", imageCommonSettings.ignorePngGamma);

            DrawTextureTypeSpecificSettings();
            EditorGUILayout.EndVertical();
        }

        private void DrawTextureTypeSpecificSettings()
        {
            if (imageCommonSettings.textureType == TextureImporterType.Sprite)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("Sprite", EditorStyles.boldLabel);
                imageCommonSettings.spriteImportMode = (SpriteImportMode)EditorGUILayout.EnumPopup(
                    "Sprite Mode",
                    imageCommonSettings.spriteImportMode);
                imageCommonSettings.spritePixelsPerUnit = Mathf.Max(
                    1f,
                    EditorGUILayout.FloatField("Pixels Per Unit", imageCommonSettings.spritePixelsPerUnit));
                imageCommonSettings.spriteMeshType = (SpriteMeshType)EditorGUILayout.EnumPopup(
                    "Mesh Type",
                    imageCommonSettings.spriteMeshType);
                imageCommonSettings.spriteExtrude = EditorGUILayout.IntSlider(
                    "Extrude Edges",
                    imageCommonSettings.spriteExtrude,
                    0,
                    32);

                if (imageCommonSettings.spriteImportMode == SpriteImportMode.Single ||
                    imageCommonSettings.spriteImportMode == SpriteImportMode.Polygon)
                {
                    SpriteAlignment alignment = (SpriteAlignment)imageCommonSettings.spriteAlignment;
                    alignment = (SpriteAlignment)EditorGUILayout.EnumPopup("Pivot", alignment);
                    imageCommonSettings.spriteAlignment = (int)alignment;
                    if (alignment == SpriteAlignment.Custom)
                        imageCommonSettings.spritePivot = EditorGUILayout.Vector2Field("Custom Pivot", imageCommonSettings.spritePivot);
                }

                imageCommonSettings.spriteGenerateFallbackPhysicsShape = EditorGUILayout.Toggle(
                    "Generate Physics Shape",
                    imageCommonSettings.spriteGenerateFallbackPhysicsShape);
            }
            else if (imageCommonSettings.textureType == TextureImporterType.NormalMap)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("Normal Map", EditorStyles.boldLabel);
                imageCommonSettings.convertToNormalMap = EditorGUILayout.Toggle(
                    "Create from Grayscale",
                    imageCommonSettings.convertToNormalMap);
                if (imageCommonSettings.convertToNormalMap)
                {
                    imageCommonSettings.heightmapScale = EditorGUILayout.Slider(
                        "Bumpiness",
                        imageCommonSettings.heightmapScale,
                        0f,
                        0.3f);
                    imageCommonSettings.normalMapFilter = (TextureImporterNormalFilter)EditorGUILayout.EnumPopup(
                        "Filtering",
                        imageCommonSettings.normalMapFilter);
                }

                imageCommonSettings.flipGreenChannel = EditorGUILayout.Toggle(
                    "Flip Green Channel",
                    imageCommonSettings.flipGreenChannel);
            }
            else if (imageCommonSettings.textureType == TextureImporterType.SingleChannel)
            {
                imageCommonSettings.singleChannelComponent =
                    (TextureImporterSingleChannelComponent)EditorGUILayout.EnumPopup(
                        "Channel",
                        imageCommonSettings.singleChannelComponent);
            }
        }

        private void DrawMipmapImageSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Mipmap", EditorStyles.boldLabel);
            imageCommonSettings.mipmapEnabled = EditorGUILayout.Toggle(
                "Generate Mip Maps",
                imageCommonSettings.mipmapEnabled);

            using (new EditorGUI.DisabledScope(!imageCommonSettings.mipmapEnabled))
            {
                bool useMipmapLimits = !imageCommonSettings.ignoreMipmapLimit;
                useMipmapLimits = EditorGUILayout.Toggle("Use Mipmap Limits", useMipmapLimits);
                imageCommonSettings.ignoreMipmapLimit = !useMipmapLimits;
                if (useMipmapLimits)
                    imageCommonSettings.mipmapLimitGroupName = EditorGUILayout.TextField(
                        "Mipmap Limit Group",
                        imageCommonSettings.mipmapLimitGroupName ?? string.Empty);

                imageCommonSettings.mipmapFilter = (TextureImporterMipFilter)EditorGUILayout.EnumPopup(
                    "Mipmap Filtering",
                    imageCommonSettings.mipmapFilter);
                imageCommonSettings.mipMapsPreserveCoverage = EditorGUILayout.Toggle(
                    "Preserve Coverage",
                    imageCommonSettings.mipMapsPreserveCoverage);
                if (imageCommonSettings.mipMapsPreserveCoverage)
                {
                    imageCommonSettings.alphaTestReferenceValue = EditorGUILayout.Slider(
                        "Alpha Cutoff",
                        imageCommonSettings.alphaTestReferenceValue,
                        0f,
                        1f);
                }

                imageCommonSettings.borderMipmap = EditorGUILayout.Toggle(
                    "Replicate Border",
                    imageCommonSettings.borderMipmap);
                imageCommonSettings.fadeOut = EditorGUILayout.Toggle("Fadeout to Gray", imageCommonSettings.fadeOut);
                if (imageCommonSettings.fadeOut)
                {
                    imageCommonSettings.mipmapFadeDistanceStart = Mathf.Max(
                        0,
                        EditorGUILayout.IntField("Fade Start", imageCommonSettings.mipmapFadeDistanceStart));
                    imageCommonSettings.mipmapFadeDistanceEnd = Mathf.Max(
                        imageCommonSettings.mipmapFadeDistanceStart,
                        EditorGUILayout.IntField("Fade End", imageCommonSettings.mipmapFadeDistanceEnd));
                }

                imageCommonSettings.streamingMipmaps = EditorGUILayout.Toggle(
                    "Mip Streaming",
                    imageCommonSettings.streamingMipmaps);
                if (imageCommonSettings.streamingMipmaps)
                {
                    imageCommonSettings.streamingMipmapsPriority = EditorGUILayout.IntSlider(
                        "Mip Map Priority",
                        imageCommonSettings.streamingMipmapsPriority,
                        -128,
                        127);
                }

                imageCommonSettings.mipmapBias = EditorGUILayout.FloatField(
                    "Mip Map Bias",
                    imageCommonSettings.mipmapBias);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSamplingImageSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("采样", EditorStyles.boldLabel);

            int wrapIndex = 4;
            if (!imageCommonSettings.separateWrapModes)
            {
                wrapIndex = Array.IndexOf(WrapModeValues, imageCommonSettings.wrapMode);
                if (wrapIndex < 0)
                    wrapIndex = 0;
            }

            int selectedWrapIndex = EditorGUILayout.Popup("Wrap Mode", wrapIndex, WrapModeLabels);
            imageCommonSettings.separateWrapModes = selectedWrapIndex == 4;
            if (imageCommonSettings.separateWrapModes)
            {
                imageCommonSettings.wrapModeU = (TextureWrapMode)EditorGUILayout.EnumPopup(
                    "Wrap U",
                    imageCommonSettings.wrapModeU);
                imageCommonSettings.wrapModeV = (TextureWrapMode)EditorGUILayout.EnumPopup(
                    "Wrap V",
                    imageCommonSettings.wrapModeV);
            }
            else
            {
                imageCommonSettings.wrapMode = WrapModeValues[Mathf.Clamp(selectedWrapIndex, 0, WrapModeValues.Length - 1)];
                imageCommonSettings.wrapModeU = imageCommonSettings.wrapMode;
                imageCommonSettings.wrapModeV = imageCommonSettings.wrapMode;
            }

            imageCommonSettings.filterMode = (FilterMode)EditorGUILayout.EnumPopup(
                "Filter Mode",
                imageCommonSettings.filterMode);
            if (imageCommonSettings.filterMode != FilterMode.Point)
            {
                imageCommonSettings.anisoLevel = EditorGUILayout.IntSlider(
                    "Aniso Level",
                    imageCommonSettings.anisoLevel,
                    0,
                    16);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawAllPlatformImageSettings()
        {
            EditorGUILayout.LabelField("平台设置", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Default、PC 和 Android 会在一次批量操作中同时写入。PC 与 Android 关闭 Override 时仍会保存面板参数，但构建时继续继承 Default。",
                MessageType.None);

            platformSettingsScrollPosition = EditorGUILayout.BeginScrollView(
                platformSettingsScrollPosition,
                true,
                false,
                GUILayout.MinHeight(330f),
                GUILayout.MaxHeight(430f));
            EditorGUILayout.BeginHorizontal(GUILayout.MinWidth(840f));
            DrawPlatformImageSettingsCard("Default", imageDefaultSettings, PlatformSettingsKind.Default);
            DrawPlatformImageSettingsCard("PC", imageStandaloneSettings, PlatformSettingsKind.Standalone);
            DrawPlatformImageSettingsCard("Android", imageAndroidSettings, PlatformSettingsKind.Android);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();
        }

        private void DrawPlatformImageSettingsCard(
            string title,
            TexturePlatformSettingsSnapshot settings,
            PlatformSettingsKind kind)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(270f), GUILayout.ExpandHeight(true));
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

            bool isDefault = kind == PlatformSettingsKind.Default;
            if (!isDefault)
            {
                settings.overridden = EditorGUILayout.Toggle(
                    kind == PlatformSettingsKind.Android ? "Override For Android" : "Override For PC",
                    settings.overridden);
            }
            else
            {
                settings.overridden = false;
            }

            using (new EditorGUI.DisabledScope(!isDefault && !settings.overridden))
            {
                settings.maxTextureSize = EditorGUILayout.IntPopup(
                    "Max Size",
                    settings.maxTextureSize,
                    new[] { "32", "64", "128", "256", "512", "1024", "2048", "4096", "8192" },
                    new[] { 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192 });
                settings.resizeAlgorithm = (TextureResizeAlgorithm)EditorGUILayout.EnumPopup(
                    "Resize Algorithm",
                    settings.resizeAlgorithm);
                settings.format = DrawFilteredTextureFormatPopup(settings.format, kind);
                settings.compression = DrawTextureCompressionPopup(
                    new GUIContent("Compression"),
                    settings.compression);
                settings.useCrunchCompression = EditorGUILayout.Toggle(
                    "Use Crunch Compression",
                    settings.useCrunchCompression);

                if (settings.compression != TextureImporterCompression.Uncompressed)
                {
                    settings.compressionQuality = EditorGUILayout.IntSlider(
                        "Compressor Quality",
                        settings.compressionQuality,
                        0,
                        100);
                }

                if (kind == PlatformSettingsKind.Android)
                {
                    settings.allowsAlphaSplitting = EditorGUILayout.Toggle(
                        "Split Alpha Channel",
                        settings.allowsAlphaSplitting);
                    settings.androidEtc2FallbackOverride = EditorGUILayout.IntPopup(
                        "Override ETC2 fallback",
                        settings.androidEtc2FallbackOverride,
                        Etc2FallbackLabels,
                        Etc2FallbackValues);
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();
        }

        private TextureImporterFormat DrawFilteredTextureFormatPopup(
            TextureImporterFormat current,
            PlatformSettingsKind kind)
        {
            Array enumValues = Enum.GetValues(typeof(TextureImporterFormat));
            List<GUIContent> labels = new List<GUIContent>();
            List<int> values = new List<int>();
            HashSet<int> seenValues = new HashSet<int>();

            foreach (TextureImporterFormat format in enumValues)
            {
                int intValue = (int)format;
                if (!seenValues.Add(intValue))
                    continue;

                bool valid;
                if (kind == PlatformSettingsKind.Default)
                {
                    valid = TextureImporter.IsDefaultPlatformTextureFormatValid(imageCommonSettings.textureType, format);
                }
                else
                {
                    BuildTarget target = kind == PlatformSettingsKind.Android
                        ? BuildTarget.Android
                        : BuildTarget.StandaloneWindows64;
                    valid = TextureImporter.IsPlatformTextureFormatValid(imageCommonSettings.textureType, target, format);
                }

                if (!valid && format != current)
                    continue;

                labels.Add(new GUIContent(ObjectNames.NicifyVariableName(format.ToString())));
                values.Add(intValue);
            }

            if (labels.Count == 0)
                return current;

            int selected = EditorGUILayout.IntPopup(
                new GUIContent("Format"),
                (int)current,
                labels.ToArray(),
                values.ToArray());
            return (TextureImporterFormat)selected;
        }

        private void DrawImageSettingsTargets()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("批量目标", EditorStyles.boldLabel);
            imageSettingsIncludeSubfolders = EditorGUILayout.Toggle(
                "包含子文件夹",
                imageSettingsIncludeSubfolders);

            List<string> assetPaths = CollectSelectedPlatformAssetPaths(imageSettingsIncludeSubfolders);
            CountImageSettingsTargets(assetPaths, out int textureCount, out int atlasCount);
            EditorGUILayout.LabelField("当前选择", textureCount + " 张图片，" + atlasCount + " 个图集");

            using (new EditorGUI.DisabledScope(assetPaths.Count == 0))
            {
                if (GUILayout.Button("应用批量图像设置", GUILayout.Height(38f)))
                    ProcessBatchImageSettings(assetPaths, textureCount, atlasCount);
            }

            EditorGUILayout.EndVertical();
        }

        private static void CountImageSettingsTargets(List<string> assetPaths, out int textureCount, out int atlasCount)
        {
            textureCount = 0;
            atlasCount = 0;

            foreach (string path in assetPaths)
            {
                AssetImporter importer = AssetImporter.GetAtPath(path);
                if (importer is TextureImporter)
                    textureCount++;
                else if (IsSpriteAtlasImporter(importer))
                    atlasCount++;
            }
        }

        private void ProcessBatchImageSettings(List<string> assetPaths, int textureCount, int atlasCount)
        {
            if (assetPaths.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "没有找到图片或图集",
                    "请先选择图片、SpriteAtlas，或包含这些资源的文件夹。",
                    "确定");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "应用批量图像设置",
                "即将覆盖 " + textureCount + " 张图片的完整导入设置，并写入 " + atlasCount +
                " 个图集的平台设置。\n\n" + BuildSampleList(assetPaths),
                "应用",
                "取消");
            if (!confirmed)
                return;

            int processedTextureCount = 0;
            int processedAtlasCount = 0;
            int failedCount = 0;
            bool canceled = false;
            List<string> errorMessages = new List<string>();

            try
            {
                for (int i = 0; i < assetPaths.Count; i++)
                {
                    string path = assetPaths[i];
                    canceled = EditorUtility.DisplayCancelableProgressBar(
                        "批量图像设置",
                        (i + 1) + "/" + assetPaths.Count + "  " + Path.GetFileName(path),
                        (float)(i + 1) / assetPaths.Count);
                    if (canceled)
                        break;

                    try
                    {
                        AssetImporter importer = AssetImporter.GetAtPath(path);
                        if (importer is TextureImporter textureImporter)
                        {
                            TextureImageSettingsUtility.ApplyTextureImporter(
                                textureImporter,
                                imageCommonSettings,
                                imageDefaultSettings,
                                imageStandaloneSettings,
                                imageAndroidSettings);
                            textureImporter.SaveAndReimport();
                            processedTextureCount++;
                        }
                        else if (IsSpriteAtlasImporter(importer))
                        {
                            TextureImageSettingsUtility.ApplySpriteAtlas(
                                importer,
                                imageDefaultSettings,
                                imageStandaloneSettings,
                                imageAndroidSettings);
                            importer.SaveAndReimport();
                            processedAtlasCount++;
                        }
                    }
                    catch (Exception exception)
                    {
                        failedCount++;
                        errorMessages.Add(path + "\n" + exception.Message);
                        Debug.LogException(exception);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            string message =
                "已处理图片: " + processedTextureCount + "\n" +
                "已处理图集: " + processedAtlasCount + "\n" +
                "失败: " + failedCount;
            if (canceled)
                message += "\n\n处理已取消，部分资源尚未处理。";

            if (errorMessages.Count > 0)
            {
                int shownCount = Mathf.Min(5, errorMessages.Count);
                message += "\n\n前几条错误:\n" + string.Join(
                    "\n\n",
                    errorMessages.GetRange(0, shownCount).ToArray());
                if (errorMessages.Count > shownCount)
                    message += "\n\n其余 " + (errorMessages.Count - shownCount) + " 条请查看 Console。";
            }

            Debug.Log("[TextureTools] " + message);
            EditorUtility.DisplayDialog("批量图像设置结果", message, "确定");
        }
    }
}
#endif
