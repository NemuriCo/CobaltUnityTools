#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Presets;

namespace SleepyCobalt.Tools.TextureTools
{
    internal sealed class CobaltTextureImporterPresetInfo
    {
        internal readonly string assetPath;
        internal readonly string displayName;
        internal readonly Preset preset;

        internal CobaltTextureImporterPresetInfo(string assetPath, string displayName, Preset preset)
        {
            this.assetPath = assetPath;
            this.displayName = displayName;
            this.preset = preset;
        }
    }

    internal static class CobaltTextureImporterPresetCatalog
    {
        internal const string PackagePresetRoot =
            "Packages/com.sleepycobalt.tools/Editor/Presets/TextureImporter";

        private static readonly List<CobaltTextureImporterPresetInfo> cachedPresets =
            new List<CobaltTextureImporterPresetInfo>();
        private static bool cacheValid;

        static CobaltTextureImporterPresetCatalog()
        {
            EditorApplication.projectChanged += Invalidate;
        }

        internal static IList<CobaltTextureImporterPresetInfo> GetPresets()
        {
            if (!cacheValid)
                RebuildCache();

            return cachedPresets;
        }

        internal static void Invalidate()
        {
            cacheValid = false;
            cachedPresets.Clear();
        }

        internal static bool IsTextureImporterPreset(Preset preset)
        {
            return preset != null &&
                   string.Equals(
                       preset.GetTargetFullTypeName(),
                       typeof(TextureImporter).FullName,
                       StringComparison.Ordinal);
        }

        private static void RebuildCache()
        {
            cachedPresets.Clear();
            cacheValid = true;

            if (!AssetDatabase.IsValidFolder(PackagePresetRoot))
                return;

            string[] guids = AssetDatabase.FindAssets("t:Preset", new[] { PackagePresetRoot });
            Array.Sort(guids, StringComparer.Ordinal);

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath) ||
                    !string.Equals(Path.GetExtension(assetPath), ".preset", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Preset preset = AssetDatabase.LoadAssetAtPath<Preset>(assetPath);
                if (!IsTextureImporterPreset(preset))
                    continue;

                cachedPresets.Add(new CobaltTextureImporterPresetInfo(
                    assetPath,
                    GetDisplayName(preset),
                    preset));
            }

            cachedPresets.Sort(ComparePresets);
        }

        private static int ComparePresets(
            CobaltTextureImporterPresetInfo left,
            CobaltTextureImporterPresetInfo right)
        {
            int nameComparison = string.Compare(
                left.displayName,
                right.displayName,
                StringComparison.OrdinalIgnoreCase);
            return nameComparison != 0
                ? nameComparison
                : string.Compare(left.assetPath, right.assetPath, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetDisplayName(Preset preset)
        {
            if (preset == null)
                return string.Empty;

            string originalName = preset.name;
            string displayName = originalName;
            string[] prefixes = { "Cobalt_", "TextureImporter_" };
            bool removedPrefix;
            do
            {
                removedPrefix = false;
                foreach (string prefix in prefixes)
                {
                    if (!displayName.StartsWith(prefix, StringComparison.Ordinal))
                        continue;

                    displayName = displayName.Substring(prefix.Length);
                    removedPrefix = true;
                }
            }
            while (removedPrefix);

            return string.IsNullOrEmpty(displayName) ? originalName : displayName;
        }
    }
}
#endif
