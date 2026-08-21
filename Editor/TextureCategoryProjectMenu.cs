#if UNITY_EDITOR
using UnityEditor;

namespace SleepyCobalt.Tools.TextureTools
{
    internal static class TextureCategoryProjectMenu
    {
        private const string LocateMenuPath = "Assets/定位到贴图分组";

        [MenuItem(LocateMenuPath, false, 2000)]
        private static void LocateSelectedTexture()
        {
            if (!TryGetSelectedTexturePath(out string assetPath))
                return;

            TextureColorBleedWindow.OpenAndLocateTexture(assetPath);
        }

        [MenuItem(LocateMenuPath, true)]
        private static bool ValidateLocateSelectedTexture()
        {
            return TryGetSelectedTexturePath(out _);
        }

        private static bool TryGetSelectedTexturePath(out string assetPath)
        {
            assetPath = string.Empty;
            if (Selection.objects == null || Selection.objects.Length != 1 ||
                Selection.assetGUIDs == null || Selection.assetGUIDs.Length != 1)
            {
                return false;
            }

            string path = AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs[0]);
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
                return false;

            if (!TextureCategoryResolver.TryClassifySupportedAsset(
                    path,
                    out bool isTexture,
                    out _)
                || !isTexture)
            {
                return false;
            }

            assetPath = path.Replace('\\', '/');
            return true;
        }
    }
}
#endif
