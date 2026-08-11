#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SleepyCobalt.Tools.ResourceTools
{
    public sealed class ResourceDependencyAnalyzerWindow : EditorWindow
    {
        private sealed class AnalysisResult
        {
            public readonly List<string> scannedAssets = new List<string>();
            public readonly List<string> usedAssets = new List<string>();
            public readonly List<string> unusedAssets = new List<string>();
            public readonly List<string> demoLikeAssets = new List<string>();
            public readonly HashSet<string> dependencySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        [SerializeField] private List<UnityEngine.Object> keepEntries = new List<UnityEngine.Object>();
        [SerializeField] private UnityEngine.Object scanFolder;
        [SerializeField] private UnityEngine.Object moveTargetFolder;
        [SerializeField] private Vector2 keepScroll;
        [SerializeField] private Vector2 resultScroll;
        [SerializeField] private int resultTab;

        private AnalysisResult lastResult;
        private string lastScanFolderPath;
        private readonly HashSet<string> selectedResultPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] ResultTabs =
        {
            "疑似未使用",
            "被保留依赖",
            "Demo/Example",
            "全部扫描"
        };

        [MenuItem("CobaltTools/资源依赖分析", false, 21)]
        private static void Open()
        {
            ResourceDependencyAnalyzerWindow window = GetWindow<ResourceDependencyAnalyzerWindow>();
            window.titleContent = new GUIContent("资源依赖分析");
            window.minSize = new Vector2(760f, 520f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("资源依赖分析", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "把确定要保留的 Prefab、Scene、Material、配置或文件夹拖到上方；把素材包目录拖到扫描目录。工具只做分析，不会删除或移动资源。",
                MessageType.Info);

            DrawKeepEntries();
            DrawScanFolder();
            DrawActions();
            DrawResults();
        }

        private void DrawKeepEntries()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("保留入口", EditorStyles.boldLabel);
            DrawDropArea("拖入要保留的资源或文件夹", AddKeepObject);

            keepScroll = EditorGUILayout.BeginScrollView(keepScroll, GUILayout.MinHeight(90f), GUILayout.MaxHeight(150f));
            for (int i = 0; i < keepEntries.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                keepEntries[i] = EditorGUILayout.ObjectField(keepEntries[i], typeof(UnityEngine.Object), false);
                if (GUILayout.Button("移除", GUILayout.Width(52f)))
                {
                    keepEntries.RemoveAt(i);
                    i--;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("添加当前选中"))
            {
                foreach (UnityEngine.Object obj in Selection.objects)
                    AddKeepObject(obj);
            }

            if (GUILayout.Button("清空入口"))
                keepEntries.Clear();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawScanFolder()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("扫描目录", EditorStyles.boldLabel);
            DrawDropArea("拖入要检查的素材包文件夹", SetScanFolder);

            EditorGUILayout.BeginHorizontal();
            scanFolder = EditorGUILayout.ObjectField(scanFolder, typeof(DefaultAsset), false);
            if (GUILayout.Button("使用当前选中文件夹", GUILayout.Width(140f)))
            {
                foreach (UnityEngine.Object obj in Selection.objects)
                {
                    string path = NormalizePath(AssetDatabase.GetAssetPath(obj));
                    if (AssetDatabase.IsValidFolder(path))
                    {
                        scanFolder = obj;
                        break;
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawActions()
        {
            EditorGUILayout.Space(8f);
            using (new EditorGUI.DisabledScope(!CanAnalyze()))
            {
                if (GUILayout.Button("分析依赖", GUILayout.Height(34f)))
                    Analyze();
            }

            if (!CanAnalyze())
                EditorGUILayout.HelpBox("至少需要 1 个保留入口，并指定 1 个扫描目录。", MessageType.None);

            using (new EditorGUI.DisabledScope(!AssetDatabase.IsValidFolder(GetScanFolderPath())))
            {
                if (GUILayout.Button("删除扫描目录下空文件夹", GUILayout.Height(26f)))
                    DeleteEmptyFoldersUnderScanFolder();
            }
        }

        private void DrawResults()
        {
            if (lastResult == null)
                return;

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("分析结果", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("扫描目录", lastScanFolderPath);

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            DrawStat("全部", lastResult.scannedAssets.Count);
            DrawStat("保留", lastResult.usedAssets.Count);
            DrawStat("疑似未使用", lastResult.unusedAssets.Count);
            DrawStat("Demo/Example", lastResult.demoLikeAssets.Count);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("复制报告"))
                EditorGUIUtility.systemCopyBuffer = BuildReport();

            if (GUILayout.Button("导出报告"))
                ExportReport();
            EditorGUILayout.EndHorizontal();

            DrawMoveControls();

            resultTab = GUILayout.Toolbar(resultTab, ResultTabs);
            List<string> list = GetCurrentResultList();

            resultScroll = EditorGUILayout.BeginScrollView(resultScroll);
            foreach (string path in list)
            {
                EditorGUILayout.BeginHorizontal();
                bool selected = selectedResultPaths.Contains(path);
                bool newSelected = EditorGUILayout.Toggle(selected, GUILayout.Width(18f));
                if (newSelected != selected)
                {
                    if (newSelected)
                        selectedResultPaths.Add(path);
                    else
                        selectedResultPaths.Remove(path);
                }

                EditorGUILayout.SelectableLabel(path, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button("选中", GUILayout.Width(48f)))
                    SelectAsset(path);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawMoveControls()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("移动勾选结果", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全选当前页"))
                SetCurrentResultSelection(true);

            if (GUILayout.Button("取消当前页"))
                SetCurrentResultSelection(false);

            if (GUILayout.Button("清空全部勾选"))
                selectedResultPaths.Clear();
            EditorGUILayout.EndHorizontal();

            DrawDropArea("拖入移动目标文件夹", SetMoveTargetFolder);

            EditorGUILayout.BeginHorizontal();
            moveTargetFolder = EditorGUILayout.ObjectField(moveTargetFolder, typeof(DefaultAsset), false);
            if (GUILayout.Button("使用/创建 Assets/_UnusedReview", GUILayout.Width(190f)))
                moveTargetFolder = GetOrCreateFolderAsset("Assets/_UnusedReview");
            EditorGUILayout.EndHorizontal();

            string targetPath = GetMoveTargetFolderPath();
            EditorGUILayout.LabelField("已勾选", selectedResultPaths.Count.ToString());
            using (new EditorGUI.DisabledScope(selectedResultPaths.Count == 0 || !AssetDatabase.IsValidFolder(targetPath)))
            {
                if (GUILayout.Button("移动勾选资源到目标文件夹", GUILayout.Height(28f)))
                    MoveSelectedAssets(targetPath);
            }

            if (!string.IsNullOrEmpty(targetPath) && !AssetDatabase.IsValidFolder(targetPath))
                EditorGUILayout.HelpBox("移动目标必须是 Assets 下的文件夹。", MessageType.Warning);

            EditorGUILayout.EndVertical();
        }

        private static void DrawStat(string label, int value)
        {
            EditorGUILayout.LabelField(label + ": " + value, GUILayout.Width(120f));
        }

        private void DrawDropArea(string text, Action<UnityEngine.Object> onObjectDropped)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 42f, GUILayout.ExpandWidth(true));
            GUI.Box(rect, text, EditorStyles.helpBox);

            Event current = Event.current;
            if (!rect.Contains(current.mousePosition))
                return;

            int eventType = (int)current.type;
            if (eventType == 9)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                current.Use();
            }
            else if (eventType == 10)
            {
                DragAndDrop.AcceptDrag();
                foreach (UnityEngine.Object obj in DragAndDrop.objectReferences)
                    onObjectDropped(obj);
                current.Use();
            }
        }

        private void AddKeepObject(UnityEngine.Object obj)
        {
            if (obj == null)
                return;

            string path = NormalizePath(AssetDatabase.GetAssetPath(obj));
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return;

            if (!keepEntries.Contains(obj))
                keepEntries.Add(obj);
        }

        private void SetScanFolder(UnityEngine.Object obj)
        {
            string path = NormalizePath(AssetDatabase.GetAssetPath(obj));
            if (AssetDatabase.IsValidFolder(path))
                scanFolder = obj;
        }

        private void SetMoveTargetFolder(UnityEngine.Object obj)
        {
            string path = NormalizePath(AssetDatabase.GetAssetPath(obj));
            if (AssetDatabase.IsValidFolder(path))
                moveTargetFolder = obj;
        }

        private bool CanAnalyze()
        {
            return GetValidKeepEntryPaths().Count > 0 && AssetDatabase.IsValidFolder(GetScanFolderPath());
        }

        private void Analyze()
        {
            List<string> keepPaths = GetValidKeepEntryPaths();
            string folderPath = GetScanFolderPath();
            AnalysisResult result = new AnalysisResult();

            try
            {
                for (int i = 0; i < keepPaths.Count; i++)
                {
                    string path = keepPaths[i];
                    EditorUtility.DisplayProgressBar(
                        "资源依赖分析",
                        "收集保留入口依赖: " + path,
                        keepPaths.Count == 0 ? 0f : (float)i / keepPaths.Count);

                    foreach (string rootAsset in ExpandAssetRoots(path))
                    {
                        string[] dependencies = AssetDatabase.GetDependencies(rootAsset, true);
                        foreach (string dependency in dependencies)
                            result.dependencySet.Add(NormalizePath(dependency));
                    }
                }

                List<string> scannedAssets = CollectAssetsInFolder(folderPath);
                for (int i = 0; i < scannedAssets.Count; i++)
                {
                    string assetPath = scannedAssets[i];
                    EditorUtility.DisplayProgressBar(
                        "资源依赖分析",
                        "比对扫描目录: " + assetPath,
                        scannedAssets.Count == 0 ? 1f : (float)i / scannedAssets.Count);

                    result.scannedAssets.Add(assetPath);

                    if (result.dependencySet.Contains(assetPath))
                        result.usedAssets.Add(assetPath);
                    else
                        result.unusedAssets.Add(assetPath);

                    if (LooksLikeDemoAsset(assetPath))
                        result.demoLikeAssets.Add(assetPath);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            SortResult(result);
            lastResult = result;
            lastScanFolderPath = folderPath;
            selectedResultPaths.Clear();
            resultTab = 0;
        }

        private List<string> GetValidKeepEntryPaths()
        {
            HashSet<string> uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (UnityEngine.Object obj in keepEntries)
            {
                string path = NormalizePath(AssetDatabase.GetAssetPath(obj));
                if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    uniquePaths.Add(path);
            }

            List<string> paths = new List<string>(uniquePaths);
            paths.Sort(StringComparer.OrdinalIgnoreCase);
            return paths;
        }

        private string GetScanFolderPath()
        {
            return NormalizePath(AssetDatabase.GetAssetPath(scanFolder));
        }

        private string GetMoveTargetFolderPath()
        {
            return NormalizePath(AssetDatabase.GetAssetPath(moveTargetFolder));
        }

        private void DeleteEmptyFoldersUnderScanFolder()
        {
            string folderPath = GetScanFolderPath();
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                EditorUtility.DisplayDialog("删除空文件夹", "请先设置扫描目录。", "确定");
                return;
            }

            List<string> emptyFolders = CollectEmptySubfolders(folderPath);
            if (emptyFolders.Count == 0)
            {
                EditorUtility.DisplayDialog("删除空文件夹", "扫描目录下没有空文件夹。", "确定");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "删除空文件夹",
                "即将删除 " + emptyFolders.Count + " 个空文件夹。\n\n不会删除扫描目录本身，也不会删除包含资源的文件夹。\n\n" +
                BuildSampleList(emptyFolders),
                "删除",
                "取消");

            if (!confirmed)
                return;

            int deletedCount = 0;
            int failedCount = 0;
            List<string> errors = new List<string>();

            try
            {
                while (true)
                {
                    emptyFolders = CollectEmptySubfolders(folderPath);
                    if (emptyFolders.Count == 0)
                        break;

                    int deletedThisPass = 0;
                    for (int i = 0; i < emptyFolders.Count; i++)
                    {
                        string path = emptyFolders[i];
                        EditorUtility.DisplayProgressBar(
                            "删除空文件夹",
                            path,
                            emptyFolders.Count == 0 ? 1f : (float)i / emptyFolders.Count);

                        if (!AssetDatabase.IsValidFolder(path) || !IsAssetFolderEmpty(path))
                            continue;

                        bool deleted = AssetDatabase.DeleteAsset(path);
                        if (deleted)
                        {
                            deletedCount++;
                            deletedThisPass++;
                        }
                        else
                        {
                            failedCount++;
                            errors.Add(path);
                        }
                    }

                    AssetDatabase.Refresh();
                    if (deletedThisPass == 0)
                        break;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            string message = "已删除: " + deletedCount + "\n失败: " + failedCount;
            if (errors.Count > 0)
            {
                int shownCount = Mathf.Min(5, errors.Count);
                message += "\n\n前几条失败路径:\n" + string.Join("\n", errors.GetRange(0, shownCount).ToArray());
            }

            EditorUtility.DisplayDialog("删除空文件夹结果", message, "确定");
        }

        private void SetCurrentResultSelection(bool selected)
        {
            foreach (string path in GetCurrentResultList())
            {
                if (selected)
                    selectedResultPaths.Add(path);
                else
                    selectedResultPaths.Remove(path);
            }
        }

        private void MoveSelectedAssets(string targetFolderPath)
        {
            List<string> paths = new List<string>(selectedResultPaths);
            paths.Sort(StringComparer.OrdinalIgnoreCase);

            bool confirmed = EditorUtility.DisplayDialog(
                "移动资源",
                "即将移动 " + paths.Count + " 个资源到:\n" + targetFolderPath + "\n\n会保留扫描目录下的相对文件夹结构，不会删除资源。",
                "移动",
                "取消");

            if (!confirmed)
                return;

            int movedCount = 0;
            int failedCount = 0;
            List<string> errors = new List<string>();

            try
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    string sourcePath = paths[i];
                    EditorUtility.DisplayProgressBar(
                        "移动资源",
                        sourcePath,
                        paths.Count == 0 ? 1f : (float)i / paths.Count);

                    string destinationPath = BuildMoveDestinationPath(sourcePath, targetFolderPath);
                    EnsureAssetFolder(NormalizePath(Path.GetDirectoryName(destinationPath)));

                    if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(destinationPath) != null)
                        destinationPath = AssetDatabase.GenerateUniqueAssetPath(destinationPath);

                    string error = AssetDatabase.MoveAsset(sourcePath, destinationPath);
                    if (string.IsNullOrEmpty(error))
                    {
                        movedCount++;
                        RemovePathFromLastResult(sourcePath);
                    }
                    else
                    {
                        failedCount++;
                        errors.Add(sourcePath + "\n" + error);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                selectedResultPaths.Clear();
            }

            string message = "已移动: " + movedCount + "\n失败: " + failedCount;
            if (errors.Count > 0)
            {
                int shownCount = Mathf.Min(5, errors.Count);
                message += "\n\n前几条错误:\n" + string.Join("\n\n", errors.GetRange(0, shownCount).ToArray());
            }

            EditorUtility.DisplayDialog("移动资源结果", message, "确定");
        }

        private string BuildMoveDestinationPath(string sourcePath, string targetFolderPath)
        {
            string relativePath = sourcePath;
            string scanRoot = string.IsNullOrEmpty(lastScanFolderPath) ? string.Empty : lastScanFolderPath.TrimEnd('/');
            if (!string.IsNullOrEmpty(scanRoot) &&
                sourcePath.StartsWith(scanRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                relativePath = sourcePath.Substring(scanRoot.Length + 1);
            }
            else
            {
                relativePath = Path.GetFileName(sourcePath);
            }

            return NormalizePath(targetFolderPath.TrimEnd('/') + "/" + relativePath);
        }

        private void RemovePathFromLastResult(string path)
        {
            if (lastResult == null)
                return;

            lastResult.scannedAssets.Remove(path);
            lastResult.usedAssets.Remove(path);
            lastResult.unusedAssets.Remove(path);
            lastResult.demoLikeAssets.Remove(path);
            lastResult.dependencySet.Remove(path);
        }

        private static IEnumerable<string> ExpandAssetRoots(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                yield return path;
                yield break;
            }

            foreach (string assetPath in CollectAssetsInFolder(path))
                yield return assetPath;
        }

        private static List<string> CollectAssetsInFolder(string folderPath)
        {
            List<string> paths = new List<string>();
            string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });
            foreach (string guid in guids)
            {
                string path = NormalizePath(AssetDatabase.GUIDToAssetPath(guid));
                if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
                    continue;

                paths.Add(path);
            }

            paths.Sort(StringComparer.OrdinalIgnoreCase);
            return paths;
        }

        private static List<string> CollectEmptySubfolders(string rootFolderPath)
        {
            List<string> folders = new List<string>();
            string rootFullPath = Path.GetFullPath(rootFolderPath);
            if (!Directory.Exists(rootFullPath))
                return folders;

            foreach (string fullPath in Directory.GetDirectories(rootFullPath, "*", SearchOption.AllDirectories))
            {
                string assetPath = FullPathToAssetPath(fullPath);
                if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (AssetDatabase.IsValidFolder(assetPath))
                    folders.Add(assetPath);
            }

            folders.Sort((left, right) =>
            {
                int depthCompare = GetPathDepth(right).CompareTo(GetPathDepth(left));
                return depthCompare != 0 ? depthCompare : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
            });

            HashSet<string> deletableSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> emptyFolders = new List<string>();
            foreach (string folder in folders)
            {
                if (CanBecomeEmptyFolder(folder, deletableSet))
                {
                    deletableSet.Add(folder);
                    emptyFolders.Add(folder);
                }
            }

            return emptyFolders;
        }

        private static bool IsAssetFolderEmpty(string folderPath)
        {
            string fullPath = Path.GetFullPath(folderPath);
            if (!Directory.Exists(fullPath))
                return false;

            return GetNonMetaFiles(fullPath).Count == 0 &&
                   Directory.GetDirectories(fullPath).Length == 0;
        }

        private static bool CanBecomeEmptyFolder(string folderPath, HashSet<string> foldersPlannedForDeletion)
        {
            string fullPath = Path.GetFullPath(folderPath);
            if (!Directory.Exists(fullPath) || GetNonMetaFiles(fullPath).Count > 0)
                return false;

            foreach (string childFullPath in Directory.GetDirectories(fullPath))
            {
                string childAssetPath = FullPathToAssetPath(childFullPath);
                if (!foldersPlannedForDeletion.Contains(childAssetPath))
                    return false;
            }

            return true;
        }

        private static List<string> GetNonMetaFiles(string fullPath)
        {
            List<string> files = new List<string>();
            foreach (string file in Directory.GetFiles(fullPath))
            {
                if (!string.Equals(Path.GetExtension(file), ".meta", StringComparison.OrdinalIgnoreCase))
                    files.Add(file);
            }

            return files;
        }

        private static int GetPathDepth(string path)
        {
            return NormalizePath(path).Split('/').Length;
        }

        private static string FullPathToAssetPath(string fullPath)
        {
            string projectRoot = NormalizePath(Path.GetFullPath(Path.Combine(Application.dataPath, ".."))).TrimEnd('/');
            string normalizedFullPath = NormalizePath(Path.GetFullPath(fullPath));
            if (!normalizedFullPath.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            return normalizedFullPath.Substring(projectRoot.Length + 1);
        }

        private static UnityEngine.Object GetOrCreateFolderAsset(string folderPath)
        {
            EnsureAssetFolder(folderPath);
            return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(folderPath);
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            folderPath = NormalizePath(folderPath);
            if (string.IsNullOrEmpty(folderPath) || AssetDatabase.IsValidFolder(folderPath))
                return;

            if (!folderPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("只能在 Assets 目录下创建文件夹: " + folderPath);

            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);

                current = next;
            }
        }

        private static bool LooksLikeDemoAsset(string path)
        {
            string lower = path.ToLowerInvariant();
            return lower.Contains("/demo") ||
                   lower.Contains("/demos") ||
                   lower.Contains("/example") ||
                   lower.Contains("/examples") ||
                   lower.Contains("/sample") ||
                   lower.Contains("/samples") ||
                   lower.Contains("/tutorial") ||
                   lower.Contains("/documentation") ||
                   lower.Contains("/readme");
        }

        private List<string> GetCurrentResultList()
        {
            switch (resultTab)
            {
                case 1:
                    return lastResult.usedAssets;
                case 2:
                    return lastResult.demoLikeAssets;
                case 3:
                    return lastResult.scannedAssets;
                default:
                    return lastResult.unusedAssets;
            }
        }

        private string BuildReport()
        {
            if (lastResult == null)
                return string.Empty;

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("资源依赖分析报告");
            builder.AppendLine("扫描目录: " + lastScanFolderPath);
            builder.AppendLine("全部: " + lastResult.scannedAssets.Count);
            builder.AppendLine("保留: " + lastResult.usedAssets.Count);
            builder.AppendLine("疑似未使用: " + lastResult.unusedAssets.Count);
            builder.AppendLine("Demo/Example: " + lastResult.demoLikeAssets.Count);
            builder.AppendLine();

            AppendSection(builder, "疑似未使用", lastResult.unusedAssets);
            AppendSection(builder, "被保留依赖", lastResult.usedAssets);
            AppendSection(builder, "Demo/Example", lastResult.demoLikeAssets);
            return builder.ToString();
        }

        private static void AppendSection(StringBuilder builder, string title, List<string> paths)
        {
            builder.AppendLine("[" + title + "]");
            foreach (string path in paths)
                builder.AppendLine(path);
            builder.AppendLine();
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

        private void ExportReport()
        {
            string path = EditorUtility.SaveFilePanel(
                "导出资源依赖分析报告",
                Application.dataPath,
                "ResourceDependencyReport.txt",
                "txt");

            if (string.IsNullOrEmpty(path))
                return;

            File.WriteAllText(path, BuildReport(), Encoding.UTF8);
            EditorUtility.RevealInFinder(path);
        }

        private static void SelectAsset(string path)
        {
            UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (obj == null)
                return;

            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        }

        private static void SortResult(AnalysisResult result)
        {
            result.scannedAssets.Sort(StringComparer.OrdinalIgnoreCase);
            result.usedAssets.Sort(StringComparer.OrdinalIgnoreCase);
            result.unusedAssets.Sort(StringComparer.OrdinalIgnoreCase);
            result.demoLikeAssets.Sort(StringComparer.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }
    }
}
#endif
