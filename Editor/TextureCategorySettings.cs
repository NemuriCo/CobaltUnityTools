#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SleepyCobalt.Tools.TextureTools
{
    internal enum TextureCategorySourceKind
    {
        Asset,
        Folder
    }

    [Serializable]
    internal sealed class TextureCategorySourceRecord
    {
        public TextureCategorySourceKind kind;
        public string guid;
        public string lastKnownPath;
    }

    [Serializable]
    internal sealed class TextureCategoryRecord
    {
        public string id;
        public string name;
        public bool expanded = true;
        public string presetId;
        public string presetName;
        public int cachedPresetRevision = -1;
        public bool hasCachedPreset;
        public TextureCommonSettingsSnapshot cachedCommonSettings = new TextureCommonSettingsSnapshot();
        public TexturePlatformSettingsSnapshot cachedDefaultSettings = new TexturePlatformSettingsSnapshot();
        public TexturePlatformSettingsSnapshot cachedStandaloneSettings = new TexturePlatformSettingsSnapshot();
        public TexturePlatformSettingsSnapshot cachedAndroidSettings = new TexturePlatformSettingsSnapshot();
        public List<TextureCategorySourceRecord> sources = new List<TextureCategorySourceRecord>();
        public List<string> excludedAssetGuids = new List<string>();

        internal bool HasUsableCachedPreset
        {
            get
            {
                return hasCachedPreset &&
                       cachedCommonSettings != null &&
                       cachedDefaultSettings != null &&
                       cachedStandaloneSettings != null &&
                       cachedAndroidSettings != null;
            }
        }

        internal void CachePreset(TextureImagePresetRecord preset)
        {
            if (preset == null || !preset.hasCommonSettings || preset.commonSettings == null)
                throw new ArgumentException("只能缓存完整图像预设。", nameof(preset));

            presetId = preset.id;
            presetName = preset.name;
            cachedPresetRevision = preset.revision;
            cachedCommonSettings = preset.commonSettings.Clone();
            cachedDefaultSettings = preset.defaultSettings == null
                ? new TexturePlatformSettingsSnapshot()
                : preset.defaultSettings.Clone();
            cachedStandaloneSettings = preset.standaloneSettings == null
                ? new TexturePlatformSettingsSnapshot()
                : preset.standaloneSettings.Clone();
            cachedAndroidSettings = preset.androidSettings == null
                ? new TexturePlatformSettingsSnapshot()
                : preset.androidSettings.Clone();
            hasCachedPreset = true;
        }

        internal void EnsureObjects()
        {
            if (sources == null)
                sources = new List<TextureCategorySourceRecord>();
            if (excludedAssetGuids == null)
                excludedAssetGuids = new List<string>();
            if (cachedCommonSettings == null)
                cachedCommonSettings = new TextureCommonSettingsSnapshot();
            if (cachedDefaultSettings == null)
                cachedDefaultSettings = new TexturePlatformSettingsSnapshot();
            if (cachedStandaloneSettings == null)
                cachedStandaloneSettings = new TexturePlatformSettingsSnapshot();
            if (cachedAndroidSettings == null)
                cachedAndroidSettings = new TexturePlatformSettingsSnapshot();
        }
    }

    [FilePath("ProjectSettings/CobaltTextureCategories.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class TextureCategoryProjectSettings : ScriptableSingleton<TextureCategoryProjectSettings>
    {
        public int version = 1;
        public List<TextureCategoryRecord> categories = new List<TextureCategoryRecord>();
        public List<TextureCategorySourceRecord> classificationSources = new List<TextureCategorySourceRecord>();
        public List<TextureCategorySourceRecord> ignoredSources = new List<TextureCategorySourceRecord>();

        internal bool EnsureIntegrity()
        {
            bool changed = false;
            if (categories == null)
            {
                categories = new List<TextureCategoryRecord>();
                changed = true;
            }
            if (ignoredSources == null)
            {
                ignoredSources = new List<TextureCategorySourceRecord>();
                changed = true;
            }
            if (classificationSources == null)
            {
                classificationSources = new List<TextureCategorySourceRecord>();
                changed = true;
            }

            HashSet<string> categoryIds = new HashSet<string>(StringComparer.Ordinal);
            for (int categoryIndex = 0; categoryIndex < categories.Count; categoryIndex++)
            {
                TextureCategoryRecord category = categories[categoryIndex];
                if (category == null)
                {
                    category = new TextureCategoryRecord { name = "新分类" };
                    categories[categoryIndex] = category;
                    changed = true;
                }

                if (string.IsNullOrEmpty(category.id) || !categoryIds.Add(category.id))
                {
                    category.id = Guid.NewGuid().ToString("N");
                    categoryIds.Add(category.id);
                    changed = true;
                }

                category.EnsureObjects();
                HashSet<string> sourceKeys = new HashSet<string>(StringComparer.Ordinal);
                for (int sourceIndex = category.sources.Count - 1; sourceIndex >= 0; sourceIndex--)
                {
                    TextureCategorySourceRecord source = category.sources[sourceIndex];
                    if (source == null)
                    {
                        category.sources.RemoveAt(sourceIndex);
                        changed = true;
                        continue;
                    }

                    string sourceKey = source.kind + ":" + (source.guid ?? string.Empty);
                    if (!sourceKeys.Add(sourceKey))
                    {
                        category.sources.RemoveAt(sourceIndex);
                        changed = true;
                    }
                }

                HashSet<string> excludedGuids = new HashSet<string>(StringComparer.Ordinal);
                for (int excludedIndex = category.excludedAssetGuids.Count - 1;
                     excludedIndex >= 0;
                     excludedIndex--)
                {
                    string excludedGuid = category.excludedAssetGuids[excludedIndex];
                    if (string.IsNullOrEmpty(excludedGuid) || !excludedGuids.Add(excludedGuid))
                    {
                        category.excludedAssetGuids.RemoveAt(excludedIndex);
                        changed = true;
                    }
                }
            }

            HashSet<string> classificationSourceKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int sourceIndex = classificationSources.Count - 1; sourceIndex >= 0; sourceIndex--)
            {
                TextureCategorySourceRecord source = classificationSources[sourceIndex];
                if (source == null)
                {
                    classificationSources.RemoveAt(sourceIndex);
                    changed = true;
                    continue;
                }

                string sourceKey = source.kind + ":" + (source.guid ?? string.Empty);
                if (!classificationSourceKeys.Add(sourceKey))
                {
                    classificationSources.RemoveAt(sourceIndex);
                    changed = true;
                }
            }

            HashSet<string> ignoredSourceKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int sourceIndex = ignoredSources.Count - 1; sourceIndex >= 0; sourceIndex--)
            {
                TextureCategorySourceRecord source = ignoredSources[sourceIndex];
                if (source == null)
                {
                    ignoredSources.RemoveAt(sourceIndex);
                    changed = true;
                    continue;
                }

                string sourceKey = source.kind + ":" + (source.guid ?? string.Empty);
                if (!ignoredSourceKeys.Add(sourceKey))
                {
                    ignoredSources.RemoveAt(sourceIndex);
                    changed = true;
                }
            }

            return changed;
        }

        internal void SaveSettings()
        {
            Save(true);
        }
    }

    internal sealed class TextureCategoryResolvedRecord
    {
        internal TextureCategoryRecord category;
        internal readonly List<string> assetPaths = new List<string>();
        internal readonly List<string> conflictPaths = new List<string>();
        internal readonly List<TextureCategorySourceRecord> missingSources = new List<TextureCategorySourceRecord>();
        internal int textureCount;
        internal int atlasCount;
    }

    internal sealed class TextureCategoryResolutionSet
    {
        internal readonly List<TextureCategoryResolvedRecord> categories = new List<TextureCategoryResolvedRecord>();
        internal readonly Dictionary<string, TextureCategoryResolvedRecord> byCategoryId =
            new Dictionary<string, TextureCategoryResolvedRecord>(StringComparer.Ordinal);
        internal readonly List<string> ignoredAssetPaths = new List<string>();
        internal readonly List<string> classificationAssetPaths = new List<string>();
        internal readonly List<string> ungroupedAssetPaths = new List<string>();
        internal readonly List<string> ungroupedInformativeAlphaAssetPaths = new List<string>();
        internal readonly List<string> ungroupedNonInformativeAlphaAssetPaths = new List<string>();
        internal readonly List<TextureCategorySourceRecord> missingClassificationSources =
            new List<TextureCategorySourceRecord>();
        internal readonly List<TextureCategorySourceRecord> missingIgnoredSources =
            new List<TextureCategorySourceRecord>();
        internal int classificationTextureCount;
        internal int classificationAtlasCount;
        internal int ungroupedTextureCount;
        internal int ungroupedAtlasCount;
        internal int conflictAssetCount;

        internal bool HasConflicts
        {
            get { return conflictAssetCount > 0; }
        }
    }

    internal static class TextureCategoryResolver
    {
        internal static TextureCategoryResolutionSet Resolve(TextureCategoryProjectSettings settings)
        {
            TextureCategoryResolutionSet result = new TextureCategoryResolutionSet();
            Dictionary<string, List<TextureCategoryResolvedRecord>> owners =
                new Dictionary<string, List<TextureCategoryResolvedRecord>>(StringComparer.OrdinalIgnoreCase);

            if (settings == null)
                return result;

            IList<TextureCategoryRecord> categories = settings.categories;

            foreach (TextureCategoryRecord category in categories)
            {
                if (category == null)
                    continue;

                category.EnsureObjects();
                TextureCategoryResolvedRecord resolved = new TextureCategoryResolvedRecord { category = category };
                result.categories.Add(resolved);
                result.byCategoryId[category.id] = resolved;

                HashSet<string> uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (TextureCategorySourceRecord source in category.sources)
                    ResolveSource(source, uniquePaths, resolved.missingSources);

                if (category.excludedAssetGuids.Count > 0)
                {
                    uniquePaths.RemoveWhere(path =>
                        category.excludedAssetGuids.Contains(AssetDatabase.AssetPathToGUID(path)));
                }

                resolved.assetPaths.AddRange(uniquePaths);
                resolved.assetPaths.Sort(StringComparer.OrdinalIgnoreCase);

                foreach (string path in resolved.assetPaths)
                {
                    if (TryClassifySupportedAsset(path, out bool isTexture, out bool isAtlas))
                    {
                        if (isTexture)
                            resolved.textureCount++;
                        else if (isAtlas)
                            resolved.atlasCount++;
                    }

                    if (!owners.TryGetValue(path, out List<TextureCategoryResolvedRecord> pathOwners))
                    {
                        pathOwners = new List<TextureCategoryResolvedRecord>();
                        owners.Add(path, pathOwners);
                    }

                    pathOwners.Add(resolved);
                }
            }

            foreach (KeyValuePair<string, List<TextureCategoryResolvedRecord>> pair in owners)
            {
                if (pair.Value.Count < 2)
                    continue;

                result.conflictAssetCount++;
                foreach (TextureCategoryResolvedRecord owner in pair.Value)
                    owner.conflictPaths.Add(pair.Key);
            }

            foreach (TextureCategoryResolvedRecord resolved in result.categories)
                resolved.conflictPaths.Sort(StringComparer.OrdinalIgnoreCase);

            HashSet<string> ignoredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (settings.ignoredSources != null)
            {
                foreach (TextureCategorySourceRecord source in settings.ignoredSources)
                    ResolveSource(source, ignoredPaths, result.missingIgnoredSources);
            }

            result.ignoredAssetPaths.AddRange(ignoredPaths);
            result.ignoredAssetPaths.Sort(StringComparer.OrdinalIgnoreCase);

            HashSet<string> classificationPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (settings.classificationSources != null)
            {
                foreach (TextureCategorySourceRecord source in settings.classificationSources)
                    ResolveSource(source, classificationPaths, result.missingClassificationSources);
            }

            result.classificationAssetPaths.AddRange(classificationPaths);
            result.classificationAssetPaths.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string path in result.classificationAssetPaths)
            {
                if (!TryClassifySupportedAsset(path, out bool isTexture, out bool isAtlas))
                    continue;

                if (isTexture)
                    result.classificationTextureCount++;
                else if (isAtlas)
                    result.classificationAtlasCount++;

                if (owners.ContainsKey(path) || ignoredPaths.Contains(path))
                    continue;

                result.ungroupedAssetPaths.Add(path);
                if (isTexture)
                    result.ungroupedTextureCount++;
                else if (isAtlas)
                    result.ungroupedAtlasCount++;

                if (isTexture && TextureCategoryAlphaAnalyzer.HasInformativeAlpha(path))
                    result.ungroupedInformativeAlphaAssetPaths.Add(path);
                else
                    result.ungroupedNonInformativeAlphaAssetPaths.Add(path);
            }

            result.ungroupedAssetPaths.Sort(StringComparer.OrdinalIgnoreCase);
            result.ungroupedInformativeAlphaAssetPaths.Sort(StringComparer.OrdinalIgnoreCase);
            result.ungroupedNonInformativeAlphaAssetPaths.Sort(StringComparer.OrdinalIgnoreCase);

            return result;
        }

        internal static bool TryClassifySupportedAsset(string path, out bool isTexture, out bool isAtlas)
        {
            isTexture = false;
            isAtlas = false;
            if (string.IsNullOrEmpty(path))
                return false;

            AssetImporter importer = AssetImporter.GetAtPath(path);
            if (importer is TextureImporter)
            {
                isTexture = true;
                return true;
            }

            if (IsSpriteAtlasImporter(importer))
            {
                isAtlas = true;
                return true;
            }

            return false;
        }

        internal static bool IsAssetsPath(string path)
        {
            return string.Equals(path, "Assets", StringComparison.OrdinalIgnoreCase) ||
                   (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase));
        }

        private static void ResolveSource(
            TextureCategorySourceRecord source,
            HashSet<string> output,
            List<TextureCategorySourceRecord> missingSources)
        {
            if (source == null)
                return;

            string path = NormalizePath(AssetDatabase.GUIDToAssetPath(source.guid));
            if (string.IsNullOrEmpty(path) || !IsAssetsPath(path))
            {
                missingSources.Add(source);
                return;
            }

            if (source.kind == TextureCategorySourceKind.Folder)
            {
                if (!AssetDatabase.IsValidFolder(path))
                {
                    missingSources.Add(source);
                    return;
                }

                foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { path }))
                {
                    string childPath = NormalizePath(AssetDatabase.GUIDToAssetPath(guid));
                    if (TryClassifySupportedAsset(childPath, out _, out _))
                        output.Add(childPath);
                }

                return;
            }

            if (AssetDatabase.IsValidFolder(path) || !TryClassifySupportedAsset(path, out _, out _))
            {
                missingSources.Add(source);
                return;
            }

            output.Add(path);
        }

        private static bool IsSpriteAtlasImporter(AssetImporter importer)
        {
            if (importer == null)
                return false;

            string assetPath = NormalizePath(importer.assetPath);
            return assetPath.EndsWith(".spriteatlas", StringComparison.OrdinalIgnoreCase) ||
                   assetPath.EndsWith(".spriteatlasv2", StringComparison.OrdinalIgnoreCase) ||
                   importer.GetType().Name.IndexOf("SpriteAtlas", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }
    }

    internal static class TextureCategoryAlphaAnalyzer
    {
        private sealed class CacheEntry
        {
            internal Hash128 dependencyHash;
            internal bool hasInformativeAlpha;
        }

        private static readonly Dictionary<string, CacheEntry> cache =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        internal static bool HasInformativeAlpha(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return false;

            try
            {
                if (!importer.DoesSourceTextureHaveAlpha())
                    return false;
            }
            catch (Exception)
            {
                return false;
            }

            Hash128 dependencyHash;
            try
            {
                dependencyHash = AssetDatabase.GetAssetDependencyHash(path);
            }
            catch (Exception)
            {
                dependencyHash = new Hash128();
            }
            if (cache.TryGetValue(path, out CacheEntry cached) && cached.dependencyHash == dependencyHash)
                return cached.hasInformativeAlpha;

            bool hasInformativeAlpha = AnalyzeTexture(path);
            cache[path] = new CacheEntry
            {
                dependencyHash = dependencyHash,
                hasInformativeAlpha = hasInformativeAlpha
            };
            return hasInformativeAlpha;
        }

        private static bool AnalyzeTexture(string path)
        {
            if (TryAnalyzeSourceFile(path, out bool sourceResult))
                return sourceResult;

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
                return false;

            try
            {
                if (texture.isReadable)
                    return HasNonOpaqueAlpha(texture.GetPixels32());
            }
            catch (Exception exception)
            {
                Debug.LogWarning("读取贴图 Alpha 通道失败：" + path + "\n" + exception.Message);
            }

            try
            {
                return AnalyzeViaRenderTexture(texture);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("读取贴图 Alpha 通道失败：" + path + "\n" + exception.Message);
                return false;
            }
        }

        private static bool TryAnalyzeSourceFile(string path, out bool result)
        {
            result = false;
            string extension = Path.GetExtension(path);
            if (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
                return false;

            Texture2D sourceTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                byte[] bytes = File.ReadAllBytes(fullPath);
                if (!ImageConversion.LoadImage(sourceTexture, bytes, false))
                    return false;

                result = HasNonOpaqueAlpha(sourceTexture.GetPixels32());
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("读取贴图源文件 Alpha 通道失败：" + path + "\n" + exception.Message);
                return false;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceTexture);
            }
        }

        private static bool AnalyzeViaRenderTexture(Texture2D texture)
        {
            const int maxDimension = 256;
            int width = Mathf.Max(1, Mathf.Min(maxDimension, texture.width));
            int height = Mathf.Max(1, Mathf.Min(maxDimension, texture.height));
            RenderTexture temporary = RenderTexture.GetTemporary(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            Texture2D readable = null;
            try
            {
                Graphics.Blit(texture, temporary);
                RenderTexture.active = temporary;
                readable = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                readable.Apply(false, false);
                return HasNonOpaqueAlpha(readable.GetPixels32());
            }
            finally
            {
                RenderTexture.active = previous;
                if (readable != null)
                    UnityEngine.Object.DestroyImmediate(readable);
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private static bool HasNonOpaqueAlpha(Color32[] pixels)
        {
            if (pixels == null)
                return false;

            for (int index = 0; index < pixels.Length; index++)
            {
                if (pixels[index].a < byte.MaxValue)
                    return true;
            }

            return false;
        }
    }
}
#endif
