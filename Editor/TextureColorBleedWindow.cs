#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SleepyCobalt.Tools.TextureTools
{
    public sealed partial class TextureColorBleedWindow : EditorWindow
    {
        private enum ToolPage
        {
            BatchImageSettings,
            ColorBleed
        }

        private enum OutputMode
        {
            OverwriteSource,
            CreateBleedCopy
        }

        [SerializeField] private ToolPage currentPage = ToolPage.BatchImageSettings;

        [SerializeField] private int paddingPixels = 16;
        [SerializeField] private int alphaThreshold = 8;
        [SerializeField] private OutputMode outputMode = OutputMode.OverwriteSource;
        [SerializeField] private bool colorBleedIncludeSubfolders = true;
        [SerializeField] private bool skipBleedCopies = true;
        [SerializeField] private bool disableAlphaIsTransparency = true;

        private const string PlatformPresetPrefsKey = "CobaltTools.TextureTools.PlatformPresets.v1";

        private static readonly string[] OutputModeLabels =
        {
            "覆盖原图",
            "生成 _Bleed 副本"
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

        [MenuItem("CobaltTools/贴图工具", false, 20)]
        private static void OpenFromCobaltTools()
        {
            OpenWindow();
        }

        private static TextureColorBleedWindow OpenWindow()
        {
            TextureColorBleedWindow window = GetWindow<TextureColorBleedWindow>();
            window.titleContent = new GUIContent("贴图工具");
            window.minSize = new Vector2(650f, 600f);
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
            EditorGUILayout.BeginVertical(GUILayout.Width(160f));
            GUILayout.Space(8f);
            GUILayout.Label("贴图工具", EditorStyles.boldLabel);
            GUILayout.Space(6f);

            DrawPageButton(ToolPage.BatchImageSettings, "批量图像设置");
            DrawPageButton(ToolPage.ColorBleed, "颜色溢出");

            GUILayout.FlexibleSpace();
            EditorGUILayout.HelpBox("先在 Project 窗口选择文件夹或贴图，再运行右侧工具。", MessageType.Info);
            EditorGUILayout.EndVertical();
        }

        private void DrawPageButton(ToolPage page, string label)
        {
            bool selected = currentPage == page;
            GUIStyle style = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = selected ? FontStyle.Bold : FontStyle.Normal,
                padding = new RectOffset(12, 8, 4, 4)
            };

            if (GUILayout.Toggle(selected, label, style, GUILayout.Height(30f)))
                currentPage = page;
        }

        private void DrawPage()
        {
            EditorGUILayout.BeginVertical();
            GUILayout.Space(8f);

            switch (currentPage)
            {
                case ToolPage.BatchImageSettings:
                    DrawBatchImageSettingsPage();
                    break;
                case ToolPage.ColorBleed:
                    DrawColorBleedPage();
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

        private List<string> CollectSelectedPngPaths(bool includeSubfolders)
        {
            HashSet<string> uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in CollectSelectedAssetPaths("t:Texture2D", includeSubfolders))
                AddPngIfSupported(path, uniquePaths);

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
