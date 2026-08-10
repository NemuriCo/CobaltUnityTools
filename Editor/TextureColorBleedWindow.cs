#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SleepyCobalt.Tools.TextureTools
{
    public sealed class TextureColorBleedWindow : EditorWindow
    {
        private enum ToolPage
        {
            ColorBleed,
            Mipmaps,
            PlatformSettings
        }

        private enum OutputMode
        {
            OverwriteSource,
            CreateBleedCopy
        }

        private enum MipmapMode
        {
            Toggle,
            Enable,
            Disable
        }

        private enum PlatformPage
        {
            Default,
            Standalone,
            Android
        }

        [Serializable]
        private sealed class PlatformTexturePreset
        {
            public bool overridden;
            public int maxTextureSize = 2048;
            public TextureResizeAlgorithm resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
            public TextureImporterFormat format = TextureImporterFormat.Automatic;
            public TextureImporterCompression compression = TextureImporterCompression.Compressed;
            public bool useCrunchCompression;
            public int compressionQuality = 50;
            public int androidEtc2FallbackOverride;
        }

        [Serializable]
        private sealed class PlatformPresetRecord
        {
            public string name;
            public PlatformTexturePreset defaultSettings = new PlatformTexturePreset();
            public PlatformTexturePreset standaloneSettings = new PlatformTexturePreset();
            public PlatformTexturePreset androidSettings = new PlatformTexturePreset();
        }

        [Serializable]
        private sealed class PlatformPresetCollection
        {
            public List<PlatformPresetRecord> presets = new List<PlatformPresetRecord>();
        }

        [SerializeField] private ToolPage currentPage = ToolPage.ColorBleed;

        [SerializeField] private int paddingPixels = 16;
        [SerializeField] private int alphaThreshold = 8;
        [SerializeField] private OutputMode outputMode = OutputMode.OverwriteSource;
        [SerializeField] private bool colorBleedIncludeSubfolders = true;
        [SerializeField] private bool skipBleedCopies = true;
        [SerializeField] private bool disableAlphaIsTransparency = true;

        [SerializeField] private MipmapMode mipmapMode = MipmapMode.Toggle;
        [SerializeField] private bool mipmapIncludeSubfolders = true;

        [SerializeField] private bool platformIncludeSubfolders = true;
        [SerializeField] private PlatformPage platformPage = PlatformPage.Default;
        [SerializeField] private PlatformTexturePreset defaultPreset = new PlatformTexturePreset();
        [SerializeField] private PlatformTexturePreset standalonePreset = new PlatformTexturePreset();
        [SerializeField] private PlatformTexturePreset androidPreset = new PlatformTexturePreset();
        [SerializeField] private string newPlatformPresetName = "新预设";
        [SerializeField] private int selectedPlatformPresetIndex;

        private const string PlatformPresetPrefsKey = "CobaltTools.TextureTools.PlatformPresets.v1";
        private PlatformPresetCollection platformPresetCollection;

        private static readonly string[] OutputModeLabels =
        {
            "覆盖原图",
            "生成 _Bleed 副本"
        };

        private static readonly string[] MipmapModeLabels =
        {
            "智能切换",
            "全部开启",
            "全部关闭"
        };

        private static readonly string[] TextureCompressionLabels =
        {
            "Low Quality",
            "Normal Quality",
            "High Quality",
            "None"
        };

        private static readonly TextureImporterCompression[] TextureCompressionValues =
        {
            TextureImporterCompression.CompressedLQ,
            TextureImporterCompression.Compressed,
            TextureImporterCompression.CompressedHQ,
            TextureImporterCompression.Uncompressed
        };

        private static readonly string[] Etc2FallbackLabels =
        {
            "Use build settings",
            "32-bit",
            "16-bit",
            "32-bit, half resolution"
        };

        private static readonly int[] Etc2FallbackValues =
        {
            0,
            1,
            2,
            3
        };

        [MenuItem("Cobalt/贴图工具", false, 20)]
        private static void OpenFromCobaltTools()
        {
            OpenWindow();
        }

        private static TextureColorBleedWindow OpenWindow()
        {
            TextureColorBleedWindow window = GetWindow<TextureColorBleedWindow>();
            window.titleContent = new GUIContent("贴图工具");
            window.minSize = new Vector2(620f, 420f);
            window.Show();
            return window;
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            DrawSidebar();
            DrawPage();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSidebar()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(150f));
            GUILayout.Space(8f);
            GUILayout.Label("贴图工具", EditorStyles.boldLabel);
            GUILayout.Space(6f);

            DrawPageButton(ToolPage.ColorBleed, "颜色溢出");
            DrawPageButton(ToolPage.Mipmaps, "Mipmap 开关");
            DrawPageButton(ToolPage.PlatformSettings, "平台压缩设置");

            GUILayout.FlexibleSpace();
            EditorGUILayout.HelpBox("先在 Project 窗口选择文件夹或贴图，再运行右侧工具。", MessageType.Info);
            EditorGUILayout.EndVertical();
        }

        private void DrawPageButton(ToolPage page, string label)
        {
            GUIStyle style = currentPage == page ? EditorStyles.toolbarButton : GUI.skin.button;
            if (GUILayout.Button(label, style, GUILayout.Height(28f)))
                currentPage = page;
        }

        private void DrawPage()
        {
            EditorGUILayout.BeginVertical();
            GUILayout.Space(8f);

            switch (currentPage)
            {
                case ToolPage.ColorBleed:
                    DrawColorBleedPage();
                    break;
                case ToolPage.Mipmaps:
                    DrawMipmapPage();
                    break;
                case ToolPage.PlatformSettings:
                    DrawPlatformSettingsPage();
                    break;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawColorBleedPage()
        {
            EditorGUILayout.LabelField("颜色溢出", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "把可见像素的 RGB 颜色扩展到透明区域，然后把整张 PNG 的 Alpha 清为 255。" +
                "适合减少双线性过滤、Mipmap 或压缩造成的透明边缘黑线、接缝。",
                MessageType.Info);

            paddingPixels = EditorGUILayout.IntSlider(
                new GUIContent("溢出距离", "向透明区域扩展多少像素。"),
                paddingPixels,
                1,
                512);

            alphaThreshold = EditorGUILayout.IntSlider(
                new GUIContent("Alpha 阈值", "Alpha 大于该值的像素会作为颜色源。"),
                alphaThreshold,
                0,
                254);

            outputMode = (OutputMode)EditorGUILayout.Popup(new GUIContent("输出方式"), (int)outputMode, OutputModeLabels);
            colorBleedIncludeSubfolders = EditorGUILayout.Toggle(new GUIContent("包含子文件夹"), colorBleedIncludeSubfolders);
            skipBleedCopies = EditorGUILayout.Toggle(new GUIContent("跳过 _Bleed 文件"), skipBleedCopies);
            disableAlphaIsTransparency = EditorGUILayout.Toggle(
                new GUIContent("关闭 Alpha 导入", "处理后关闭 Alpha Is Transparency 和 Alpha Source，避免 Unity 再做额外透明边处理。"),
                disableAlphaIsTransparency);

            GUILayout.Space(8f);

            if (outputMode == OutputMode.OverwriteSource)
            {
                EditorGUILayout.HelpBox(
                    "覆盖原图会直接改写源 PNG。现有 .meta 和引用会保留，但图片文件内容会被重写。",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "生成副本会在原图旁边创建 *_Bleed.png，并复制原图的导入设置。",
                    MessageType.None);
            }

            List<string> selectedPngs = CollectSelectedPngPaths(colorBleedIncludeSubfolders);
            DrawSelectionSummary(selectedPngs.Count, "PNG 贴图");

            using (new EditorGUI.DisabledScope(selectedPngs.Count == 0))
            {
                if (GUILayout.Button("处理选中的 PNG", GUILayout.Height(36f)))
                    ProcessColorBleed(selectedPngs);
            }
        }

        private void DrawMipmapPage()
        {
            EditorGUILayout.LabelField("Mipmap 开关", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "批量修改选中贴图的 Mipmap 导入设置。开启“包含子文件夹”后，选中文件夹会递归扫描所有子层级。",
                MessageType.Info);

            mipmapMode = (MipmapMode)EditorGUILayout.Popup(new GUIContent("模式"), (int)mipmapMode, MipmapModeLabels);
            mipmapIncludeSubfolders = EditorGUILayout.Toggle(new GUIContent("包含子文件夹"), mipmapIncludeSubfolders);

            EditorGUILayout.Space(6f);
            EditorGUILayout.HelpBox(
                "智能切换：如果选中范围里有任意贴图未开启 Mipmap，则全部开启；否则全部关闭。",
                MessageType.None);

            List<string> texturePaths = CollectSelectedTexturePaths(mipmapIncludeSubfolders);
            int enabledCount = CountMipmaps(texturePaths, true);
            int disabledCount = texturePaths.Count - enabledCount;

            DrawSelectionSummary(texturePaths.Count, "贴图");
            EditorGUILayout.LabelField("已开启 Mipmap", enabledCount.ToString());
            EditorGUILayout.LabelField("已关闭 Mipmap", disabledCount.ToString());

            using (new EditorGUI.DisabledScope(texturePaths.Count == 0))
            {
                if (GUILayout.Button("应用 Mipmap 设置", GUILayout.Height(36f)))
                    ProcessMipmaps(texturePaths);
            }
        }

        private void DrawPlatformSettingsPage()
        {
            EditorGUILayout.LabelField("平台压缩设置", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "批量设置选中文件夹及子层级贴图的 Default、PC 和 Android 导入参数。每个平台按页签分别配置，点击应用后会一次性写入三个平台。",
                MessageType.Info);

            DrawPlatformPresetControls();

            EditorGUILayout.Space(8f);
            platformIncludeSubfolders = EditorGUILayout.Toggle(new GUIContent("包含子文件夹"), platformIncludeSubfolders);

            EditorGUILayout.Space(6f);
            platformPage = (PlatformPage)GUILayout.Toolbar(
                (int)platformPage,
                new[] { "Default", "PC", "Android" },
                GUILayout.Height(24f));

            EditorGUILayout.Space(8f);

            switch (platformPage)
            {
                case PlatformPage.Default:
                    DrawDefaultPlatformPreset(defaultPreset);
                    break;
                case PlatformPage.Standalone:
                    DrawStandalonePlatformPreset(standalonePreset);
                    break;
                case PlatformPage.Android:
                    DrawAndroidPlatformPreset(androidPreset);
                    break;
            }

            List<string> assetPaths = CollectSelectedPlatformAssetPaths(platformIncludeSubfolders);
            DrawSelectionSummary(assetPaths.Count, "贴图/图集");

            using (new EditorGUI.DisabledScope(assetPaths.Count == 0))
            {
                if (GUILayout.Button("应用 Default + PC + Android 设置", GUILayout.Height(36f)))
                    ProcessPlatformSettings(assetPaths);
            }
        }

        private static void DrawDefaultPlatformPreset(PlatformTexturePreset preset)
        {
            DrawPlatformSizeResizeAndFormat(preset);

            preset.compression = DrawTextureCompressionPopup(
                new GUIContent("Compression"),
                preset.compression);

            preset.useCrunchCompression = EditorGUILayout.Toggle(
                new GUIContent("Use Crunch Compression"),
                preset.useCrunchCompression);

            if (preset.useCrunchCompression)
            {
                preset.compressionQuality = EditorGUILayout.IntSlider(
                    new GUIContent("Compressor Quality"),
                    preset.compressionQuality,
                    0,
                    100);
            }
        }

        private static void DrawStandalonePlatformPreset(PlatformTexturePreset preset)
        {
            preset.overridden = EditorGUILayout.Toggle(
                new GUIContent("Override For Windows, Mac, Linux"),
                preset.overridden);

            using (new EditorGUI.DisabledScope(!preset.overridden))
                DrawPlatformSizeResizeAndFormat(preset);
        }

        private static void DrawAndroidPlatformPreset(PlatformTexturePreset preset)
        {
            preset.overridden = EditorGUILayout.Toggle(
                new GUIContent("Override For Android"),
                preset.overridden);

            using (new EditorGUI.DisabledScope(!preset.overridden))
            {
                DrawPlatformSizeResizeAndFormat(preset);

                preset.compression = DrawTextureCompressionPopup(
                    new GUIContent("Compressor Quality"),
                    preset.compression);

                preset.androidEtc2FallbackOverride = EditorGUILayout.IntPopup(
                    "Override ETC2 fallback",
                    preset.androidEtc2FallbackOverride,
                    Etc2FallbackLabels,
                    Etc2FallbackValues);
            }
        }

        private void DrawPlatformPresetControls()
        {
            EnsurePlatformPresetsLoaded();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("预设", EditorStyles.boldLabel);

            string[] presetNames = GetPlatformPresetNames();
            if (presetNames.Length > 0)
            {
                selectedPlatformPresetIndex = Mathf.Clamp(selectedPlatformPresetIndex, 0, presetNames.Length - 1);
                selectedPlatformPresetIndex = EditorGUILayout.Popup(
                    new GUIContent("选择预设"),
                    selectedPlatformPresetIndex,
                    presetNames);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("加载预设"))
                    LoadSelectedPlatformPreset();

                if (GUILayout.Button("删除预设"))
                    DeleteSelectedPlatformPreset();

                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox("还没有自定义预设。可以先调整下面的参数，再保存为预设。", MessageType.None);
            }

            EditorGUILayout.Space(4f);
            newPlatformPresetName = EditorGUILayout.TextField("预设名称", newPlatformPresetName);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("保存当前为预设"))
                SaveCurrentPlatformPreset();

            if (GUILayout.Button("载入推荐小体积预设"))
                LoadCompactGradientPreset();

            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("载入 UI 图集小体积预设"))
                LoadCompactUiAtlasPreset();

            EditorGUILayout.EndVertical();
        }

        private static void DrawPlatformSizeResizeAndFormat(PlatformTexturePreset preset)
        {
            preset.maxTextureSize = EditorGUILayout.IntPopup(
                "Max Size",
                preset.maxTextureSize,
                new[] { "32", "64", "128", "256", "512", "1024", "2048", "4096", "8192" },
                new[] { 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192 });

            preset.resizeAlgorithm = (TextureResizeAlgorithm)EditorGUILayout.EnumPopup(
                new GUIContent("Resize Algorithm"),
                preset.resizeAlgorithm);

            preset.format = (TextureImporterFormat)EditorGUILayout.EnumPopup(
                new GUIContent("Format"),
                preset.format);
        }

        private static TextureImporterCompression DrawTextureCompressionPopup(
            GUIContent label,
            TextureImporterCompression currentValue)
        {
            int selectedIndex = Array.IndexOf(TextureCompressionValues, currentValue);
            if (selectedIndex < 0)
                selectedIndex = 1;

            selectedIndex = EditorGUILayout.Popup(label, selectedIndex, TextureCompressionLabels);
            return TextureCompressionValues[Mathf.Clamp(selectedIndex, 0, TextureCompressionValues.Length - 1)];
        }

        private void EnsurePlatformPresetsLoaded()
        {
            if (platformPresetCollection != null)
                return;

            string json = EditorPrefs.GetString(PlatformPresetPrefsKey, string.Empty);
            if (!string.IsNullOrEmpty(json))
                platformPresetCollection = JsonUtility.FromJson<PlatformPresetCollection>(json);

            if (platformPresetCollection == null)
                platformPresetCollection = new PlatformPresetCollection();

            if (platformPresetCollection.presets == null)
                platformPresetCollection.presets = new List<PlatformPresetRecord>();
        }

        private void SavePlatformPresets()
        {
            EnsurePlatformPresetsLoaded();
            string json = JsonUtility.ToJson(platformPresetCollection);
            EditorPrefs.SetString(PlatformPresetPrefsKey, json);
        }

        private string[] GetPlatformPresetNames()
        {
            EnsurePlatformPresetsLoaded();
            string[] names = new string[platformPresetCollection.presets.Count];
            for (int i = 0; i < names.Length; i++)
                names[i] = platformPresetCollection.presets[i].name;

            return names;
        }

        private void SaveCurrentPlatformPreset()
        {
            EnsurePlatformPresetsLoaded();

            string trimmedPresetName = string.IsNullOrEmpty(newPlatformPresetName)
                ? string.Empty
                : newPlatformPresetName.Trim();

            string presetName = string.IsNullOrEmpty(trimmedPresetName)
                ? "未命名预设"
                : trimmedPresetName;

            PlatformPresetRecord record = platformPresetCollection.presets.Find(p => p.name == presetName);
            if (record == null)
            {
                record = new PlatformPresetRecord();
                platformPresetCollection.presets.Add(record);
                selectedPlatformPresetIndex = platformPresetCollection.presets.Count - 1;
            }

            record.name = presetName;
            record.defaultSettings = CopyPlatformPreset(defaultPreset);
            record.standaloneSettings = CopyPlatformPreset(standalonePreset);
            record.androidSettings = CopyPlatformPreset(androidPreset);

            SavePlatformPresets();
            EditorUtility.DisplayDialog("保存预设", "已保存预设: " + presetName, "确定");
        }

        private void LoadSelectedPlatformPreset()
        {
            EnsurePlatformPresetsLoaded();
            if (platformPresetCollection.presets.Count == 0)
                return;

            selectedPlatformPresetIndex = Mathf.Clamp(
                selectedPlatformPresetIndex,
                0,
                platformPresetCollection.presets.Count - 1);

            ApplyPlatformPresetRecord(platformPresetCollection.presets[selectedPlatformPresetIndex]);
        }

        private void DeleteSelectedPlatformPreset()
        {
            EnsurePlatformPresetsLoaded();
            if (platformPresetCollection.presets.Count == 0)
                return;

            selectedPlatformPresetIndex = Mathf.Clamp(
                selectedPlatformPresetIndex,
                0,
                platformPresetCollection.presets.Count - 1);

            string presetName = platformPresetCollection.presets[selectedPlatformPresetIndex].name;
            bool confirmed = EditorUtility.DisplayDialog(
                "删除预设",
                "确定删除预设“" + presetName + "”？",
                "删除",
                "取消");

            if (!confirmed)
                return;

            platformPresetCollection.presets.RemoveAt(selectedPlatformPresetIndex);
            selectedPlatformPresetIndex = Mathf.Clamp(selectedPlatformPresetIndex, 0, platformPresetCollection.presets.Count - 1);
            SavePlatformPresets();
        }

        private void LoadCompactGradientPreset()
        {
            defaultPreset.maxTextureSize = 1024;
            defaultPreset.resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
            defaultPreset.format = TextureImporterFormat.Automatic;
            defaultPreset.compression = TextureImporterCompression.CompressedLQ;
            defaultPreset.useCrunchCompression = true;
            defaultPreset.compressionQuality = 50;

            standalonePreset.overridden = true;
            standalonePreset.maxTextureSize = 1024;
            standalonePreset.resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
            standalonePreset.format = TextureImporterFormat.Automatic;

            androidPreset.overridden = true;
            androidPreset.maxTextureSize = 1024;
            androidPreset.resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
            androidPreset.format = TextureImporterFormat.ASTC_8x8;
            androidPreset.compression = TextureImporterCompression.Compressed;
            androidPreset.androidEtc2FallbackOverride = 0;

            newPlatformPresetName = "纯色渐变小体积";
        }

        private void LoadCompactUiAtlasPreset()
        {
            defaultPreset.maxTextureSize = 1024;
            defaultPreset.resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
            defaultPreset.format = TextureImporterFormat.Automatic;
            defaultPreset.compression = TextureImporterCompression.Compressed;
            defaultPreset.useCrunchCompression = false;
            defaultPreset.compressionQuality = 50;

            standalonePreset.overridden = true;
            standalonePreset.maxTextureSize = 1024;
            standalonePreset.resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
            standalonePreset.format = TextureImporterFormat.Automatic;

            androidPreset.overridden = true;
            androidPreset.maxTextureSize = 1024;
            androidPreset.resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
            androidPreset.format = TextureImporterFormat.ASTC_8x8;
            androidPreset.compression = TextureImporterCompression.Compressed;
            androidPreset.androidEtc2FallbackOverride = 0;

            newPlatformPresetName = "UI图集小体积";
        }

        private void ApplyPlatformPresetRecord(PlatformPresetRecord record)
        {
            if (record == null)
                return;

            defaultPreset = CopyPlatformPreset(record.defaultSettings);
            standalonePreset = CopyPlatformPreset(record.standaloneSettings);
            androidPreset = CopyPlatformPreset(record.androidSettings);
            newPlatformPresetName = record.name;
        }

        private static PlatformTexturePreset CopyPlatformPreset(PlatformTexturePreset source)
        {
            PlatformTexturePreset copy = new PlatformTexturePreset();
            if (source == null)
                return copy;

            copy.overridden = source.overridden;
            copy.maxTextureSize = source.maxTextureSize;
            copy.resizeAlgorithm = source.resizeAlgorithm;
            copy.format = source.format;
            copy.compression = source.compression;
            copy.useCrunchCompression = source.useCrunchCompression;
            copy.compressionQuality = source.compressionQuality;
            copy.androidEtc2FallbackOverride = source.androidEtc2FallbackOverride;
            return copy;
        }

        private static void DrawSelectionSummary(int count, string label)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("当前选择", count + " 个" + label);
        }

        private void ProcessColorBleed(List<string> sourcePaths)
        {
            if (sourcePaths.Count == 0)
            {
                EditorUtility.DisplayDialog("没有找到 PNG 贴图", "请先选择 PNG 贴图，或选择包含 PNG 贴图的文件夹。", "确定");
                return;
            }

            if (outputMode == OutputMode.OverwriteSource)
            {
                bool confirmed = EditorUtility.DisplayDialog(
                    "确认覆盖源 PNG",
                    "即将直接改写 " + sourcePaths.Count + " 张 PNG。\n\n" + BuildSampleList(sourcePaths),
                    "开始处理",
                    "取消");

                if (!confirmed)
                    return;
            }

            int successCount = 0;
            int skippedCount = 0;
            int failedCount = 0;
            bool canceled = false;
            List<string> errorMessages = new List<string>();

            try
            {
                for (int i = 0; i < sourcePaths.Count; i++)
                {
                    string sourcePath = sourcePaths[i];
                    canceled = EditorUtility.DisplayCancelableProgressBar(
                        "颜色溢出",
                        (i + 1) + "/" + sourcePaths.Count + "  " + sourcePath,
                        (float)i / sourcePaths.Count);

                    if (canceled)
                        break;

                    try
                    {
                        string outputPath = sourcePath;

                        if (outputMode == OutputMode.CreateBleedCopy)
                        {
                            outputPath = CreateBleedCopyPath(sourcePath);
                            if (!AssetDatabase.CopyAsset(sourcePath, outputPath))
                                throw new IOException("无法创建副本: " + outputPath);
                        }

                        ProcessResult result = ProcessPng(sourcePath, outputPath);
                        if (result == ProcessResult.Skipped)
                        {
                            skippedCount++;
                            if (outputMode == OutputMode.CreateBleedCopy)
                                AssetDatabase.DeleteAsset(outputPath);
                            continue;
                        }

                        AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

                        if (disableAlphaIsTransparency)
                            DisableAlphaTransparencyProcessing(outputPath);

                        successCount++;
                    }
                    catch (Exception exception)
                    {
                        failedCount++;
                        errorMessages.Add(sourcePath + "\n" + exception.Message);
                        Debug.LogException(exception);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }

            string message =
                "已处理: " + successCount + "\n" +
                "已跳过: " + skippedCount + "\n" +
                "失败: " + failedCount;

            if (canceled)
                message += "\n\n处理已取消，部分贴图尚未处理。";

            if (errorMessages.Count > 0)
            {
                int shownCount = Mathf.Min(5, errorMessages.Count);
                message += "\n\n前几条错误:\n" + string.Join("\n\n", errorMessages.GetRange(0, shownCount).ToArray());
                if (errorMessages.Count > shownCount)
                    message += "\n\n其余 " + (errorMessages.Count - shownCount) + " 条请查看 Console。";
            }

            EditorUtility.DisplayDialog("颜色溢出结果", message, "确定");
        }

        private void ProcessMipmaps(List<string> texturePaths)
        {
            if (texturePaths.Count == 0)
            {
                EditorUtility.DisplayDialog("没有找到贴图", "请先选择一个或多个文件夹，或选择贴图资源。", "确定");
                return;
            }

            bool enable = GetTargetMipmapState(texturePaths);
            int changeCount = CountMipmapChanges(texturePaths, enable);

            if (changeCount == 0)
            {
                EditorUtility.DisplayDialog(
                    "Mipmap 开关",
                    texturePaths.Count + " 张贴图已经全部" + (enable ? "开启" : "关闭") + " Mipmap。",
                    "确定");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "应用 Mipmap 设置",
                "将 " + changeCount + " / " + texturePaths.Count + " 张选中贴图的 Mipmap 设置为" + (enable ? "开启" : "关闭") + "？\n\n" +
                BuildSampleList(texturePaths),
                "应用",
                "取消");

            if (!confirmed)
                return;

            int processedCount = 0;
            bool canceled = false;

            try
            {
                for (int i = 0; i < texturePaths.Count; i++)
                {
                    string path = texturePaths[i];
                    canceled = EditorUtility.DisplayCancelableProgressBar(
                        "Mipmap 开关",
                        (i + 1) + "/" + texturePaths.Count + "  " + Path.GetFileName(path),
                        (float)(i + 1) / texturePaths.Count);

                    if (canceled)
                        break;

                    TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null || importer.mipmapEnabled == enable)
                        continue;

                    importer.mipmapEnabled = enable;
                    importer.SaveAndReimport();
                    processedCount++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            string result = "已为 " + processedCount + " 张贴图" + (enable ? "开启" : "关闭") + " Mipmap。";
            if (canceled)
                result += "\n\n处理已取消，部分贴图尚未处理。";

            Debug.Log("[TextureTools] " + result);
            EditorUtility.DisplayDialog("Mipmap 处理结果", result, "确定");
        }

        private void ProcessPlatformSettings(List<string> assetPaths)
        {
            if (assetPaths.Count == 0)
            {
                EditorUtility.DisplayDialog("没有找到贴图或图集", "请先选择一个或多个文件夹，或选择贴图/图集资源。", "确定");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "应用平台压缩设置",
                "即将为 " + assetPaths.Count + " 个贴图/图集资源写入 Default、PC 和 Android 平台导入设置。\n\n" +
                BuildSampleList(assetPaths),
                "应用",
                "取消");

            if (!confirmed)
                return;

            int processedCount = 0;
            int failedCount = 0;
            bool canceled = false;
            List<string> errorMessages = new List<string>();

            try
            {
                for (int i = 0; i < assetPaths.Count; i++)
                {
                    string path = assetPaths[i];
                    canceled = EditorUtility.DisplayCancelableProgressBar(
                        "平台压缩设置",
                        (i + 1) + "/" + assetPaths.Count + "  " + Path.GetFileName(path),
                        (float)(i + 1) / assetPaths.Count);

                    if (canceled)
                        break;

                    try
                    {
                        AssetImporter importer = AssetImporter.GetAtPath(path);
                        if (importer is TextureImporter textureImporter)
                        {
                            ApplyDefaultTextureSettings(textureImporter, defaultPreset);
                            ApplyStandaloneTextureSettings(textureImporter, standalonePreset);
                            ApplyAndroidTextureSettings(textureImporter, androidPreset);

                            textureImporter.SaveAndReimport();
                            processedCount++;
                            continue;
                        }

                        if (IsSpriteAtlasImporter(importer))
                        {
                            ApplyDefaultAtlasSettings(importer, defaultPreset);
                            ApplyStandaloneAtlasSettings(importer, standalonePreset);
                            ApplyAndroidAtlasSettings(importer, androidPreset);

                            importer.SaveAndReimport();
                            processedCount++;
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
                "已处理: " + processedCount + "\n" +
                "失败: " + failedCount;

            if (canceled)
                message += "\n\n处理已取消，部分贴图尚未处理。";

            if (errorMessages.Count > 0)
            {
                int shownCount = Mathf.Min(5, errorMessages.Count);
                message += "\n\n前几条错误:\n" + string.Join("\n\n", errorMessages.GetRange(0, shownCount).ToArray());
                if (errorMessages.Count > shownCount)
                    message += "\n\n其余 " + (errorMessages.Count - shownCount) + " 条请查看 Console。";
            }

            EditorUtility.DisplayDialog("平台压缩设置结果", message, "确定");
        }

        private static void ApplyDefaultTextureSettings(TextureImporter importer, PlatformTexturePreset preset)
        {
            importer.maxTextureSize = preset.maxTextureSize;
            importer.textureCompression = preset.compression;
            importer.crunchedCompression = preset.useCrunchCompression;
            importer.compressionQuality = preset.compressionQuality;

            TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings("DefaultTexturePlatform");
            settings.name = "DefaultTexturePlatform";
            settings.overridden = false;
            settings.maxTextureSize = preset.maxTextureSize;
            settings.resizeAlgorithm = preset.resizeAlgorithm;
            settings.format = preset.format;
            settings.textureCompression = preset.compression;
            settings.crunchedCompression = preset.useCrunchCompression;
            settings.compressionQuality = preset.compressionQuality;
            importer.SetPlatformTextureSettings(settings);
        }

        private static void ApplyStandaloneTextureSettings(TextureImporter importer, PlatformTexturePreset preset)
        {
            TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings("Standalone");
            settings.name = "Standalone";
            settings.overridden = preset.overridden;
            settings.maxTextureSize = preset.maxTextureSize;
            settings.resizeAlgorithm = preset.resizeAlgorithm;
            settings.format = preset.format;
            importer.SetPlatformTextureSettings(settings);
        }

        private static void ApplyAndroidTextureSettings(TextureImporter importer, PlatformTexturePreset preset)
        {
            TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings("Android");
            settings.name = "Android";
            settings.overridden = preset.overridden;
            settings.maxTextureSize = preset.maxTextureSize;
            settings.resizeAlgorithm = preset.resizeAlgorithm;
            settings.format = preset.format;
            settings.textureCompression = preset.compression;
            settings.compressionQuality = GetCompressionQualityValue(preset.compression);
            SetAndroidEtc2FallbackOverride(settings, preset.androidEtc2FallbackOverride);
            importer.SetPlatformTextureSettings(settings);
        }

        private static void ApplyDefaultAtlasSettings(AssetImporter importer, PlatformTexturePreset preset)
        {
            TextureImporterPlatformSettings settings = GetAtlasPlatformSettings(importer, "DefaultTexturePlatform");
            settings.name = "DefaultTexturePlatform";
            settings.overridden = false;
            settings.maxTextureSize = preset.maxTextureSize;
            settings.resizeAlgorithm = preset.resizeAlgorithm;
            settings.format = preset.format;
            settings.textureCompression = preset.compression;
            settings.crunchedCompression = preset.useCrunchCompression;
            settings.compressionQuality = preset.compressionQuality;
            SetAtlasPlatformSettings(importer, settings);
        }

        private static void ApplyStandaloneAtlasSettings(AssetImporter importer, PlatformTexturePreset preset)
        {
            TextureImporterPlatformSettings settings = GetAtlasPlatformSettings(importer, "Standalone");
            settings.name = "Standalone";
            settings.overridden = preset.overridden;
            settings.maxTextureSize = preset.maxTextureSize;
            settings.resizeAlgorithm = preset.resizeAlgorithm;
            settings.format = preset.format;
            settings.textureCompression = preset.compression;
            settings.compressionQuality = GetCompressionQualityValue(preset.compression);
            SetAtlasPlatformSettings(importer, settings);
        }

        private static void ApplyAndroidAtlasSettings(AssetImporter importer, PlatformTexturePreset preset)
        {
            TextureImporterPlatformSettings settings = GetAtlasPlatformSettings(importer, "Android");
            settings.name = "Android";
            settings.overridden = preset.overridden;
            settings.maxTextureSize = preset.maxTextureSize;
            settings.resizeAlgorithm = preset.resizeAlgorithm;
            settings.format = preset.format;
            settings.textureCompression = preset.compression;
            settings.compressionQuality = GetCompressionQualityValue(preset.compression);
            SetAndroidEtc2FallbackOverride(settings, preset.androidEtc2FallbackOverride);
            SetAtlasPlatformSettings(importer, settings);
        }

        private static TextureImporterPlatformSettings GetAtlasPlatformSettings(AssetImporter importer, string platformName)
        {
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

        private static int GetCompressionQualityValue(TextureImporterCompression compression)
        {
            switch (compression)
            {
                case TextureImporterCompression.CompressedLQ:
                    return 0;
                case TextureImporterCompression.CompressedHQ:
                    return 100;
                case TextureImporterCompression.Compressed:
                    return 50;
                default:
                    return 50;
            }
        }

        private static void SetAndroidEtc2FallbackOverride(TextureImporterPlatformSettings settings, int value)
        {
            var property = typeof(TextureImporterPlatformSettings).GetProperty("androidETC2FallbackOverride");
            if (property == null || !property.CanWrite)
                return;

            object convertedValue = Enum.ToObject(property.PropertyType, value);
            property.SetValue(settings, convertedValue, null);
        }

        private bool GetTargetMipmapState(List<string> texturePaths)
        {
            if (mipmapMode == MipmapMode.Enable)
                return true;

            if (mipmapMode == MipmapMode.Disable)
                return false;

            foreach (string path in texturePaths)
            {
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null && !importer.mipmapEnabled)
                    return true;
            }

            return false;
        }

        private List<string> CollectSelectedPngPaths(bool includeSubfolders)
        {
            HashSet<string> uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in CollectSelectedAssetPaths("t:Texture2D", includeSubfolders))
                AddPngIfSupported(path, uniquePaths);

            List<string> paths = new List<string>(uniquePaths);
            paths.Sort(StringComparer.OrdinalIgnoreCase);
            return paths;
        }

        private List<string> CollectSelectedTexturePaths(bool includeSubfolders)
        {
            HashSet<string> uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in CollectSelectedAssetPaths("t:Texture", includeSubfolders))
            {
                if (AssetImporter.GetAtPath(path) is TextureImporter)
                    uniquePaths.Add(NormalizePath(path));
            }

            List<string> paths = new List<string>(uniquePaths);
            paths.Sort(StringComparer.OrdinalIgnoreCase);
            return paths;
        }

        private List<string> CollectSelectedPlatformAssetPaths(bool includeSubfolders)
        {
            HashSet<string> uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in CollectSelectedAssetPaths(string.Empty, includeSubfolders))
            {
                AssetImporter importer = AssetImporter.GetAtPath(path);
                if (importer is TextureImporter || IsSpriteAtlasImporter(importer))
                    uniquePaths.Add(NormalizePath(path));
            }

            List<string> paths = new List<string>(uniquePaths);
            paths.Sort(StringComparer.OrdinalIgnoreCase);
            return paths;
        }

        private static bool IsSpriteAtlasImporter(AssetImporter importer)
        {
            if (importer == null)
                return false;

            string assetPath = NormalizePath(importer.assetPath);
            if (assetPath.EndsWith(".spriteatlas", StringComparison.OrdinalIgnoreCase) ||
                assetPath.EndsWith(".spriteatlasv2", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return importer.GetType().Name.IndexOf("SpriteAtlas", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static IEnumerable<string> CollectSelectedAssetPaths(string filter, bool includeSubfolders)
        {
            foreach (string guid in Selection.assetGUIDs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path))
                    continue;

                path = NormalizePath(path);

                if (AssetDatabase.IsValidFolder(path))
                {
                    string[] guids = AssetDatabase.FindAssets(filter, new[] { path });
                    foreach (string childGuid in guids)
                    {
                        string childPath = NormalizePath(AssetDatabase.GUIDToAssetPath(childGuid));
                        if (!includeSubfolders &&
                            !string.Equals(NormalizePath(Path.GetDirectoryName(childPath)), path, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        yield return childPath;
                    }
                }
                else
                {
                    yield return path;
                }
            }
        }

        private void AddPngIfSupported(string assetPath, HashSet<string> output)
        {
            if (string.IsNullOrEmpty(assetPath))
                return;

            assetPath = NormalizePath(assetPath);

            if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return;

            if (!string.Equals(Path.GetExtension(assetPath), ".png", StringComparison.OrdinalIgnoreCase))
                return;

            if (skipBleedCopies &&
                Path.GetFileNameWithoutExtension(assetPath).EndsWith("_Bleed", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            output.Add(assetPath);
        }

        private static int CountMipmaps(List<string> texturePaths, bool enabled)
        {
            int count = 0;
            foreach (string path in texturePaths)
            {
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null && importer.mipmapEnabled == enabled)
                    count++;
            }

            return count;
        }

        private static int CountMipmapChanges(List<string> texturePaths, bool enable)
        {
            int count = 0;
            foreach (string path in texturePaths)
            {
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null && importer.mipmapEnabled != enable)
                    count++;
            }

            return count;
        }

        private static string BuildSampleList(List<string> paths)
        {
            const int maxSamples = 12;
            int displayCount = Mathf.Min(paths.Count, maxSamples);
            List<string> lines = new List<string>(displayCount + 1);

            for (int i = 0; i < displayCount; i++)
                lines.Add(paths[i]);

            if (paths.Count > displayCount)
                lines.Add("...以及另外 " + (paths.Count - displayCount) + " 个");

            return string.Join("\n", lines.ToArray());
        }

        private static string CreateBleedCopyPath(string sourcePath)
        {
            string directory = NormalizePath(Path.GetDirectoryName(sourcePath));
            string fileName = Path.GetFileNameWithoutExtension(sourcePath);
            string candidate = directory + "/" + fileName + "_Bleed.png";
            return AssetDatabase.GenerateUniqueAssetPath(candidate);
        }

        private ProcessResult ProcessPng(string sourceAssetPath, string outputAssetPath)
        {
            string sourceFullPath = Path.GetFullPath(sourceAssetPath);
            string outputFullPath = Path.GetFullPath(outputAssetPath);

            if (!File.Exists(sourceFullPath))
                throw new FileNotFoundException("找不到源 PNG。", sourceFullPath);

            byte[] sourceBytes = File.ReadAllBytes(sourceFullPath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
            {
                name = Path.GetFileNameWithoutExtension(sourceAssetPath)
            };

            try
            {
                if (!ImageConversion.LoadImage(texture, sourceBytes, false))
                    throw new InvalidDataException("PNG 解码失败。");

                Color32[] pixels = texture.GetPixels32();
                ProcessResult result = DilateRgbAndClearAlpha(
                    pixels,
                    texture.width,
                    texture.height,
                    paddingPixels,
                    (byte)alphaThreshold);

                if (result == ProcessResult.Skipped)
                    return result;

                texture.SetPixels32(pixels);
                texture.Apply(false, false);

                byte[] outputBytes = ImageConversion.EncodeToPNG(texture);
                if (outputBytes == null || outputBytes.Length == 0)
                    throw new InvalidDataException("PNG 编码失败。");

                string outputDirectory = Path.GetDirectoryName(outputFullPath);
                if (!string.IsNullOrEmpty(outputDirectory))
                    Directory.CreateDirectory(outputDirectory);

                File.WriteAllBytes(outputFullPath, outputBytes);
                return ProcessResult.Processed;
            }
            finally
            {
                DestroyImmediate(texture);
            }
        }

        private static ProcessResult DilateRgbAndClearAlpha(
            Color32[] pixels,
            int width,
            int height,
            int paddingPixels,
            byte alphaThreshold)
        {
            if (pixels == null || pixels.Length != width * height)
                throw new ArgumentException("像素数据尺寸不正确。");

            int pixelCount = pixels.Length;
            int[] distance = new int[pixelCount];
            int[] queue = new int[pixelCount];

            const int Unvisited = -1;
            int queueHead = 0;
            int queueTail = 0;
            int transparentOrEdgeCount = 0;

            for (int i = 0; i < pixelCount; i++)
            {
                if (pixels[i].a > alphaThreshold)
                {
                    distance[i] = 0;
                    queue[queueTail++] = i;
                }
                else
                {
                    distance[i] = Unvisited;
                    transparentOrEdgeCount++;
                }
            }

            if (transparentOrEdgeCount == 0 || queueTail == 0)
                return ProcessResult.Skipped;

            int maxDistance = Mathf.Clamp(paddingPixels, 1, 512);

            while (queueHead < queueTail)
            {
                int index = queue[queueHead++];
                int currentDistance = distance[index];

                if (currentDistance >= maxDistance)
                    continue;

                int x = index % width;
                int y = index / width;
                int nextDistance = currentDistance + 1;

                TrySpread(index, x - 1, y - 1, width, height, pixels, distance, queue, ref queueTail, nextDistance);
                TrySpread(index, x, y - 1, width, height, pixels, distance, queue, ref queueTail, nextDistance);
                TrySpread(index, x + 1, y - 1, width, height, pixels, distance, queue, ref queueTail, nextDistance);
                TrySpread(index, x - 1, y, width, height, pixels, distance, queue, ref queueTail, nextDistance);
                TrySpread(index, x + 1, y, width, height, pixels, distance, queue, ref queueTail, nextDistance);
                TrySpread(index, x - 1, y + 1, width, height, pixels, distance, queue, ref queueTail, nextDistance);
                TrySpread(index, x, y + 1, width, height, pixels, distance, queue, ref queueTail, nextDistance);
                TrySpread(index, x + 1, y + 1, width, height, pixels, distance, queue, ref queueTail, nextDistance);
            }

            for (int i = 0; i < pixelCount; i++)
            {
                Color32 color = pixels[i];
                color.a = 255;
                pixels[i] = color;
            }

            return ProcessResult.Processed;
        }

        private static void TrySpread(
            int sourceIndex,
            int targetX,
            int targetY,
            int width,
            int height,
            Color32[] pixels,
            int[] distance,
            int[] queue,
            ref int queueTail,
            int nextDistance)
        {
            if ((uint)targetX >= (uint)width || (uint)targetY >= (uint)height)
                return;

            int targetIndex = targetY * width + targetX;
            if (distance[targetIndex] != -1)
                return;

            Color32 sourceColor = pixels[sourceIndex];
            pixels[targetIndex] = new Color32(sourceColor.r, sourceColor.g, sourceColor.b, pixels[targetIndex].a);
            distance[targetIndex] = nextDistance;
            queue[queueTail++] = targetIndex;
        }

        private static void DisableAlphaTransparencyProcessing(string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                return;

            bool changed = false;

            if (importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = false;
                changed = true;
            }

            if (importer.alphaSource != TextureImporterAlphaSource.None)
            {
                importer.alphaSource = TextureImporterAlphaSource.None;
                changed = true;
            }

            if (changed)
                importer.SaveAndReimport();
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }

        private enum ProcessResult
        {
            Processed,
            Skipped
        }
    }
}
#endif
