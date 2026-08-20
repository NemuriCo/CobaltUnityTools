#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Presets;
using UnityEngine;

namespace SleepyCobalt.Tools.TextureTools
{
    public sealed partial class TextureColorBleedWindow
    {
        [SerializeField] private Vector2 textureCategoriesScrollPosition;
        [SerializeField] private bool showUngroupedTextures = true;
        [SerializeField] private bool showIgnoredTextureRules;
        [SerializeField] private string ungroupedSearchText = string.Empty;
        private TextureCategoryQuickSearch ungroupedQuickSearch;

        private enum TextureCategoryQuickSearch
        {
            None,
            Sprite,
            Normal,
            Transparency,
            Alpha
        }

        private sealed class TextureQuickSearchInfo
        {
            internal bool isTexture;
            internal TextureImporterType textureType;
            internal bool alphaIsTransparency;
            internal bool hasSourceAlpha;
        }

        private TextureCategoryResolutionSet textureCategoryResolution;
        private bool textureCategoryResolutionDirty = true;
        private readonly Dictionary<string, string> textureCategoryMemberSearchTexts =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, TextureCategoryQuickSearch> textureCategoryMemberQuickSearches =
            new Dictionary<string, TextureCategoryQuickSearch>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> textureSearchMatchesByQuery =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TextureQuickSearchInfo> textureQuickSearchInfoByPath =
            new Dictionary<string, TextureQuickSearchInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> selectedUngroupedAssetPaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<string>> selectedTextureCategoryMemberPaths =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> lastSelectedTextureCategoryMemberPaths =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private string lastSelectedUngroupedAssetPath;

        private sealed class TextureCategoryApplyJob
        {
            internal string categoryName;
            internal List<string> assetPaths;
            internal Preset textureImporterPreset;
        }

        private void OnEnable()
        {
            if (!Enum.IsDefined(typeof(ToolPage), currentPage))
                currentPage = ToolPage.TextureCategories;

            InitializeTextureCategoryPage();
        }

        private void InitializeTextureCategoryPage()
        {
            EditorApplication.projectChanged -= OnTextureCategoryProjectChanged;
            EditorApplication.projectChanged += OnTextureCategoryProjectChanged;

            TextureCategoryProjectSettings settings = TextureCategoryProjectSettings.instance;
            if (settings.EnsureIntegrity())
                settings.SaveSettings();

            MarkTextureCategoryResolutionDirty();
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= OnTextureCategoryProjectChanged;
        }

        private void OnTextureCategoryProjectChanged()
        {
            MarkTextureCategoryResolutionDirty();
            Repaint();
        }

        private void MarkTextureCategoryResolutionDirty()
        {
            textureCategoryResolutionDirty = true;
            textureSearchMatchesByQuery.Clear();
            textureQuickSearchInfoByPath.Clear();
        }

        private void DrawTextureCategoriesPage()
        {
            TextureCategoryProjectSettings settings = GetTextureCategorySettings();
            EnsureTextureCategoryResolution(settings);

            textureCategoriesScrollPosition = EditorGUILayout.BeginScrollView(
                textureCategoriesScrollPosition,
                false,
                true);
            EditorGUILayout.LabelField("贴图分类", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "创建分类并拖入 Unity 的 TextureImporter Preset 资源。可从 Project 窗口拖入图片或文件夹；" +
                "文件夹会递归包含以后新增的资源。设置只会在点击应用按钮后写入。",
                MessageType.Info);

            DrawTextureCategoryToolbar(settings);
            DrawTextureClassificationScope(settings);
            DrawUngroupedTextureSection(settings);
            DrawIgnoredTextureRules(settings);

            if (textureCategoryResolution.HasConflicts)
            {
                EditorGUILayout.HelpBox(
                    "检测到 " + textureCategoryResolution.conflictAssetCount +
                    " 个资源同时属于多个分类。请先移除重叠来源，再应用全部分类。",
                    MessageType.Error);
            }

            if (settings.categories.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "还没有分类。点击“新建分类”，拖入 TextureImporter Preset 后把图片或文件夹拖入分类。",
                    MessageType.None);
            }

            for (int index = 0; index < settings.categories.Count; index++)
            {
                TextureCategoryRecord category = settings.categories[index];
                TextureCategoryResolvedRecord resolved = GetResolvedCategory(category);
                if (!DrawTextureCategoryCard(settings, category, resolved))
                    continue;

                settings.categories.RemoveAt(index);
                settings.SaveSettings();
                MarkTextureCategoryResolutionDirty();
                break;
            }

            EditorGUILayout.EndScrollView();
        }

        private TextureCategoryProjectSettings GetTextureCategorySettings()
        {
            TextureCategoryProjectSettings settings = TextureCategoryProjectSettings.instance;
            settings.LoadBackupIfNecessary();
            if (settings.EnsureIntegrity())
                settings.SaveSettings();
            return settings;
        }

        private void EnsureTextureCategoryResolution(TextureCategoryProjectSettings settings)
        {
            if (!textureCategoryResolutionDirty && textureCategoryResolution != null)
                return;

            textureCategoryResolution = TextureCategoryResolver.Resolve(settings);
            textureCategoryResolutionDirty = false;
        }

        private void DrawUngroupedTextureSection(TextureCategoryProjectSettings settings)
        {
            int ungroupedCount = textureCategoryResolution.ungroupedAssetPaths.Count;
            PruneSelectedUngroupedAssets();
            List<string> visibleUngroupedPaths = GetFilteredUngroupedAssetPaths();
            if (ungroupedCount > 0)
            {
                EditorGUILayout.HelpBox(
                    "待分组范围内还有 " + textureCategoryResolution.ungroupedTextureCount +
                    " 张图片未加入任何分类，也没有被忽略。",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox("待分组范围内的图片都已分类或忽略。", MessageType.Info);
            }

            showUngroupedTextures = EditorGUILayout.Foldout(
                showUngroupedTextures,
                "未分组贴图（" + ungroupedCount + "）",
                true);
            if (!showUngroupedTextures || ungroupedCount == 0)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("搜索未分组", GUILayout.Width(72f));
            string newSearchText = EditorGUILayout.TextField(ungroupedSearchText ?? string.Empty);
            if (!string.Equals(newSearchText, ungroupedSearchText, StringComparison.Ordinal))
            {
                ungroupedSearchText = newSearchText;
                visibleUngroupedPaths = GetFilteredUngroupedAssetPaths();
                Repaint();
            }
            if (GUILayout.Button("清除", GUILayout.Width(48f)))
            {
                ungroupedSearchText = string.Empty;
                visibleUngroupedPaths = GetFilteredUngroupedAssetPaths();
                Repaint();
            }
            if (GUILayout.Button(GetQuickSearchButtonLabel(ungroupedQuickSearch), GUILayout.Width(88f)))
                ShowQuickSearchMenu(ungroupedQuickSearch, selected =>
                {
                    ungroupedQuickSearch = selected;
                    Repaint();
                });
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            int visibleSelectedCount = CountSelectedUngroupedAssets(visibleUngroupedPaths);
            EditorGUILayout.LabelField(
                visibleSelectedCount == 0
                    ? "点击选择贴图，Ctrl/Cmd 多选，Shift 连选"
                    : "已选择 " + visibleSelectedCount + " 个资源",
                EditorStyles.miniLabel);
            if (GUILayout.Button("全选", GUILayout.Width(48f)))
            {
                selectedUngroupedAssetPaths.Clear();
                selectedUngroupedAssetPaths.UnionWith(visibleUngroupedPaths);
                Repaint();
            }
            if (GUILayout.Button("清空选择", GUILayout.Width(64f)))
            {
                selectedUngroupedAssetPaths.Clear();
                lastSelectedUngroupedAssetPath = null;
                Repaint();
            }
            using (new EditorGUI.DisabledScope(visibleSelectedCount == 0))
            {
                if (GUILayout.Button("加入分类…", GUILayout.Width(72f)))
                    ShowAddUngroupedTextureMenu(settings, GetSelectedUngroupedAssetPaths(visibleUngroupedPaths));
                if (GUILayout.Button("忽略", GUILayout.Width(48f)))
                    AddIgnoredUngroupedTextures(settings, GetSelectedUngroupedAssetPaths(visibleUngroupedPaths));
            }
            EditorGUILayout.EndHorizontal();

            if (visibleUngroupedPaths.Count == 0)
            {
                EditorGUILayout.HelpBox("没有匹配当前搜索条件的未分组资源。", MessageType.Info);
            }
            else
            {
                DrawUngroupedTextureGrid(settings, visibleUngroupedPaths);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawUngroupedTextureGrid(
            TextureCategoryProjectSettings settings,
            IList<string> assetPaths)
        {
            if (assetPaths == null || assetPaths.Count == 0)
                return;

            const float tileWidth = 112f;
            const float tileHeight = 112f;
            const float previewSize = 78f;
            int columnCount = Mathf.Max(1, Mathf.FloorToInt((GetTextureCategoryContentWidth() - 12f) / tileWidth));
            for (int index = 0; index < assetPaths.Count; index++)
            {
                if (index % columnCount == 0)
                    EditorGUILayout.BeginHorizontal();

                string path = assetPaths[index];
                Rect tileRect = GUILayoutUtility.GetRect(
                    tileWidth,
                    tileHeight,
                    GUILayout.Width(tileWidth),
                    GUILayout.Height(tileHeight));
                bool selected = selectedUngroupedAssetPaths.Contains(path);
                if (selected)
                    EditorGUI.DrawRect(tileRect, new Color(0.24f, 0.48f, 0.75f, 0.45f));
                else
                    GUI.Box(tileRect, GUIContent.none, EditorStyles.helpBox);

                UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                Texture2D preview = asset == null ? null : AssetPreview.GetAssetPreview(asset);
                if (preview == null && asset != null)
                    preview = AssetPreview.GetMiniThumbnail(asset);
                Rect previewRect = new Rect(
                    tileRect.x + (tileRect.width - previewSize) * 0.5f,
                    tileRect.y + 4f,
                    previewSize,
                    previewSize);
                if (preview != null)
                    GUI.DrawTexture(previewRect, preview, ScaleMode.ScaleToFit, true);

                string label = Path.GetFileName(path);
                GUI.Label(
                    new Rect(tileRect.x + 4f, tileRect.y + previewSize + 7f, tileRect.width - 8f, 18f),
                    label,
                    EditorStyles.centeredGreyMiniLabel);

                Event currentEvent = Event.current;
                bool actionKey = currentEvent.control || currentEvent.command;
                bool shiftKey = currentEvent.shift;
                bool isRightClick = currentEvent.button == 1 &&
                    (currentEvent.type == EventType.ContextClick || currentEvent.type == EventType.MouseDown);
                if (isRightClick && tileRect.Contains(currentEvent.mousePosition))
                {
                    ShowUngroupedAssetContextMenu(settings, path);
                    currentEvent.Use();
                }
                if (GUI.Button(tileRect, GUIContent.none, GUIStyle.none))
                    SelectUngroupedAsset(assetPaths, index, actionKey, shiftKey);

                if (index % columnCount == columnCount - 1 || index == assetPaths.Count - 1)
                    EditorGUILayout.EndHorizontal();
            }
        }

        private void SelectUngroupedAsset(
            IList<string> assetPaths,
            int index,
            bool actionKey,
            bool shiftKey)
        {
            if (assetPaths == null || index < 0 || index >= assetPaths.Count)
                return;

            string path = assetPaths[index];
            if (shiftKey && !string.IsNullOrEmpty(lastSelectedUngroupedAssetPath))
            {
                IList<string> selectionOrder = textureCategoryResolution.ungroupedAssetPaths;
                int anchorIndex = selectionOrder.IndexOf(lastSelectedUngroupedAssetPath);
                int targetIndex = selectionOrder.IndexOf(path);
                if (anchorIndex >= 0 && targetIndex >= 0)
                {
                    if (!actionKey)
                        selectedUngroupedAssetPaths.Clear();
                    int start = Mathf.Min(anchorIndex, targetIndex);
                    int end = Mathf.Max(anchorIndex, targetIndex);
                    for (int rangeIndex = start; rangeIndex <= end; rangeIndex++)
                        selectedUngroupedAssetPaths.Add(selectionOrder[rangeIndex]);
                    lastSelectedUngroupedAssetPath = path;
                    Repaint();
                    return;
                }
            }

            if (actionKey)
            {
                if (!selectedUngroupedAssetPaths.Add(path))
                    selectedUngroupedAssetPaths.Remove(path);
            }
            else
            {
                selectedUngroupedAssetPaths.Clear();
                selectedUngroupedAssetPaths.Add(path);
            }

            lastSelectedUngroupedAssetPath = path;
            Repaint();
        }

        private void PruneSelectedUngroupedAssets()
        {
            HashSet<string> validPaths = new HashSet<string>(
                textureCategoryResolution.ungroupedAssetPaths,
                StringComparer.OrdinalIgnoreCase);
            selectedUngroupedAssetPaths.RemoveWhere(path => !validPaths.Contains(path));
            if (!string.IsNullOrEmpty(lastSelectedUngroupedAssetPath) && !validPaths.Contains(lastSelectedUngroupedAssetPath))
                lastSelectedUngroupedAssetPath = null;
        }

        private List<string> GetSelectedUngroupedAssetPaths(IList<string> visiblePaths)
        {
            List<string> paths = new List<string>();
            if (visiblePaths == null)
                return paths;

            foreach (string path in visiblePaths)
            {
                if (selectedUngroupedAssetPaths.Contains(path))
                    paths.Add(path);
            }

            return paths;
        }

        private int CountSelectedUngroupedAssets(IList<string> visiblePaths)
        {
            if (visiblePaths == null)
                return 0;

            int count = 0;
            foreach (string path in visiblePaths)
            {
                if (selectedUngroupedAssetPaths.Contains(path))
                    count++;
            }

            return count;
        }

        private List<string> GetFilteredUngroupedAssetPaths()
        {
            return FilterTextureCategoryAssetPaths(
                textureCategoryResolution.ungroupedAssetPaths,
                ungroupedSearchText,
                ungroupedQuickSearch);
        }

        private List<string> GetFilteredTextureCategoryAssetPaths(
            TextureCategoryRecord category,
            IList<string> assetPaths)
        {
            string searchText = string.Empty;
            if (category != null && !string.IsNullOrEmpty(category.id))
                textureCategoryMemberSearchTexts.TryGetValue(category.id, out searchText);

            TextureCategoryQuickSearch quickSearch = TextureCategoryQuickSearch.None;
            if (category != null && !string.IsNullOrEmpty(category.id))
                textureCategoryMemberQuickSearches.TryGetValue(category.id, out quickSearch);

            return FilterTextureCategoryAssetPaths(assetPaths, searchText, quickSearch);
        }

        private List<string> FilterTextureCategoryAssetPaths(
            IList<string> assetPaths,
            string searchText,
            TextureCategoryQuickSearch quickSearch)
        {
            List<string> filteredPaths = new List<string>();
            if (assetPaths == null)
                return filteredPaths;

            string search = (searchText ?? string.Empty).Trim();
            HashSet<string> matchingPaths = string.IsNullOrEmpty(search)
                ? null
                : GetTextureSearchMatches(search);
            foreach (string path in assetPaths)
            {
                if (!string.IsNullOrEmpty(path) &&
                    (matchingPaths == null || matchingPaths.Contains(path)) &&
                    MatchesTextureQuickSearch(path, quickSearch))
                {
                    filteredPaths.Add(path);
                }
            }

            return filteredPaths;
        }

        private HashSet<string> GetTextureSearchMatches(string search)
        {
            if (textureSearchMatchesByQuery.TryGetValue(search, out HashSet<string> cachedMatches))
                return cachedMatches;

            HashSet<string> matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string[] guids = AssetDatabase.FindAssets(search, new[] { "Assets" });
                foreach (string guid in guids)
                {
                    string path = NormalizePath(AssetDatabase.GUIDToAssetPath(guid));
                    if (TextureCategoryResolver.IsAssetsPath(path))
                        matches.Add(path);
                }
            }
            catch (Exception)
            {
                // Invalid search syntax should simply produce no matches.
            }

            textureSearchMatchesByQuery.Add(search, matches);
            return matches;
        }

        private bool MatchesTextureQuickSearch(
            string path,
            TextureCategoryQuickSearch quickSearch)
        {
            if (quickSearch == TextureCategoryQuickSearch.None)
                return true;

            string fileName = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            bool nameContainsNormal = fileName.IndexOf("Normal", StringComparison.OrdinalIgnoreCase) >= 0;
            if (quickSearch == TextureCategoryQuickSearch.Normal && nameContainsNormal)
                return true;

            TextureQuickSearchInfo info = GetTextureQuickSearchInfo(path);
            if (!info.isTexture)
                return false;

            switch (quickSearch)
            {
                case TextureCategoryQuickSearch.Sprite:
                    return info.textureType == TextureImporterType.Sprite;
                case TextureCategoryQuickSearch.Normal:
                    return info.textureType == TextureImporterType.NormalMap;
                case TextureCategoryQuickSearch.Transparency:
                    return info.alphaIsTransparency;
                case TextureCategoryQuickSearch.Alpha:
                    return info.hasSourceAlpha && !info.alphaIsTransparency;
                default:
                    return true;
            }
        }

        private TextureQuickSearchInfo GetTextureQuickSearchInfo(string path)
        {
            if (textureQuickSearchInfoByPath.TryGetValue(path, out TextureQuickSearchInfo cachedInfo))
                return cachedInfo;

            TextureQuickSearchInfo info = new TextureQuickSearchInfo();
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                info.isTexture = true;
                info.textureType = importer.textureType;
                info.alphaIsTransparency = importer.alphaIsTransparency;
                try
                {
                    info.hasSourceAlpha = importer.DoesSourceTextureHaveAlpha();
                }
                catch (Exception)
                {
                    info.hasSourceAlpha = false;
                }
            }

            textureQuickSearchInfoByPath[path] = info;
            return info;
        }

        private static string GetQuickSearchButtonLabel(TextureCategoryQuickSearch quickSearch)
        {
            switch (quickSearch)
            {
                case TextureCategoryQuickSearch.Sprite:
                    return "快捷：Sprite";
                case TextureCategoryQuickSearch.Normal:
                    return "快捷：Normal";
                case TextureCategoryQuickSearch.Transparency:
                    return "快捷：透明";
                case TextureCategoryQuickSearch.Alpha:
                    return "快捷：Alpha";
                default:
                    return "快捷检索";
            }
        }

        private static string GetQuickSearchMenuLabel(TextureCategoryQuickSearch quickSearch)
        {
            switch (quickSearch)
            {
                case TextureCategoryQuickSearch.Sprite:
                    return "Sprite（Texture Type = Sprite）";
                case TextureCategoryQuickSearch.Normal:
                    return "Normal（类型为 Normal 或名称含 Normal）";
                case TextureCategoryQuickSearch.Transparency:
                    return "Transparency（Alpha Is Transparency）";
                case TextureCategoryQuickSearch.Alpha:
                    return "Alpha（有 Alpha 且未启用 Alpha Is Transparency）";
                default:
                    return "不限快捷条件";
            }
        }

        private static void ShowQuickSearchMenu(
            TextureCategoryQuickSearch currentSearch,
            Action<TextureCategoryQuickSearch> onSelected)
        {
            GenericMenu menu = new GenericMenu();
            AddQuickSearchMenuItem(menu, TextureCategoryQuickSearch.None, currentSearch, onSelected);
            menu.AddSeparator(string.Empty);
            AddQuickSearchMenuItem(menu, TextureCategoryQuickSearch.Sprite, currentSearch, onSelected);
            AddQuickSearchMenuItem(menu, TextureCategoryQuickSearch.Normal, currentSearch, onSelected);
            AddQuickSearchMenuItem(menu, TextureCategoryQuickSearch.Transparency, currentSearch, onSelected);
            AddQuickSearchMenuItem(menu, TextureCategoryQuickSearch.Alpha, currentSearch, onSelected);
            menu.ShowAsContext();
        }

        private static void AddQuickSearchMenuItem(
            GenericMenu menu,
            TextureCategoryQuickSearch quickSearch,
            TextureCategoryQuickSearch currentSearch,
            Action<TextureCategoryQuickSearch> onSelected)
        {
            menu.AddItem(
                new GUIContent(GetQuickSearchMenuLabel(quickSearch)),
                quickSearch == currentSearch,
                () => onSelected(quickSearch));
        }

        private void ShowUngroupedAssetContextMenu(
            TextureCategoryProjectSettings settings,
            string path)
        {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            List<string> actionPaths = PrepareUngroupedContextActionPaths(path);
            GenericMenu menu = new GenericMenu();
            if (asset == null)
            {
                menu.AddDisabledItem(new GUIContent("资源已失效"));
            }
            else
            {
                menu.AddItem(
                    new GUIContent("忽略"),
                    false,
                    () => AddIgnoredUngroupedTextures(settings, actionPaths));
                AddUngroupedTextureCategoryMenuItems(menu, settings, actionPaths);
                menu.AddSeparator(string.Empty);
                menu.AddItem(
                    new GUIContent("在 Project 中定位"),
                    false,
                    () => PingAssetInProject(asset));
            }

            menu.ShowAsContext();
        }

        private void AddUngroupedTextureCategoryMenuItems(
            GenericMenu menu,
            TextureCategoryProjectSettings settings,
            IList<string> paths)
        {
            bool hasCategory = false;
            if (settings != null && settings.categories != null)
            {
                foreach (TextureCategoryRecord category in settings.categories)
                {
                    if (category == null || string.IsNullOrWhiteSpace(category.name))
                        continue;

                    hasCategory = true;
                    TextureCategoryRecord targetCategory = category;
                    menu.AddItem(
                        new GUIContent("加入分类/" + targetCategory.name.Trim()),
                        false,
                        () => AddUngroupedTexturesToCategory(settings, targetCategory, paths));
                }
            }

            if (!hasCategory)
                menu.AddDisabledItem(new GUIContent("加入分类/请先新建分类"));
        }

        private List<string> PrepareUngroupedContextActionPaths(string path)
        {
            if (!selectedUngroupedAssetPaths.Contains(path))
            {
                selectedUngroupedAssetPaths.Clear();
                selectedUngroupedAssetPaths.Add(path);
                lastSelectedUngroupedAssetPath = path;
                Repaint();
            }

            return GetSelectedUngroupedAssetPaths(textureCategoryResolution.ungroupedAssetPaths);
        }

        private static void PingAssetInProject(UnityEngine.Object asset)
        {
            if (asset == null)
                return;

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private void DrawTextureClassificationScope(TextureCategoryProjectSettings settings)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("待分组范围", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "只检查这里添加的资源：" + textureCategoryResolution.classificationTextureCount + " 张图片",
                EditorStyles.miniLabel);

            DrawTextureClassificationDropArea(settings);

            if (settings.classificationSources.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "请从 Project 窗口拖入需要整理的图片或文件夹。不会再自动扫描整个项目。",
                    MessageType.Info);
            }
            else
            {
                HashSet<TextureCategorySourceRecord> missingSources =
                    new HashSet<TextureCategorySourceRecord>(textureCategoryResolution.missingClassificationSources);
                for (int index = 0; index < settings.classificationSources.Count; index++)
                {
                    TextureCategorySourceRecord source = settings.classificationSources[index];
                    string currentPath = source == null
                        ? string.Empty
                        : NormalizePath(AssetDatabase.GUIDToAssetPath(source.guid));

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(
                        source != null && source.kind == TextureCategorySourceKind.Folder ? "文件夹" : "资源",
                        GUILayout.Width(44f));
                    if (!string.IsNullOrEmpty(currentPath))
                    {
                        using (new EditorGUI.DisabledScope(true))
                        {
                            EditorGUILayout.ObjectField(
                                AssetDatabase.LoadMainAssetAtPath(currentPath),
                                typeof(UnityEngine.Object),
                                false);
                        }
                    }
                    else
                    {
                        EditorGUILayout.LabelField(
                            (source == null ? "未知来源" : source.lastKnownPath) + "（失效）",
                            EditorStyles.miniLabel);
                    }

                    if (GUILayout.Button("移除", GUILayout.Width(48f)))
                    {
                        settings.classificationSources.RemoveAt(index);
                        settings.SaveSettings();
                        MarkTextureCategoryResolutionDirty();
                        EditorGUILayout.EndHorizontal();
                        break;
                    }

                    EditorGUILayout.EndHorizontal();
                    if (source != null && missingSources.Contains(source) && !string.IsNullOrEmpty(currentPath))
                        EditorGUILayout.LabelField("    当前资源类型不受支持。", EditorStyles.miniLabel);
                }
            }

            if (textureCategoryResolution.missingClassificationSources.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    "有 " + textureCategoryResolution.missingClassificationSources.Count +
                    " 个待分组来源已失效，可以从列表中移除后重新拖入。",
                    MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6f);
        }

        private void DrawTextureClassificationDropArea(TextureCategoryProjectSettings settings)
        {
            Rect dropArea = GUILayoutUtility.GetRect(0f, 48f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "拖入需要分组的图片或文件夹", EditorStyles.helpBox);

            Event currentEvent = Event.current;
            if (!dropArea.Contains(currentEvent.mousePosition) ||
                (currentEvent.type != EventType.DragUpdated && currentEvent.type != EventType.DragPerform))
            {
                return;
            }

            List<string> draggedPaths = GetDraggedProjectPaths();
            bool hasValidSource = false;
            foreach (string path in draggedPaths)
            {
                if (TryCreateTextureCategorySource(path, out _, out _))
                {
                    hasValidSource = true;
                    break;
                }
            }

            DragAndDrop.visualMode = hasValidSource ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            if (currentEvent.type == EventType.DragUpdated)
            {
                currentEvent.Use();
                return;
            }

            DragAndDrop.AcceptDrag();
            bool changed = false;
            List<string> rejected = new List<string>();
            foreach (string path in draggedPaths)
            {
                if (!TryCreateTextureCategorySource(path, out TextureCategorySourceRecord source, out string reason))
                {
                    rejected.Add(path + "：" + reason);
                    continue;
                }

                bool duplicate = settings.classificationSources.Exists(item =>
                    item != null && item.kind == source.kind && item.guid == source.guid);
                if (duplicate)
                    continue;

                settings.classificationSources.Add(source);
                changed = true;
            }

            if (changed)
            {
                settings.SaveSettings();
                MarkTextureCategoryResolutionDirty();
            }

            if (rejected.Count > 0)
            {
                int displayCount = Mathf.Min(5, rejected.Count);
                EditorUtility.DisplayDialog(
                    "部分资源未加入待分组范围",
                    string.Join("\n", rejected.GetRange(0, displayCount).ToArray()),
                    "确定");
            }

            currentEvent.Use();
        }

        private void DrawIgnoredTextureRules(TextureCategoryProjectSettings settings)
        {
            showIgnoredTextureRules = EditorGUILayout.Foldout(
                showIgnoredTextureRules,
                "忽略规则（" + settings.ignoredSources.Count + "）",
                true);
            if (!showIgnoredTextureRules)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawIgnoredTextureDropArea(settings);

            if (settings.ignoredSources.Count == 0)
            {
                EditorGUILayout.LabelField("没有忽略规则。", EditorStyles.miniLabel);
            }
            else
            {
                HashSet<TextureCategorySourceRecord> missingRules =
                    new HashSet<TextureCategorySourceRecord>(textureCategoryResolution.missingIgnoredSources);
                for (int index = 0; index < settings.ignoredSources.Count; index++)
                {
                    TextureCategorySourceRecord source = settings.ignoredSources[index];
                    string currentPath = source == null
                        ? string.Empty
                        : NormalizePath(AssetDatabase.GUIDToAssetPath(source.guid));

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label(
                        source != null && source.kind == TextureCategorySourceKind.Folder ? "文件夹" : "资源",
                        GUILayout.Width(44f));
                    if (!string.IsNullOrEmpty(currentPath))
                    {
                        using (new EditorGUI.DisabledScope(true))
                        {
                            EditorGUILayout.ObjectField(
                                AssetDatabase.LoadMainAssetAtPath(currentPath),
                                typeof(UnityEngine.Object),
                                false);
                        }
                    }
                    else
                    {
                        EditorGUILayout.LabelField(
                            (source == null ? "未知来源" : source.lastKnownPath) + "（失效）",
                            EditorStyles.miniLabel);
                    }

                    if (GUILayout.Button("取消忽略", GUILayout.Width(64f)))
                    {
                        settings.ignoredSources.RemoveAt(index);
                        settings.SaveSettings();
                        MarkTextureCategoryResolutionDirty();
                        EditorGUILayout.EndHorizontal();
                        break;
                    }

                    EditorGUILayout.EndHorizontal();
                    if (source != null && missingRules.Contains(source) && !string.IsNullOrEmpty(currentPath))
                        EditorGUILayout.LabelField("    当前资源类型不受支持。", EditorStyles.miniLabel);
                }
            }

            if (textureCategoryResolution.missingIgnoredSources.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    "有 " + textureCategoryResolution.missingIgnoredSources.Count +
                    " 个忽略规则已经失效，可以从上方列表取消。",
                    MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6f);
        }

        private void ShowAddUngroupedTextureMenu(
            TextureCategoryProjectSettings settings,
            IList<string> paths)
        {
            GenericMenu menu = new GenericMenu();
            bool hasCategory = false;
            foreach (TextureCategoryRecord category in settings.categories)
            {
                if (category == null || string.IsNullOrWhiteSpace(category.name))
                    continue;

                hasCategory = true;
                TextureCategoryRecord targetCategory = category;
                menu.AddItem(
                    new GUIContent(category.name.Trim()),
                    false,
                    () => AddUngroupedTexturesToCategory(settings, targetCategory, paths));
            }

            if (!hasCategory)
                menu.AddDisabledItem(new GUIContent("请先新建分类"));
            menu.ShowAsContext();
        }

        private void AddUngroupedTexturesToCategory(
            TextureCategoryProjectSettings settings,
            TextureCategoryRecord category,
            IList<string> paths)
        {
            if (category == null || paths == null || paths.Count == 0)
                return;

            bool changed = false;
            List<string> rejected = new List<string>();
            foreach (string path in paths)
            {
                if (!TryCreateTextureCategorySource(path, out TextureCategorySourceRecord source, out string reason))
                {
                    rejected.Add(path + "：" + reason);
                    continue;
                }

                if (source.kind == TextureCategorySourceKind.Asset &&
                    category.excludedAssetGuids.Remove(source.guid))
                {
                    changed = true;
                }

                bool duplicate = category.sources.Exists(item =>
                    item != null && item.kind == source.kind && item.guid == source.guid);
                if (!duplicate)
                {
                    category.sources.Add(source);
                    changed = true;
                }
            }

            if (changed)
            {
                settings.SaveSettings();
                MarkTextureCategoryResolutionDirty();
                Repaint();
            }

            if (rejected.Count > 0)
            {
                int displayCount = Mathf.Min(5, rejected.Count);
                EditorUtility.DisplayDialog(
                    "部分资源未加入分类",
                    string.Join("\n", rejected.GetRange(0, displayCount).ToArray()),
                    "确定");
            }
        }

        private void AddIgnoredUngroupedTextures(
            TextureCategoryProjectSettings settings,
            IList<string> paths)
        {
            if (paths == null || paths.Count == 0)
                return;

            bool changed = false;
            List<string> rejected = new List<string>();
            foreach (string path in paths)
            {
                if (!TryCreateTextureCategorySource(path, out TextureCategorySourceRecord source, out string reason))
                {
                    rejected.Add(path + "：" + reason);
                    continue;
                }

                bool duplicate = settings.ignoredSources.Exists(item =>
                    item != null && item.kind == source.kind && item.guid == source.guid);
                if (!duplicate)
                {
                    settings.ignoredSources.Add(source);
                    changed = true;
                }
            }

            if (changed)
            {
                settings.SaveSettings();
                MarkTextureCategoryResolutionDirty();
                Repaint();
            }

            if (rejected.Count > 0)
            {
                int displayCount = Mathf.Min(5, rejected.Count);
                EditorUtility.DisplayDialog(
                    "部分资源未添加忽略规则",
                    string.Join("\n", rejected.GetRange(0, displayCount).ToArray()),
                    "确定");
            }
        }

        private void AddIgnoredTextureFolder(TextureCategoryProjectSettings settings, string assetPath)
        {
            string folderPath = NormalizePath(Path.GetDirectoryName(assetPath));
            if (string.IsNullOrEmpty(folderPath) || string.Equals(folderPath, "Assets", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog(
                    "无法忽略文件夹",
                    "Assets 根目录不能作为忽略规则，请忽略单个资源或选择更具体的子文件夹。",
                    "确定");
                return;
            }

            AddIgnoredTextureRule(settings, folderPath);
        }

        private void AddIgnoredTextureRule(TextureCategoryProjectSettings settings, string path)
        {
            if (!TryCreateTextureCategorySource(path, out TextureCategorySourceRecord source, out string reason))
            {
                EditorUtility.DisplayDialog("无法添加忽略规则", reason, "确定");
                return;
            }

            bool duplicate = settings.ignoredSources.Exists(item =>
                item != null && item.kind == source.kind && item.guid == source.guid);
            if (!duplicate)
            {
                settings.ignoredSources.Add(source);
                settings.SaveSettings();
                MarkTextureCategoryResolutionDirty();
                Repaint();
            }
        }

        private void DrawIgnoredTextureDropArea(TextureCategoryProjectSettings settings)
        {
            Rect dropArea = GUILayoutUtility.GetRect(0f, 42f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "拖入要忽略的图片或文件夹", EditorStyles.helpBox);

            Event currentEvent = Event.current;
            if (!dropArea.Contains(currentEvent.mousePosition) ||
                (currentEvent.type != EventType.DragUpdated && currentEvent.type != EventType.DragPerform))
            {
                return;
            }

            List<string> draggedPaths = GetDraggedProjectPaths();
            bool hasValidSource = false;
            foreach (string path in draggedPaths)
            {
                if (TryCreateTextureCategorySource(path, out _, out _))
                {
                    hasValidSource = true;
                    break;
                }
            }

            DragAndDrop.visualMode = hasValidSource ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            if (currentEvent.type == EventType.DragUpdated)
            {
                currentEvent.Use();
                return;
            }

            DragAndDrop.AcceptDrag();
            bool changed = false;
            List<string> rejected = new List<string>();
            foreach (string path in draggedPaths)
            {
                if (!TryCreateTextureCategorySource(path, out TextureCategorySourceRecord source, out string reason))
                {
                    rejected.Add(path + "：" + reason);
                    continue;
                }

                bool duplicate = settings.ignoredSources.Exists(item =>
                    item != null && item.kind == source.kind && item.guid == source.guid);
                if (duplicate)
                    continue;

                settings.ignoredSources.Add(source);
                changed = true;
            }

            if (changed)
            {
                settings.SaveSettings();
                MarkTextureCategoryResolutionDirty();
            }

            if (rejected.Count > 0)
            {
                int displayCount = Mathf.Min(5, rejected.Count);
                EditorUtility.DisplayDialog(
                    "部分忽略规则未添加",
                    string.Join("\n", rejected.GetRange(0, displayCount).ToArray()),
                    "确定");
            }

            currentEvent.Use();
        }

        private TextureCategoryResolvedRecord GetResolvedCategory(TextureCategoryRecord category)
        {
            if (category == null || textureCategoryResolution == null || string.IsNullOrEmpty(category.id))
                return new TextureCategoryResolvedRecord { category = category };

            return textureCategoryResolution.byCategoryId.TryGetValue(
                category.id,
                out TextureCategoryResolvedRecord resolved)
                ? resolved
                : new TextureCategoryResolvedRecord { category = category };
        }

        private void DrawTextureCategoryToolbar(TextureCategoryProjectSettings settings)
        {
            Rect toolbarRect = GUILayoutUtility.GetRect(0f, 24f, GUILayout.ExpandWidth(true));
            const float spacing = 4f;
            float buttonWidth = Mathf.Max(1f, (toolbarRect.width - spacing * 2f) / 3f);
            Rect createRect = new Rect(toolbarRect.x, toolbarRect.y, buttonWidth, toolbarRect.height);
            Rect refreshRect = new Rect(
                createRect.xMax + spacing,
                toolbarRect.y,
                buttonWidth,
                toolbarRect.height);
            Rect applyRect = new Rect(
                refreshRect.xMax + spacing,
                toolbarRect.y,
                buttonWidth,
                toolbarRect.height);

            if (GUI.Button(createRect, "新建分类"))
            {
                settings.categories.Add(new TextureCategoryRecord
                {
                    id = Guid.NewGuid().ToString("N"),
                    name = GenerateUniqueTextureCategoryName(settings.categories),
                    expanded = true
                });
                settings.SaveSettings();
                MarkTextureCategoryResolutionDirty();
            }

            if (GUI.Button(refreshRect, "刷新成员"))
                MarkTextureCategoryResolutionDirty();

            EnsureTextureCategoryResolution(settings);
            using (new EditorGUI.DisabledScope(!CanApplyAllTextureCategories(settings)))
            {
                if (GUI.Button(applyRect, "应用全部分类"))
                    ApplyAllTextureCategories(settings);
            }

            EditorGUILayout.Space(6f);
        }

        private float GetTextureCategoryContentWidth()
        {
            return Mathf.Max(240f, position.width - 160f - 32f);
        }

        private bool DrawTextureCategoryCard(
            TextureCategoryProjectSettings settings,
            TextureCategoryRecord category,
            TextureCategoryResolvedRecord resolved)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            string title = string.IsNullOrWhiteSpace(category.name) ? "未命名分类" : category.name.Trim();
            bool expanded = EditorGUILayout.Foldout(category.expanded, title, true, EditorStyles.foldoutHeader);
            if (expanded != category.expanded)
            {
                category.expanded = expanded;
                settings.SaveSettings();
            }

            bool deleteRequested = GUILayout.Button("删除", GUILayout.Width(52f));
            EditorGUILayout.EndHorizontal();

            if (deleteRequested && EditorUtility.DisplayDialog(
                    "删除贴图分类",
                    "确定删除分类“" + title + "”？这不会修改或删除任何图片资源。",
                    "删除",
                    "取消"))
            {
                EditorGUILayout.EndVertical();
                return true;
            }

            if (!category.expanded)
            {
                EditorGUILayout.EndVertical();
                return false;
            }

            EditorGUI.BeginChangeCheck();
            category.name = EditorGUILayout.DelayedTextField("分类名称", category.name ?? string.Empty);
            if (EditorGUI.EndChangeCheck())
                settings.SaveSettings();

            if (!TryValidateTextureCategoryName(settings, category, out string nameError))
                EditorGUILayout.HelpBox(nameError, MessageType.Error);

            DrawTextureCategoryPresetField(settings, category);
            DrawTextureCategoryDropArea(settings, category);
            DrawTextureCategorySources(settings, category, resolved);

            EditorGUILayout.LabelField(
                "当前成员",
                resolved.textureCount + " 张图片");

            if (resolved.missingSources.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    "有 " + resolved.missingSources.Count + " 个来源已失效或不再是支持的资源，请从来源列表中移除或重新拖入。",
                    MessageType.Warning);
            }

            if (resolved.conflictPaths.Count > 0)
                DrawTextureCategoryConflicts(resolved);

            DrawTextureCategoryMemberGrid(settings, category, resolved);

            bool canApply = CanApplyTextureCategory(settings, category, resolved);
            using (new EditorGUI.DisabledScope(!canApply))
            {
                if (GUILayout.Button("应用此分类", GUILayout.Height(32f)))
                    ApplySingleTextureCategory(category, resolved);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6f);
            return false;
        }

        private void DrawTextureCategoryPresetField(
            TextureCategoryProjectSettings settings,
            TextureCategoryRecord category)
        {
            EditorGUI.BeginChangeCheck();
            Preset preset = (Preset)EditorGUILayout.ObjectField(
                new GUIContent("TextureImporter Preset", "拖入 Preset Type 为 UnityEditor.TextureImporter 的资源。"),
                category.textureImporterPreset,
                typeof(Preset),
                false);
            if (EditorGUI.EndChangeCheck())
            {
                category.textureImporterPreset = preset;
                settings.SaveSettings();
            }

            if (category.textureImporterPreset == null)
            {
                EditorGUILayout.HelpBox("请拖入一个 TextureImporter Preset 资源。", MessageType.Error);
            }
            else if (!IsTextureImporterPreset(category.textureImporterPreset))
            {
                EditorGUILayout.HelpBox(
                    "当前 Preset 的类型不是 UnityEditor.TextureImporter，无法应用到图片。",
                    MessageType.Error);
            }
            else
            {
                EditorGUILayout.LabelField(
                    "预设状态",
                    "直接使用“" + category.textureImporterPreset.name + "”",
                    EditorStyles.miniLabel);
            }
        }

        private static bool IsTextureImporterPreset(Preset preset)
        {
            return preset != null &&
                   string.Equals(
                       preset.GetTargetFullTypeName(),
                       typeof(TextureImporter).FullName,
                       StringComparison.Ordinal);
        }

        private void DrawTextureCategoryDropArea(
            TextureCategoryProjectSettings settings,
            TextureCategoryRecord category)
        {
            Rect dropArea = GUILayoutUtility.GetRect(0f, 54f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "从 Project 拖入图片或文件夹", EditorStyles.helpBox);

            Event currentEvent = Event.current;
            if (!dropArea.Contains(currentEvent.mousePosition) ||
                (currentEvent.type != EventType.DragUpdated && currentEvent.type != EventType.DragPerform))
            {
                return;
            }

            List<string> draggedPaths = GetDraggedProjectPaths();
            bool hasValidSource = false;
            foreach (string path in draggedPaths)
            {
                if (TryCreateTextureCategorySource(path, out _, out _))
                {
                    hasValidSource = true;
                    break;
                }
            }

            DragAndDrop.visualMode = hasValidSource ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            if (currentEvent.type == EventType.DragUpdated)
            {
                currentEvent.Use();
                return;
            }

            DragAndDrop.AcceptDrag();
            List<string> rejected = new List<string>();
            bool changed = false;
            foreach (string path in draggedPaths)
            {
                if (!TryCreateTextureCategorySource(path, out TextureCategorySourceRecord source, out string reason))
                {
                    rejected.Add(path + "：" + reason);
                    continue;
                }

                if (source.kind == TextureCategorySourceKind.Asset &&
                    category.excludedAssetGuids.Remove(source.guid))
                {
                    changed = true;
                }

                bool duplicate = category.sources.Exists(item =>
                    item != null && item.kind == source.kind && item.guid == source.guid);
                if (duplicate)
                    continue;

                category.sources.Add(source);
                changed = true;
            }

            if (changed)
            {
                settings.SaveSettings();
                MarkTextureCategoryResolutionDirty();
            }

            if (rejected.Count > 0)
            {
                int displayCount = Mathf.Min(5, rejected.Count);
                EditorUtility.DisplayDialog(
                    "部分资源未加入分类",
                    string.Join("\n", rejected.GetRange(0, displayCount).ToArray()) +
                    (rejected.Count > displayCount ? "\n……以及另外 " + (rejected.Count - displayCount) + " 个" : string.Empty),
                    "确定");
            }

            currentEvent.Use();
        }

        private static List<string> GetDraggedProjectPaths()
        {
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in DragAndDrop.paths)
            {
                if (!string.IsNullOrEmpty(path))
                    paths.Add(NormalizePath(path));
            }

            foreach (UnityEngine.Object draggedObject in DragAndDrop.objectReferences)
            {
                string path = NormalizePath(AssetDatabase.GetAssetPath(draggedObject));
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path);
            }

            return new List<string>(paths);
        }

        private static bool TryCreateTextureCategorySource(
            string path,
            out TextureCategorySourceRecord source,
            out string reason)
        {
            source = null;
            reason = string.Empty;
            path = NormalizePath(path);

            if (!TextureCategoryResolver.IsAssetsPath(path))
            {
                reason = "只支持 Assets 下的资源";
                return false;
            }

            bool isFolder = AssetDatabase.IsValidFolder(path);
            if (!isFolder && !TextureCategoryResolver.TryClassifySupportedAsset(path, out _, out _))
            {
                reason = "不是可设置 TextureImporter 的图片";
                return false;
            }

            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
            {
                reason = "资源没有可保存的 GUID";
                return false;
            }

            source = new TextureCategorySourceRecord
            {
                kind = isFolder ? TextureCategorySourceKind.Folder : TextureCategorySourceKind.Asset,
                guid = guid,
                lastKnownPath = path
            };
            return true;
        }

        private void DrawTextureCategorySources(
            TextureCategoryProjectSettings settings,
            TextureCategoryRecord category,
            TextureCategoryResolvedRecord resolved)
        {
            EditorGUILayout.LabelField("来源规则", EditorStyles.boldLabel);
            HashSet<TextureCategorySourceRecord> missing =
                new HashSet<TextureCategorySourceRecord>(resolved.missingSources);
            bool displayedRule = false;
            for (int index = 0; index < category.sources.Count; index++)
            {
                TextureCategorySourceRecord source = category.sources[index];
                bool isMissing = source != null && missing.Contains(source);
                if (source != null && source.kind != TextureCategorySourceKind.Folder && !isMissing)
                    continue;

                displayedRule = true;
                string currentPath = source == null
                    ? string.Empty
                    : NormalizePath(AssetDatabase.GUIDToAssetPath(source.guid));

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(
                    source != null && source.kind == TextureCategorySourceKind.Folder ? "文件夹" : "资源",
                    GUILayout.Width(44f));

                if (!string.IsNullOrEmpty(currentPath))
                {
                    UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(currentPath);
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.ObjectField(asset, typeof(UnityEngine.Object), false);
                }
                else
                {
                    string lastPath = source == null ? "未知来源" : source.lastKnownPath;
                    EditorGUILayout.LabelField(lastPath + "（失效）", EditorStyles.miniLabel);
                }

                if (GUILayout.Button("移除", GUILayout.Width(48f)))
                {
                    category.sources.RemoveAt(index);
                    settings.SaveSettings();
                    MarkTextureCategoryResolutionDirty();
                    EditorGUILayout.EndHorizontal();
                    break;
                }

                EditorGUILayout.EndHorizontal();
                if (source != null && missing.Contains(source) && !string.IsNullOrEmpty(currentPath))
                    EditorGUILayout.LabelField("    当前资源类型不受支持。", EditorStyles.miniLabel);
            }

            if (!displayedRule)
                EditorGUILayout.LabelField("没有文件夹规则；单图来源统一显示在当前成员缩略图中。", EditorStyles.miniLabel);
        }

        private void DrawTextureCategoryMemberGrid(
            TextureCategoryProjectSettings settings,
            TextureCategoryRecord category,
            TextureCategoryResolvedRecord resolved)
        {
            HashSet<string> selectedPaths = GetSelectedTextureCategoryMemberPaths(category);
            PruneSelectedTextureCategoryMemberPaths(selectedPaths, resolved);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("搜索成员", GUILayout.Width(72f));
            string currentSearchText;
            if (category == null ||
                string.IsNullOrEmpty(category.id) ||
                !textureCategoryMemberSearchTexts.TryGetValue(category.id, out currentSearchText))
            {
                currentSearchText = string.Empty;
            }
            string newSearchText = EditorGUILayout.TextField(currentSearchText ?? string.Empty);
            if (!string.Equals(newSearchText, currentSearchText, StringComparison.Ordinal))
            {
                if (category != null && !string.IsNullOrEmpty(category.id))
                    textureCategoryMemberSearchTexts[category.id] = newSearchText;
                Repaint();
            }
            if (GUILayout.Button("清除", GUILayout.Width(48f)))
            {
                if (category != null && !string.IsNullOrEmpty(category.id))
                    textureCategoryMemberSearchTexts.Remove(category.id);
                Repaint();
            }
            TextureCategoryQuickSearch quickSearch = TextureCategoryQuickSearch.None;
            if (category != null && !string.IsNullOrEmpty(category.id))
                textureCategoryMemberQuickSearches.TryGetValue(category.id, out quickSearch);
            if (GUILayout.Button(GetQuickSearchButtonLabel(quickSearch), GUILayout.Width(88f)))
            {
                TextureCategoryRecord targetCategory = category;
                ShowQuickSearchMenu(quickSearch, selected =>
                {
                    if (targetCategory != null && !string.IsNullOrEmpty(targetCategory.id))
                        textureCategoryMemberQuickSearches[targetCategory.id] = selected;
                    Repaint();
                });
            }
            EditorGUILayout.EndHorizontal();

            List<string> visibleAssetPaths = GetFilteredTextureCategoryAssetPaths(
                category,
                resolved.assetPaths);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                selectedPaths.Count == 0
                    ? "点击选择成员，Ctrl/Cmd 多选，Shift 连选"
                    : "已选择 " + selectedPaths.Count + " 个成员",
                EditorStyles.miniLabel);
            if (GUILayout.Button("全选", GUILayout.Width(48f)))
            {
                selectedPaths.Clear();
                selectedPaths.UnionWith(visibleAssetPaths);
                Repaint();
            }
            if (GUILayout.Button("清空选择", GUILayout.Width(64f)))
            {
                selectedPaths.Clear();
                selectedTextureCategoryMemberPaths.Remove(category.id);
                lastSelectedTextureCategoryMemberPaths.Remove(category.id);
                Repaint();
            }
            using (new EditorGUI.DisabledScope(selectedPaths.Count == 0))
            {
                if (GUILayout.Button("移动到", GUILayout.Width(56f)))
                {
                    ShowMoveTextureCategoryMenu(
                        settings,
                        category,
                        new List<string>(selectedPaths));
                }
                if (GUILayout.Button("移除", GUILayout.Width(48f)))
                {
                    RemoveTextureCategoryMembers(
                        settings,
                        category,
                        new List<string>(selectedPaths));
                }
            }
            EditorGUILayout.EndHorizontal();

            if (visibleAssetPaths.Count == 0)
            {
                EditorGUILayout.LabelField(
                    resolved.assetPaths.Count == 0
                        ? "没有可显示的成员。"
                        : "没有匹配当前搜索条件的成员。",
                    EditorStyles.miniLabel);
                return;
            }

            const float tileWidth = 112f;
            const float tileHeight = 112f;
            const float previewSize = 78f;
            int columnCount = Mathf.Max(1, Mathf.FloorToInt((GetTextureCategoryContentWidth() - 12f) / tileWidth));
            for (int index = 0; index < visibleAssetPaths.Count; index++)
            {
                if (index % columnCount == 0)
                    EditorGUILayout.BeginHorizontal();

                string path = visibleAssetPaths[index];
                Rect tileRect = GUILayoutUtility.GetRect(
                    tileWidth,
                    tileHeight,
                    GUILayout.Width(tileWidth),
                    GUILayout.Height(tileHeight));

                if (selectedPaths.Contains(path))
                    EditorGUI.DrawRect(tileRect, new Color(0.24f, 0.48f, 0.75f, 0.45f));
                else
                    GUI.Box(tileRect, GUIContent.none, EditorStyles.helpBox);

                UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                Texture2D preview = asset == null ? null : AssetPreview.GetAssetPreview(asset);
                if (preview == null && asset != null)
                    preview = AssetPreview.GetMiniThumbnail(asset);
                Rect previewRect = new Rect(
                    tileRect.x + (tileRect.width - previewSize) * 0.5f,
                    tileRect.y + 4f,
                    previewSize,
                    previewSize);
                if (preview != null)
                    GUI.DrawTexture(previewRect, preview, ScaleMode.ScaleToFit, true);

                GUI.Label(
                    new Rect(tileRect.x + 4f, tileRect.y + previewSize + 7f, tileRect.width - 8f, 18f),
                    Path.GetFileName(path),
                    EditorStyles.centeredGreyMiniLabel);

                Event currentEvent = Event.current;
                bool actionKey = currentEvent.control || currentEvent.command;
                bool shiftKey = currentEvent.shift;
                bool isRightClick = currentEvent.button == 1 &&
                    (currentEvent.type == EventType.ContextClick || currentEvent.type == EventType.MouseDown);
                if (isRightClick && tileRect.Contains(currentEvent.mousePosition))
                {
                    ShowTextureCategoryMemberContextMenu(settings, category, path);
                    currentEvent.Use();
                }
                if (GUI.Button(tileRect, GUIContent.none, GUIStyle.none))
                    SelectTextureCategoryMember(category, visibleAssetPaths, index, actionKey, shiftKey);

                if (index % columnCount == columnCount - 1 || index == visibleAssetPaths.Count - 1)
                    EditorGUILayout.EndHorizontal();
            }
        }

        private HashSet<string> GetSelectedTextureCategoryMemberPaths(TextureCategoryRecord category)
        {
            if (category == null || string.IsNullOrEmpty(category.id))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!selectedTextureCategoryMemberPaths.TryGetValue(category.id, out HashSet<string> selected))
            {
                selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                selectedTextureCategoryMemberPaths.Add(category.id, selected);
            }

            return selected;
        }

        private void ShowTextureCategoryMemberContextMenu(
            TextureCategoryProjectSettings settings,
            TextureCategoryRecord category,
            string path)
        {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            List<string> actionPaths = PrepareTextureCategoryContextActionPaths(category, path);
            GenericMenu menu = new GenericMenu();
            if (asset == null)
            {
                menu.AddDisabledItem(new GUIContent("资源已失效"));
            }
            else
            {
                menu.AddItem(
                    new GUIContent("移除"),
                    false,
                    () => RemoveTextureCategoryMembers(settings, category, actionPaths));
                AddMoveTextureCategoryMenuItems(menu, settings, category, actionPaths);
                menu.AddSeparator(string.Empty);
                menu.AddItem(
                    new GUIContent("在 Project 中定位"),
                    false,
                    () => PingAssetInProject(asset));
            }

            menu.ShowAsContext();
        }

        private void AddMoveTextureCategoryMenuItems(
            GenericMenu menu,
            TextureCategoryProjectSettings settings,
            TextureCategoryRecord sourceCategory,
            IList<string> paths)
        {
            bool hasTargetCategory = false;
            if (settings != null && settings.categories != null)
            {
                foreach (TextureCategoryRecord targetCategory in settings.categories)
                {
                    if (targetCategory == null ||
                        targetCategory == sourceCategory ||
                        string.IsNullOrWhiteSpace(targetCategory.name))
                    {
                        continue;
                    }

                    hasTargetCategory = true;
                    TextureCategoryRecord capturedTargetCategory = targetCategory;
                    menu.AddItem(
                        new GUIContent("移动到/" + capturedTargetCategory.name.Trim()),
                        false,
                        () => MoveTextureCategoryMembers(
                            settings,
                            sourceCategory,
                            capturedTargetCategory,
                            paths));
                }
            }

            if (!hasTargetCategory)
                menu.AddDisabledItem(new GUIContent("移动到/没有其他可用分类"));
        }

        private List<string> PrepareTextureCategoryContextActionPaths(
            TextureCategoryRecord category,
            string path)
        {
            HashSet<string> selectedPaths = GetSelectedTextureCategoryMemberPaths(category);
            if (!selectedPaths.Contains(path))
            {
                selectedPaths.Clear();
                selectedPaths.Add(path);
                lastSelectedTextureCategoryMemberPaths[category.id] = path;
                Repaint();
            }

            return new List<string>(selectedPaths);
        }

        private void PruneSelectedTextureCategoryMemberPaths(
            HashSet<string> selectedPaths,
            TextureCategoryResolvedRecord resolved)
        {
            HashSet<string> validPaths = new HashSet<string>(
                resolved.assetPaths,
                StringComparer.OrdinalIgnoreCase);
            selectedPaths.RemoveWhere(path => !validPaths.Contains(path));
        }

        private void SelectTextureCategoryMember(
            TextureCategoryRecord category,
            IList<string> assetPaths,
            int index,
            bool actionKey,
            bool shiftKey)
        {
            if (category == null || assetPaths == null || index < 0 || index >= assetPaths.Count)
                return;

            HashSet<string> selectedPaths = GetSelectedTextureCategoryMemberPaths(category);
            string path = assetPaths[index];
            lastSelectedTextureCategoryMemberPaths.TryGetValue(
                category.id,
                out string lastSelectedPath);
            if (shiftKey && !string.IsNullOrEmpty(lastSelectedPath))
            {
                int anchorIndex = assetPaths.IndexOf(lastSelectedPath);
                if (anchorIndex >= 0)
                {
                    if (!actionKey)
                        selectedPaths.Clear();
                    int start = Mathf.Min(anchorIndex, index);
                    int end = Mathf.Max(anchorIndex, index);
                    for (int rangeIndex = start; rangeIndex <= end; rangeIndex++)
                        selectedPaths.Add(assetPaths[rangeIndex]);
                    lastSelectedTextureCategoryMemberPaths[category.id] = path;
                    Repaint();
                    return;
                }
            }

            if (actionKey)
            {
                if (!selectedPaths.Add(path))
                    selectedPaths.Remove(path);
            }
            else
            {
                selectedPaths.Clear();
                selectedPaths.Add(path);
            }

            lastSelectedTextureCategoryMemberPaths[category.id] = path;
            Repaint();
        }

        private void RemoveTextureCategoryMembers(
            TextureCategoryProjectSettings settings,
            TextureCategoryRecord category,
            IList<string> paths)
        {
            if (category == null || paths == null || paths.Count == 0)
                return;

            bool changed = false;
            foreach (string path in paths)
            {
                if (string.IsNullOrEmpty(path))
                    continue;

                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid))
                    continue;

                for (int index = category.sources.Count - 1; index >= 0; index--)
                {
                    TextureCategorySourceRecord source = category.sources[index];
                    if (source != null &&
                        source.kind == TextureCategorySourceKind.Asset &&
                        source.guid == guid)
                    {
                        category.sources.RemoveAt(index);
                        changed = true;
                    }
                }

                if (!category.excludedAssetGuids.Contains(guid))
                {
                    category.excludedAssetGuids.Add(guid);
                    changed = true;
                }
            }

            if (!changed)
                return;

            settings.SaveSettings();
            MarkTextureCategoryResolutionDirty();
            if (selectedTextureCategoryMemberPaths.TryGetValue(category.id, out HashSet<string> selectedPaths))
            {
                selectedPaths.Clear();
            }
            Repaint();
        }

        private void ShowMoveTextureCategoryMenu(
            TextureCategoryProjectSettings settings,
            TextureCategoryRecord sourceCategory,
            IList<string> paths)
        {
            GenericMenu menu = new GenericMenu();
            bool hasTargetCategory = false;
            foreach (TextureCategoryRecord category in settings.categories)
            {
                if (category == null || category == sourceCategory || string.IsNullOrWhiteSpace(category.name))
                    continue;

                hasTargetCategory = true;
                TextureCategoryRecord targetCategory = category;
                menu.AddItem(
                    new GUIContent(category.name.Trim()),
                    false,
                    () => MoveTextureCategoryMembers(settings, sourceCategory, targetCategory, paths));
            }

            if (!hasTargetCategory)
                menu.AddDisabledItem(new GUIContent("没有其他可用分类"));
            menu.ShowAsContext();
        }

        private void MoveTextureCategoryMembers(
            TextureCategoryProjectSettings settings,
            TextureCategoryRecord sourceCategory,
            TextureCategoryRecord targetCategory,
            IList<string> paths)
        {
            if (sourceCategory == null || targetCategory == null || paths == null || paths.Count == 0)
                return;

            sourceCategory.EnsureObjects();
            targetCategory.EnsureObjects();
            bool changed = false;
            List<string> rejected = new List<string>();
            foreach (string path in paths)
            {
                if (!TryCreateTextureCategorySource(path, out TextureCategorySourceRecord source, out string reason) ||
                    source.kind != TextureCategorySourceKind.Asset)
                {
                    rejected.Add(path + "：" + (string.IsNullOrEmpty(reason) ? "不是可移动的单个资源" : reason));
                    continue;
                }

                for (int index = sourceCategory.sources.Count - 1; index >= 0; index--)
                {
                    TextureCategorySourceRecord existing = sourceCategory.sources[index];
                    if (existing != null &&
                        existing.kind == TextureCategorySourceKind.Asset &&
                        existing.guid == source.guid)
                    {
                        sourceCategory.sources.RemoveAt(index);
                        changed = true;
                    }
                }

                if (!sourceCategory.excludedAssetGuids.Contains(source.guid))
                {
                    sourceCategory.excludedAssetGuids.Add(source.guid);
                    changed = true;
                }

                if (targetCategory.excludedAssetGuids.Remove(source.guid))
                    changed = true;

                bool targetHasSource = targetCategory.sources.Exists(existing =>
                    existing != null &&
                    existing.kind == TextureCategorySourceKind.Asset &&
                    existing.guid == source.guid);
                if (!targetHasSource)
                {
                    targetCategory.sources.Add(source);
                    changed = true;
                }
            }

            if (changed)
            {
                settings.SaveSettings();
                MarkTextureCategoryResolutionDirty();
                if (selectedTextureCategoryMemberPaths.TryGetValue(sourceCategory.id, out HashSet<string> selectedPaths))
                    selectedPaths.Clear();
                Repaint();
            }

            if (rejected.Count > 0)
            {
                int displayCount = Mathf.Min(5, rejected.Count);
                EditorUtility.DisplayDialog(
                    "部分资源未移动",
                    string.Join("\n", rejected.GetRange(0, displayCount).ToArray()),
                    "确定");
            }
        }

        private void DrawTextureCategoryConflicts(TextureCategoryResolvedRecord resolved)
        {
            int displayCount = Mathf.Min(5, resolved.conflictPaths.Count);
            List<string> lines = new List<string>();
            for (int index = 0; index < displayCount; index++)
            {
                string path = resolved.conflictPaths[index];
                lines.Add(path + " → " + string.Join("、", GetTextureCategoryOwners(path).ToArray()));
            }

            if (resolved.conflictPaths.Count > displayCount)
                lines.Add("……以及另外 " + (resolved.conflictPaths.Count - displayCount) + " 个冲突资源");

            EditorGUILayout.HelpBox(
                "此分类包含冲突资源，应用已禁用：\n" + string.Join("\n", lines.ToArray()),
                MessageType.Error);
        }

        private List<string> GetTextureCategoryOwners(string path)
        {
            List<string> owners = new List<string>();
            foreach (TextureCategoryResolvedRecord resolved in textureCategoryResolution.categories)
            {
                if (resolved.assetPaths.Contains(path))
                    owners.Add(string.IsNullOrWhiteSpace(resolved.category.name) ? "未命名分类" : resolved.category.name.Trim());
            }

            return owners;
        }

        private bool TryValidateTextureCategoryName(
            TextureCategoryProjectSettings settings,
            TextureCategoryRecord category,
            out string error)
        {
            string normalized = category == null || category.name == null ? string.Empty : category.name.Trim();
            if (string.IsNullOrEmpty(normalized))
            {
                error = "分类名称不能为空。";
                return false;
            }

            int matchCount = 0;
            foreach (TextureCategoryRecord other in settings.categories)
            {
                if (other != null && string.Equals(
                        normalized,
                        (other.name ?? string.Empty).Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    matchCount++;
                }
            }

            if (matchCount > 1)
            {
                error = "分类名称不能与其他分类重复。";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private bool CanApplyTextureCategory(
            TextureCategoryProjectSettings settings,
            TextureCategoryRecord category,
            TextureCategoryResolvedRecord resolved)
        {
            return resolved != null &&
                   resolved.textureCount > 0 &&
                   resolved.conflictPaths.Count == 0 &&
                   TryValidateTextureCategoryName(settings, category, out _) &&
                   IsTextureImporterPreset(category.textureImporterPreset);
        }

        private bool CanApplyAllTextureCategories(TextureCategoryProjectSettings settings)
        {
            if (textureCategoryResolution == null || textureCategoryResolution.HasConflicts)
                return false;

            bool hasTargets = false;
            foreach (TextureCategoryResolvedRecord resolved in textureCategoryResolution.categories)
            {
                TextureCategoryRecord category = resolved.category;
                if (!TryValidateTextureCategoryName(settings, category, out _))
                    return false;
                if (resolved.textureCount == 0)
                    continue;

                hasTargets = true;
                if (!IsTextureImporterPreset(category.textureImporterPreset))
                    return false;
            }

            return hasTargets;
        }

        private void ApplySingleTextureCategory(
            TextureCategoryRecord category,
            TextureCategoryResolvedRecord resolved)
        {
            TextureCategoryApplyJob job = CreateTextureCategoryApplyJob(category, resolved);
            bool confirmed = EditorUtility.DisplayDialog(
                "应用贴图分类",
                "分类：“" + job.categoryName + "”\n" +
                "即将应用 TextureImporter Preset 到 " + resolved.textureCount + " 张图片。",
                "应用",
                "取消");
            if (!confirmed)
                return;

            ApplyTextureCategoryJobs(new List<TextureCategoryApplyJob> { job }, "应用贴图分类");
        }

        private void ApplyAllTextureCategories(TextureCategoryProjectSettings settings)
        {
            EnsureTextureCategoryResolution(settings);
            if (!CanApplyAllTextureCategories(settings))
                return;

            List<TextureCategoryApplyJob> jobs = new List<TextureCategoryApplyJob>();
            int textureCount = 0;
            foreach (TextureCategoryResolvedRecord resolved in textureCategoryResolution.categories)
            {
                if (resolved.textureCount == 0)
                    continue;

                TextureCategoryApplyJob job = CreateTextureCategoryApplyJob(resolved.category, resolved);
                jobs.Add(job);
                textureCount += resolved.textureCount;
            }

            string confirmation =
                "即将把各分类的 TextureImporter Preset 应用到 " + textureCount + " 张图片。";

            if (!EditorUtility.DisplayDialog("应用全部贴图分类", confirmation, "应用全部", "取消"))
                return;

            ApplyTextureCategoryJobs(jobs, "应用全部贴图分类");
        }

        private TextureCategoryApplyJob CreateTextureCategoryApplyJob(
            TextureCategoryRecord category,
            TextureCategoryResolvedRecord resolved)
        {
            return new TextureCategoryApplyJob
            {
                categoryName = category.name.Trim(),
                assetPaths = new List<string>(resolved.assetPaths),
                textureImporterPreset = category.textureImporterPreset
            };
        }

        private void ApplyTextureCategoryJobs(List<TextureCategoryApplyJob> jobs, string operationTitle)
        {
            int totalAssets = 0;
            foreach (TextureCategoryApplyJob job in jobs)
                totalAssets += job.assetPaths.Count;

            int processedTextureCount = 0;
            int upToDateTextureCount = 0;
            int skippedCount = 0;
            int failedCount = 0;
            int progressIndex = 0;
            bool canceled = false;
            List<string> errors = new List<string>();

            try
            {
                foreach (TextureCategoryApplyJob job in jobs)
                {
                    foreach (string path in job.assetPaths)
                    {
                        canceled = EditorUtility.DisplayCancelableProgressBar(
                            operationTitle,
                            job.categoryName + "  " + Path.GetFileName(path),
                            totalAssets == 0 ? 1f : (float)progressIndex / totalAssets);
                        if (canceled)
                            break;

                        progressIndex++;
                        try
                        {
                            AssetImporter importer = AssetImporter.GetAtPath(path);
                            if (importer is TextureImporter textureImporter)
                            {
                                if (!job.textureImporterPreset.CanBeAppliedTo(textureImporter))
                                {
                                    skippedCount++;
                                    continue;
                                }

                                if (job.textureImporterPreset.DataEquals(textureImporter))
                                {
                                    upToDateTextureCount++;
                                    continue;
                                }

                                job.textureImporterPreset.ApplyTo(textureImporter);
                                textureImporter.SaveAndReimport();
                                processedTextureCount++;
                            }
                            else
                            {
                                skippedCount++;
                            }
                        }
                        catch (Exception exception)
                        {
                            failedCount++;
                            errors.Add(job.categoryName + " / " + path + "\n" + exception.Message);
                            Debug.LogException(exception);
                        }
                    }

                    if (canceled)
                        break;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                MarkTextureCategoryResolutionDirty();
            }

            string message =
                "已应用图片: " + processedTextureCount + "\n" +
                "已是最新设置: " + upToDateTextureCount + "\n" +
                "无法应用/已跳过: " + skippedCount + "\n" +
                "失败: " + failedCount;
            if (canceled)
                message += "\n\n处理已取消，部分资源尚未处理。";

            if (errors.Count > 0)
            {
                int shownCount = Mathf.Min(5, errors.Count);
                message += "\n\n前几条错误:\n" + string.Join(
                    "\n\n",
                    errors.GetRange(0, shownCount).ToArray());
                if (errors.Count > shownCount)
                    message += "\n\n其余 " + (errors.Count - shownCount) + " 条请查看 Console。";
            }

            Debug.Log("[TextureTools] " + operationTitle + "\n" + message);
            EditorUtility.DisplayDialog(operationTitle + "结果", message, "确定");
        }

        private static string GenerateUniqueTextureCategoryName(IList<TextureCategoryRecord> categories)
        {
            const string baseName = "新分类";
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TextureCategoryRecord category in categories)
            {
                if (category != null && !string.IsNullOrWhiteSpace(category.name))
                    names.Add(category.name.Trim());
            }

            if (!names.Contains(baseName))
                return baseName;

            int suffix = 2;
            while (names.Contains(baseName + " " + suffix))
                suffix++;
            return baseName + " " + suffix;
        }
    }
}
#endif
