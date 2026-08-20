#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Presets;
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
        public Preset textureImporterPreset;
        public List<TextureCategorySourceRecord> sources = new List<TextureCategorySourceRecord>();
        public List<string> excludedAssetGuids = new List<string>();

        internal void EnsureObjects()
        {
            if (sources == null)
                sources = new List<TextureCategorySourceRecord>();
            if (excludedAssetGuids == null)
                excludedAssetGuids = new List<string>();
        }
    }

    [FilePath("ProjectSettings/CobaltTextureCategories.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class TextureCategoryProjectSettings : ScriptableSingleton<TextureCategoryProjectSettings>
    {
        private const string BackupFileExtension = ".json";

        [SerializeField] public int version = 1;
        [SerializeField] public List<TextureCategoryRecord> categories = new List<TextureCategoryRecord>();
        [SerializeField] public List<TextureCategorySourceRecord> classificationSources = new List<TextureCategorySourceRecord>();
        [SerializeField] public List<TextureCategorySourceRecord> ignoredSources = new List<TextureCategorySourceRecord>();

        [NonSerialized] private bool hasLoadedBackup;

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
            string filePath = GetFilePath();
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
            Save(true);
            SaveBackup(filePath);
        }

        internal void LoadBackupIfNecessary()
        {
            if (hasLoadedBackup)
                return;

            hasLoadedBackup = true;
            if ((categories != null && categories.Count > 0) ||
                (classificationSources != null && classificationSources.Count > 0) ||
                (ignoredSources != null && ignoredSources.Count > 0))
                return;

            string backupPath = GetBackupPath(GetFilePath());
            if (!File.Exists(backupPath))
                return;

            try
            {
                TextureCategoryBackupData backup = JsonUtility.FromJson<TextureCategoryBackupData>(File.ReadAllText(backupPath));
                if (backup == null)
                    return;

                version = backup.version;
                categories = backup.categories == null
                    ? new List<TextureCategoryRecord>()
                    : backup.categories.ConvertAll(CreateCategoryFromBackup);
                classificationSources = CloneSources(backup.classificationSources);
                ignoredSources = CloneSources(backup.ignoredSources);
                EnsureIntegrity();
                Save(true);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("贴图分类备份读取失败：" + exception.Message);
            }
        }

        private void SaveBackup(string settingsFilePath)
        {
            try
            {
                TextureCategoryBackupData backup = new TextureCategoryBackupData
                {
                    version = version,
                    categories = categories == null
                        ? new List<TextureCategoryBackupCategory>()
                        : categories.ConvertAll(CreateCategoryBackup),
                    classificationSources = CloneSources(classificationSources),
                    ignoredSources = CloneSources(ignoredSources)
                };
                File.WriteAllText(GetBackupPath(settingsFilePath), JsonUtility.ToJson(backup, true));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("贴图分类备份保存失败：" + exception.Message);
            }
        }

        private static string GetBackupPath(string settingsFilePath)
        {
            return Path.ChangeExtension(settingsFilePath, BackupFileExtension);
        }

        private static TextureCategoryBackupCategory CreateCategoryBackup(TextureCategoryRecord category)
        {
            category = category ?? new TextureCategoryRecord();
            return new TextureCategoryBackupCategory
            {
                id = category.id,
                name = category.name,
                expanded = category.expanded,
                textureImporterPresetGuid = category.textureImporterPreset == null
                    ? string.Empty
                    : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(category.textureImporterPreset)),
                sources = CloneSources(category.sources),
                excludedAssetGuids = category.excludedAssetGuids == null
                    ? new List<string>()
                    : new List<string>(category.excludedAssetGuids)
            };
        }

        private static TextureCategoryRecord CreateCategoryFromBackup(TextureCategoryBackupCategory backup)
        {
            backup = backup ?? new TextureCategoryBackupCategory();
            return new TextureCategoryRecord
            {
                id = backup.id,
                name = backup.name,
                expanded = backup.expanded,
                textureImporterPreset = string.IsNullOrEmpty(backup.textureImporterPresetGuid)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<Preset>(
                        AssetDatabase.GUIDToAssetPath(backup.textureImporterPresetGuid)),
                sources = CloneSources(backup.sources),
                excludedAssetGuids = backup.excludedAssetGuids == null
                    ? new List<string>()
                    : new List<string>(backup.excludedAssetGuids)
            };
        }

        private static List<TextureCategorySourceRecord> CloneSources(List<TextureCategorySourceRecord> sources)
        {
            List<TextureCategorySourceRecord> result = new List<TextureCategorySourceRecord>();
            if (sources == null)
                return result;

            foreach (TextureCategorySourceRecord source in sources)
            {
                if (source == null)
                    continue;

                result.Add(new TextureCategorySourceRecord
                {
                    kind = source.kind,
                    guid = source.guid,
                    lastKnownPath = source.lastKnownPath
                });
            }

            return result;
        }
    }

    [Serializable]
    internal sealed class TextureCategoryBackupData
    {
        public int version;
        public List<TextureCategoryBackupCategory> categories = new List<TextureCategoryBackupCategory>();
        public List<TextureCategorySourceRecord> classificationSources = new List<TextureCategorySourceRecord>();
        public List<TextureCategorySourceRecord> ignoredSources = new List<TextureCategorySourceRecord>();
    }

    [Serializable]
    internal sealed class TextureCategoryBackupCategory
    {
        public string id;
        public string name;
        public bool expanded = true;
        public string textureImporterPresetGuid;
        public List<TextureCategorySourceRecord> sources = new List<TextureCategorySourceRecord>();
        public List<string> excludedAssetGuids = new List<string>();
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
            }

            result.ungroupedAssetPaths.Sort(StringComparer.OrdinalIgnoreCase);

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

        private static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }
    }

}
#endif
