using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SleepyCobalt.Tools
{
    public sealed class CobaltToolsWindow : EditorWindow
    {
        private const string AllCategoriesLabel = "全部";

        private readonly List<CobaltToolDefinition> filteredTools = new List<CobaltToolDefinition>();
        private string searchText = string.Empty;
        private string[] categoryOptions = { AllCategoriesLabel };
        private int selectedCategoryIndex;
        private Vector2 scrollPosition;

        [MenuItem("CobaltTools/工具总览", false, 0)]
        private static void Open()
        {
            CobaltToolsWindow window = GetWindow<CobaltToolsWindow>();
            window.titleContent = new GUIContent("Cobalt Tools");
            window.minSize = new Vector2(480f, 360f);
            window.Show();
        }

        private void OnEnable()
        {
            BuildCategoryOptions();
        }

        private void OnGUI()
        {
            DrawToolbar();
            UpdateFilteredTools();

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(
                string.Format("工具 {0}/{1}", filteredTools.Count, CobaltToolCatalog.Tools.Count),
                EditorStyles.miniLabel);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            foreach (CobaltToolDefinition tool in filteredTools)
                DrawToolEntry(tool);

            if (filteredTools.Count == 0)
            {
                EditorGUILayout.HelpBox("没有匹配的工具。请尝试其他搜索词或分类。", MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUI.SetNextControlName("CobaltToolsSearchField");
            searchText = EditorGUILayout.TextField(
                searchText,
                EditorStyles.toolbarSearchField,
                GUILayout.MinWidth(150f));

            if (GUILayout.Button("清除", EditorStyles.toolbarButton, GUILayout.Width(42f)))
            {
                searchText = string.Empty;
                GUI.FocusControl(string.Empty);
            }

            GUILayout.FlexibleSpace();
            selectedCategoryIndex = EditorGUILayout.Popup(
                selectedCategoryIndex,
                categoryOptions,
                EditorStyles.toolbarPopup,
                GUILayout.Width(100f));

            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolEntry(CobaltToolDefinition tool)
        {
            bool selectionReady = IsSelectionReady(tool.SelectionRequirement);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(tool.Name, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            if (tool.CanExecuteFromHub)
            {
                using (new EditorGUI.DisabledScope(!selectionReady))
                {
                    if (GUILayout.Button("运行", GUILayout.Width(52f)))
                        ExecuteTool(tool);
                }
            }
            else
            {
                GUILayout.Label("仅查看", EditorStyles.miniLabel, GUILayout.Width(48f));
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(tool.Description, EditorStyles.wordWrappedLabel);
            DrawMetadata("分类", tool.Category);
            DrawMetadata("入口", tool.EntryPath);
            DrawMetadata("需要", tool.UsageRequirement);

            if (tool.CanExecuteFromHub && !selectionReady)
                EditorGUILayout.HelpBox("当前选择不满足运行条件。", MessageType.Warning);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3f);
        }

        private static void DrawMetadata(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label + "：", EditorStyles.miniLabel, GUILayout.Width(38f));
            EditorGUILayout.LabelField(value, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void ExecuteTool(CobaltToolDefinition tool)
        {
            if (string.IsNullOrEmpty(tool.MenuPath))
                return;

            bool executed = EditorApplication.ExecuteMenuItem(tool.MenuPath);
            if (!executed)
            {
                Debug.LogWarning("Cobalt Tools Hub 无法执行菜单：" + tool.MenuPath);
                ShowNotification(new GUIContent("无法执行：" + tool.Name));
            }
        }

        private void BuildCategoryOptions()
        {
            List<string> categories = new List<string> { AllCategoriesLabel };
            foreach (CobaltToolDefinition tool in CobaltToolCatalog.Tools)
            {
                if (!categories.Contains(tool.Category))
                    categories.Add(tool.Category);
            }

            categories.Sort(1, categories.Count - 1, StringComparer.Ordinal);
            categoryOptions = categories.ToArray();
            selectedCategoryIndex = Mathf.Clamp(selectedCategoryIndex, 0, categoryOptions.Length - 1);
        }

        private void UpdateFilteredTools()
        {
            filteredTools.Clear();
            string category = categoryOptions[selectedCategoryIndex];

            foreach (CobaltToolDefinition tool in CobaltToolCatalog.Tools)
            {
                if (category != AllCategoriesLabel && !string.Equals(tool.Category, category, StringComparison.Ordinal))
                    continue;

                if (!MatchesSearch(tool))
                    continue;

                filteredTools.Add(tool);
            }
        }

        private bool MatchesSearch(CobaltToolDefinition tool)
        {
            if (string.IsNullOrWhiteSpace(searchText))
                return true;

            return ContainsIgnoreCase(tool.Name, searchText) ||
                   ContainsIgnoreCase(tool.Description, searchText) ||
                   ContainsIgnoreCase(tool.Category, searchText) ||
                   ContainsIgnoreCase(tool.EntryPath, searchText) ||
                   ContainsIgnoreCase(tool.MenuPath, searchText);
        }

        private static bool ContainsIgnoreCase(string source, string value)
        {
            return !string.IsNullOrEmpty(source) &&
                   source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsSelectionReady(CobaltToolSelectionRequirement requirement)
        {
            switch (requirement)
            {
                case CobaltToolSelectionRequirement.None:
                    return true;
                case CobaltToolSelectionRequirement.HierarchyGameObject:
                    return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
                case CobaltToolSelectionRequirement.TextureAsset:
                    foreach (UnityEngine.Object selectedObject in Selection.objects)
                    {
                        if (selectedObject is Texture)
                            return true;
                    }

                    return false;
                case CobaltToolSelectionRequirement.PrefabAsset:
                    foreach (UnityEngine.Object selectedObject in Selection.objects)
                    {
                        string assetPath = AssetDatabase.GetAssetPath(selectedObject);
                        if (!string.IsNullOrEmpty(assetPath) &&
                            assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) &&
                            AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) != null &&
                            AssetDatabase.IsOpenForEdit(assetPath))
                        {
                            return true;
                        }
                    }

                    return false;
                case CobaltToolSelectionRequirement.ProjectAsset:
                    return Selection.assetGUIDs != null && Selection.assetGUIDs.Length > 0;
                default:
                    return false;
            }
        }
    }
}
