using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.IO;

namespace SleepyCobalt.Tools
{
public class NormalTextureProcessor
{
    [MenuItem("CobaltTools/一键设置法线贴图",false,30)]
    private static void BatchSetNormalMapsExact()
    {
        // 查找所有贴图
        string[] guids = AssetDatabase.FindAssets("t:Texture2D");

        if (guids.Length == 0)
        {
            Debug.Log("没有找到任何贴图");
            return;
        }

        // 收集需要修改的贴图
        List<string> textureNames = new List<string>();
        List<string> texturePaths = new List<string>();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = Path.GetFileNameWithoutExtension(path);

            // 检查是否以_Normal结尾
            if (fileName.EndsWith("_Normal"))
            {
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

                if (importer != null && importer.textureType != TextureImporterType.NormalMap)
                {
                    textureNames.Add(fileName);
                    texturePaths.Add(path);
                }
            }
        }

        if (textureNames.Count == 0)
        {
            Debug.Log("没有找到需要修改的法线贴图");
            return;
        }

        // 构建确认消息
        StringBuilder message = new StringBuilder();
        message.AppendLine($"将修改 {textureNames.Count} 个法线贴图的Texture Type为Normal map：");

        // 最多显示15个贴图名称
        int displayCount = Mathf.Min(textureNames.Count, 15);
        for (int i = 0; i < displayCount; i++)
        {
            message.AppendLine($"? {textureNames[i]}");
        }

        if (textureNames.Count > displayCount)
        {
            message.AppendLine($"...以及另外 {textureNames.Count - displayCount} 个贴图");
        }

        message.AppendLine("\n确定要继续吗？");

        // 自定义确认窗口
        if (EditorUtility.DisplayDialog("批量设置法线贴图（精确匹配）",
            message.ToString(),
            "确认修改", "取消"))
        {
            int processedCount = 0;

            foreach (string path in texturePaths)
            {
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.NormalMap;
                    importer.textureShape = TextureImporterShape.Texture2D;

                    // 确保启用mipmaps（法线贴图常用设置）
                    importer.mipmapEnabled = true;
                    importer.streamingMipmaps = true;

                    // 保存设置
                    EditorUtility.SetDirty(importer);
                    AssetDatabase.ImportAsset(path);
                    processedCount++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"操作完成: 成功处理 {processedCount} 个法线贴图");
        }
        else
        {
            Debug.Log("用户取消了操作");
        }
    }
}
}
