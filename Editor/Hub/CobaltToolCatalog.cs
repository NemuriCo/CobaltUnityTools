using System.Collections.Generic;

namespace SleepyCobalt.Tools
{
    /// <summary>
    /// Describes the user-facing tools in the package without owning their implementation.
    /// Add one entry here when a new tool needs to appear in the Hub.
    /// </summary>
    internal static class CobaltToolCatalog
    {
        private static readonly CobaltToolDefinition[] ToolDefinitions =
        {
            new CobaltToolDefinition(
                "骨骼组选择器",
                "场景",
                "在 Scene View 中为带有 Root 和方向命名骨骼的角色显示方向手柄，可快速选择或移动骨骼组。",
                "Scene View：选中符合条件的 GameObject 后自动显示手柄",
                null,
                "需要选中带有名为 Root 的直接子物体，并且 Root 下有方向命名骨骼的 GameObject。",
                false,
                CobaltToolSelectionRequirement.HierarchyGameObject),

            new CobaltToolDefinition(
                "复制材质",
                "材质",
                "复制选中 GameObject 及其子物体 Renderer 使用的材质，并重新绑定复制出的材质。",
                "GameObject > 「复制材质」",
                "GameObject/「复制材质」",
                "需要在 Hierarchy 中选中至少一个 GameObject。",
                true,
                CobaltToolSelectionRequirement.HierarchyGameObject),

            new CobaltToolDefinition(
                "复制材质和贴图",
                "材质",
                "复制选中 GameObject 使用的材质及其纹理，并将 Renderer 重新绑定到复制出的资源。",
                "GameObject > 「复制材质和贴图」",
                "GameObject/「复制材质和贴图」",
                "需要在 Hierarchy 中选中至少一个 GameObject。",
                true,
                CobaltToolSelectionRequirement.HierarchyGameObject),

            new CobaltToolDefinition(
                "删除选中物体 Missing 脚本",
                "资源",
                "递归清理选中 Prefab 资源中的 Missing MonoBehaviour，并保存修改后的 Prefab。",
                "Project 右键 > 「删除选中物体Missing脚本」",
                "Assets/「删除选中物体Missing脚本」",
                "需要在 Project 窗口选中一个或多个可编辑的 Prefab 资源。",
                true,
                CobaltToolSelectionRequirement.PrefabAsset),

            new CobaltToolDefinition(
                "查找 Mesh Renderer",
                "场景",
                "扫描当前场景（包含未激活对象），并选中所有带 MeshRenderer 的 GameObject。",
                "CobaltTools > 查找Mesh Renderer",
                "CobaltTools/查找Mesh Renderer",
                "不需要预先选择对象。",
                true,
                CobaltToolSelectionRequirement.None),

            new CobaltToolDefinition(
                "查找 Skinned Mesh Renderer",
                "场景",
                "扫描当前场景（包含未激活对象），并选中所有带 SkinnedMeshRenderer 的 GameObject。",
                "CobaltTools > 查找Skinned Mesh Renderer",
                "CobaltTools/查找Skinned Mesh Renderer",
                "不需要预先选择对象。",
                true,
                CobaltToolSelectionRequirement.None),

            new CobaltToolDefinition(
                "一键设置法线贴图",
                "贴图",
                "扫描项目中的 Texture2D，将文件名以 _Normal 结尾且尚未设置为 Normal Map 的贴图批量转换。",
                "CobaltTools > 一键设置法线贴图",
                "CobaltTools/一键设置法线贴图",
                "不需要预先选择对象；工具会扫描整个项目并在执行前显示确认对话框。",
                true,
                CobaltToolSelectionRequirement.None),

            new CobaltToolDefinition(
                "资源依赖分析",
                "资源",
                "分析指定素材包目录中的资源依赖，查看疑似未使用、被保留依赖和 Demo/Example 资源。",
                "CobaltTools > 资源依赖分析",
                "CobaltTools/资源依赖分析",
                "打开窗口后需要指定至少一个保留入口和一个扫描目录。",
                true,
                CobaltToolSelectionRequirement.None),

            new CobaltToolDefinition(
                "贴图工具",
                "贴图",
                "在一个窗口中处理颜色溢出、Mipmap 开关，以及 Default、PC、Android 平台贴图压缩设置。",
                "CobaltTools > 贴图工具",
                "CobaltTools/贴图工具",
                "打开窗口后根据所选页面，需要在 Project 中选择 PNG、贴图或贴图文件夹。",
                true,
                CobaltToolSelectionRequirement.None),

            new CobaltToolDefinition(
                "从贴图创建 ToonShader 材质",
                "材质",
                "根据选中的贴图及其命名后缀，在贴图同目录生成并配置 ToonShader 材质。",
                "Project 右键 > 从贴图创建ToonShader材质",
                "Assets/从贴图创建ToonShader材质",
                "需要在 Project 窗口选中一个或多个 Texture，并且项目中存在 Toon Shader。",
                true,
                CobaltToolSelectionRequirement.TextureAsset)
        };

        public static IReadOnlyList<CobaltToolDefinition> Tools
        {
            get { return ToolDefinitions; }
        }
    }

    internal enum CobaltToolSelectionRequirement
    {
        None,
        HierarchyGameObject,
        TextureAsset,
        PrefabAsset,
        ProjectAsset
    }

    internal sealed class CobaltToolDefinition
    {
        public readonly string Name;
        public readonly string Category;
        public readonly string Description;
        public readonly string EntryPath;
        public readonly string MenuPath;
        public readonly string UsageRequirement;
        public readonly bool CanExecuteFromHub;
        public readonly CobaltToolSelectionRequirement SelectionRequirement;

        public CobaltToolDefinition(
            string name,
            string category,
            string description,
            string entryPath,
            string menuPath,
            string usageRequirement,
            bool canExecuteFromHub,
            CobaltToolSelectionRequirement selectionRequirement)
        {
            Name = name;
            Category = category;
            Description = description;
            EntryPath = entryPath;
            MenuPath = menuPath;
            UsageRequirement = usageRequirement;
            CanExecuteFromHub = canExecuteFromHub;
            SelectionRequirement = selectionRequirement;
        }
    }
}
