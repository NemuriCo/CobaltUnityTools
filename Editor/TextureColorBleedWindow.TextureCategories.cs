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
        [SerializeField] private Vector2 textureCategoriesScrollPosition;
        [SerializeField] private bool showUngroupedTextures = true;
        [SerializeField] private bool showIgnoredTextureRules;

        private TextureCategoryResolutionSet textureCategoryResolution;
        private bool textureCategoryResolutionDirty = true;

        private sealed class TextureCategoryApplyJob
        {
            internal string categoryName;
            internal List<string> assetPaths;
            internal TextureCommonSettingsSnapshot commonSettings;
            internal TexturePlatformSettingsSnapshot defaultSettings;
            internal TexturePlatformSettingsSnapshot standaloneSettings;
            internal TexturePlatformSettingsSnapshot androidSettings;
            internal bool usesCachedFallback;
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
        }

        private void DrawTextureCategoriesPage()
        {
            EnsureImagePresetsLoaded();
            TextureCategoryProjectSettings settings = GetTextureCategorySettings();
            SyncAllTextureCategoryPresetCaches(settings);
            EnsureTextureCategoryResolution(settings);

            textureCategoriesScrollPosition = EditorGUILayout.BeginScrollView(textureCategoriesScrollPosition);
            EditorGUILayout.LabelField("贴图分类", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "创建分类并绑定完整图像预设。可从 Project 窗口拖入图片、SpriteAtlas 或文件夹；" +
                "文件夹会递归包含以后新增的资源。设置只会在点击应用按钮后写入。",
                MessageType.Info);

            DrawTextureCategoryToolbar(settings);
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
                    "还没有分类。点击“新建分类”，绑定预设后把图片或文件夹拖入分类。",
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
            if (ungroupedCount > 0)
            {
                EditorGUILayout.HelpBox(
                    "还有 " + textureCategoryResolution.ungroupedTextureCount + " 张图片和 " +
                    textureCategoryResolution.ungroupedAtlasCount +
                    " 个图集未加入任何分类，也没有被忽略。",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox("当前 Assets 下的图片和图集都已分类或忽略。", MessageType.Info);
            }

            showUngroupedTextures = EditorGUILayout.Foldout(
                showUngroupedTextures,
                "未分组贴图（" + ungroupedCount + "）",
                true);
            if (!showUngroupedTextures || ungroupedCount == 0)
                return;

            const int maxVisibleAssets = 200;
            int visibleCount = Mathf.Min(maxVisibleAssets, ungroupedCount);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            for (int index = 0; index < visibleCount; index++)
            {
                string path = textureCategoryResolution.ungroupedAssetPaths[index];
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(
                        AssetDatabase.LoadMainAssetAtPath(path),
                        typeof(UnityEngine.Object),
                        false);
                }

                if (GUILayout.Button("加入分类…", GUILayout.Width(72f)))
                    ShowAddUngroupedTextureMenu(settings, path);
                if (GUILayout.Button("忽略", GUILayout.Width(42f)))
                    AddIgnoredTextureSource(settings, path);
                if (GUILayout.Button("忽略文件夹", GUILayout.Width(72f)))
                    AddIgnoredTextureFolder(settings, path);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField(path, EditorStyles.miniLabel);
            }

            if (ungroupedCount > maxVisibleAssets)
            {
                EditorGUILayout.LabelField(
                    "仅显示前 " + maxVisibleAssets + " 个，另外 " +
                    (ungroupedCount - maxVisibleAssets) + " 个仍计入未分组提示。",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
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

        private void ShowAddUngroupedTextureMenu(TextureCategoryProjectSettings settings, string path)
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
                    () => AddUngroupedTextureToCategory(settings, targetCategory, path));
            }

            if (!hasCategory)
                menu.AddDisabledItem(new GUIContent("请先新建分类"));
            menu.ShowAsContext();
        }

        private void AddUngroupedTextureToCategory(
            TextureCategoryProjectSettings settings,
            TextureCategoryRecord category,
            string path)
        {
            if (!TryCreateTextureCategorySource(path, out TextureCategorySourceRecord source, out string reason))
            {
                EditorUtility.DisplayDialog("无法加入分类", reason, "确定");
                return;
            }

            bool duplicate = category.sources.Exists(item =>
                item != null && item.kind == source.kind && item.guid == source.guid);
            if (!duplicate)
            {
                category.sources.Add(source);
                settings.SaveSettings();
                MarkTextureCategoryResolutionDirty();
                Repaint();
            }
        }

        private void AddIgnoredTextureSource(TextureCategoryProjectSettings settings, string path)
        {
            AddIgnoredTextureRule(settings, path);
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
            GUI.Box(dropArea, "拖入要忽略的图片、SpriteAtlas 或文件夹", EditorStyles.helpBox);

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
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("新建分类"))
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

            if (GUILayout.Button("刷新成员"))
                MarkTextureCategoryResolutionDirty();

            EnsureTextureCategoryResolution(settings);
            using (new EditorGUI.DisabledScope(!CanApplyAllTextureCategories(settings)))
            {
                if (GUILayout.Button("应用全部分类", GUILayout.Height(24f)))
                    ApplyAllTextureCategories(settings);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6f);
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
                resolved.textureCount + " 张图片，" + resolved.atlasCount + " 个图集");

            if (resolved.missingSources.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    "有 " + resolved.missingSources.Count + " 个来源已失效或不再是支持的资源，请从来源列表中移除或重新拖入。",
                    MessageType.Warning);
            }

            if (resolved.conflictPaths.Count > 0)
                DrawTextureCategoryConflicts(resolved);

            bool showMembers = EditorGUILayout.Foldout(
                category.showResolvedMembers,
                "查看解析成员（" + resolved.assetPaths.Count + "）",
                true);
            if (showMembers != category.showResolvedMembers)
            {
                category.showResolvedMembers = showMembers;
                settings.SaveSettings();
            }

            if (category.showResolvedMembers)
                DrawResolvedTextureCategoryMembers(resolved);

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
            List<TextureImagePresetRecord> completePresets = GetCompleteImagePresets();
            TextureImagePresetRecord livePreset = FindCompleteImagePreset(category.presetId);
            bool hasMissingLink = !string.IsNullOrEmpty(category.presetId) && livePreset == null;

            List<string> labels = new List<string> { "未选择" };
            List<TextureImagePresetRecord> mappedPresets = new List<TextureImagePresetRecord> { null };
            int selectedIndex = 0;

            if (hasMissingLink)
            {
                labels.Add("缺失：" + (string.IsNullOrEmpty(category.presetName) ? category.presetId : category.presetName));
                mappedPresets.Add(null);
                selectedIndex = 1;
            }

            foreach (TextureImagePresetRecord preset in completePresets)
            {
                labels.Add(preset.name);
                mappedPresets.Add(preset);
                if (preset.id == category.presetId)
                    selectedIndex = mappedPresets.Count - 1;
            }

            int newIndex = EditorGUILayout.Popup("图像设置预设", selectedIndex, labels.ToArray());
            if (newIndex != selectedIndex)
            {
                if (newIndex == 0)
                {
                    category.presetId = string.Empty;
                    category.presetName = string.Empty;
                    category.cachedPresetRevision = -1;
                    category.hasCachedPreset = false;
                }
                else if (mappedPresets[newIndex] != null)
                {
                    category.CachePreset(mappedPresets[newIndex]);
                }

                settings.SaveSettings();
                livePreset = FindCompleteImagePreset(category.presetId);
                hasMissingLink = !string.IsNullOrEmpty(category.presetId) && livePreset == null;
            }

            if (livePreset != null)
            {
                EditorGUILayout.LabelField("预设状态", "实时跟随“" + livePreset.name + "”", EditorStyles.miniLabel);
            }
            else if (hasMissingLink && category.HasUsableCachedPreset)
            {
                EditorGUILayout.HelpBox(
                    "本机找不到原预设“" + category.presetName + "”，应用时会使用分类中缓存的最近设置。可从下拉框重新关联。",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox("请选择一个完整图像预设。", MessageType.Error);
            }
        }

        private void DrawTextureCategoryDropArea(
            TextureCategoryProjectSettings settings,
            TextureCategoryRecord category)
        {
            Rect dropArea = GUILayoutUtility.GetRect(0f, 54f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "从 Project 拖入图片、SpriteAtlas 或文件夹", EditorStyles.helpBox);

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
                reason = "不是图片或 SpriteAtlas";
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
            if (category.sources.Count == 0)
            {
                EditorGUILayout.LabelField("尚未添加来源。", EditorStyles.miniLabel);
                return;
            }

            HashSet<TextureCategorySourceRecord> missing =
                new HashSet<TextureCategorySourceRecord>(resolved.missingSources);
            for (int index = 0; index < category.sources.Count; index++)
            {
                TextureCategorySourceRecord source = category.sources[index];
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
        }

        private void DrawResolvedTextureCategoryMembers(TextureCategoryResolvedRecord resolved)
        {
            const int maxVisibleMembers = 100;
            int visibleCount = Mathf.Min(maxVisibleMembers, resolved.assetPaths.Count);
            using (new EditorGUI.IndentLevelScope())
            {
                for (int index = 0; index < visibleCount; index++)
                {
                    string path = resolved.assetPaths[index];
                    EditorGUILayout.BeginHorizontal();
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.ObjectField(
                            AssetDatabase.LoadMainAssetAtPath(path),
                            typeof(UnityEngine.Object),
                            false);
                    }
                    GUILayout.Label(path, EditorStyles.miniLabel, GUILayout.MaxWidth(300f));
                    EditorGUILayout.EndHorizontal();
                }
            }

            if (resolved.assetPaths.Count > maxVisibleMembers)
            {
                EditorGUILayout.LabelField(
                    "仅显示前 " + maxVisibleMembers + " 个成员，其余 " +
                    (resolved.assetPaths.Count - maxVisibleMembers) + " 个仍会正常处理。",
                    EditorStyles.miniLabel);
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
                   resolved.assetPaths.Count > 0 &&
                   resolved.conflictPaths.Count == 0 &&
                   TryValidateTextureCategoryName(settings, category, out _) &&
                   !string.IsNullOrEmpty(category.presetId) &&
                   category.HasUsableCachedPreset;
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
                if (resolved.assetPaths.Count == 0)
                    continue;

                hasTargets = true;
                if (string.IsNullOrEmpty(category.presetId) || !category.HasUsableCachedPreset)
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
                "即将处理 " + resolved.textureCount + " 张图片和 " + resolved.atlasCount + " 个图集。" +
                (job.usesCachedFallback ? "\n\n本机缺少原预设，将使用分类缓存设置。" : string.Empty),
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
            int atlasCount = 0;
            int fallbackCount = 0;
            foreach (TextureCategoryResolvedRecord resolved in textureCategoryResolution.categories)
            {
                if (resolved.assetPaths.Count == 0)
                    continue;

                TextureCategoryApplyJob job = CreateTextureCategoryApplyJob(resolved.category, resolved);
                jobs.Add(job);
                textureCount += resolved.textureCount;
                atlasCount += resolved.atlasCount;
                if (job.usesCachedFallback)
                    fallbackCount++;
            }

            string confirmation =
                "即将应用 " + jobs.Count + " 个分类，处理 " + textureCount + " 张图片和 " + atlasCount + " 个图集。";
            if (fallbackCount > 0)
                confirmation += "\n\n其中 " + fallbackCount + " 个分类会使用缓存的预设设置。";

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
                commonSettings = category.cachedCommonSettings.Clone(),
                defaultSettings = category.cachedDefaultSettings.Clone(),
                standaloneSettings = category.cachedStandaloneSettings.Clone(),
                androidSettings = category.cachedAndroidSettings.Clone(),
                usesCachedFallback = FindCompleteImagePreset(category.presetId) == null
            };
        }

        private void ApplyTextureCategoryJobs(List<TextureCategoryApplyJob> jobs, string operationTitle)
        {
            int totalAssets = 0;
            foreach (TextureCategoryApplyJob job in jobs)
                totalAssets += job.assetPaths.Count;

            int processedTextureCount = 0;
            int processedAtlasCount = 0;
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
                                TextureImageSettingsUtility.ApplyTextureImporter(
                                    textureImporter,
                                    job.commonSettings,
                                    job.defaultSettings,
                                    job.standaloneSettings,
                                    job.androidSettings);
                                textureImporter.SaveAndReimport();
                                processedTextureCount++;
                            }
                            else if (IsSpriteAtlasImporter(importer))
                            {
                                TextureImageSettingsUtility.ApplySpriteAtlas(
                                    importer,
                                    job.defaultSettings,
                                    job.standaloneSettings,
                                    job.androidSettings);
                                importer.SaveAndReimport();
                                processedAtlasCount++;
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
                "已处理图片: " + processedTextureCount + "\n" +
                "已处理图集: " + processedAtlasCount + "\n" +
                "已跳过: " + skippedCount + "\n" +
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

        private void SyncAllTextureCategoryPresetCaches(TextureCategoryProjectSettings settings)
        {
            bool changed = false;
            foreach (TextureCategoryRecord category in settings.categories)
            {
                TextureImagePresetRecord preset = FindCompleteImagePreset(category.presetId);
                if (preset == null)
                    continue;

                if (!category.HasUsableCachedPreset ||
                    category.cachedPresetRevision != preset.revision ||
                    category.presetName != preset.name)
                {
                    category.CachePreset(preset);
                    changed = true;
                }
            }

            if (changed)
                settings.SaveSettings();
        }

        private void SyncTextureCategoriesForPreset(TextureImagePresetRecord preset)
        {
            if (preset == null || string.IsNullOrEmpty(preset.id) ||
                !preset.hasCommonSettings || preset.commonSettings == null)
            {
                return;
            }

            TextureCategoryProjectSettings settings = TextureCategoryProjectSettings.instance;
            if (settings.EnsureIntegrity())
                settings.SaveSettings();

            bool changed = false;
            foreach (TextureCategoryRecord category in settings.categories)
            {
                if (category.presetId != preset.id)
                    continue;

                category.CachePreset(preset);
                changed = true;
            }

            if (changed)
                settings.SaveSettings();
        }

        private TextureImagePresetRecord FindCompleteImagePreset(string presetId)
        {
            if (string.IsNullOrEmpty(presetId))
                return null;

            EnsureImagePresetsLoaded();
            return imagePresetCollection.presets.Find(preset =>
                preset != null &&
                preset.id == presetId &&
                preset.hasCommonSettings &&
                preset.commonSettings != null);
        }

        private List<TextureImagePresetRecord> GetCompleteImagePresets()
        {
            EnsureImagePresetsLoaded();
            List<TextureImagePresetRecord> presets = imagePresetCollection.presets.FindAll(preset =>
                preset != null && preset.hasCommonSettings && preset.commonSettings != null);
            presets.Sort((left, right) => string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase));
            return presets;
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
