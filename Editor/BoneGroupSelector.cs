using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

namespace SleepyCobalt.Tools
{
[InitializeOnLoad]
public static class BoneGroupSelector
{
    static BoneGroupSelector()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    static void OnSceneGUI(SceneView sceneView)
    {
        if (Selection.activeTransform == null)
            return;

        Transform selected = Selection.activeTransform;

        // 👉 不在名含 "CatSkin" 的物体上生效
        if (selected.name.Contains("CatSkin"))
            return;

        Transform root = FindDirectChildNamed(selected, "Root");
        if (root == null)
            return;

        Vector3 center = GetChildrenCenter(root);
        float offset = 1.5f;
        float size = HandleUtility.GetHandleSize(center) * 0.3f;

        DrawHandle(root, center, Vector3.left, "Left", Color.cyan, offset, size, Vector3.right);
        DrawHandle(root, center, Vector3.right, "Right", Color.red, offset, size, Vector3.right);
        DrawHandle(root, center, Vector3.forward, "Forward", Color.green, offset, size, Vector3.forward);
        DrawHandle(root, center, Vector3.back, "Back", Color.magenta, offset, size, Vector3.forward);
        DrawHandle(root, center, Vector3.up, "Up", Color.yellow, offset, size, Vector3.up);
        DrawHandle(root, center, Vector3.down, "Down", Color.gray, offset, size, Vector3.up);
    }

    static void DrawHandle(
        Transform root,
        Vector3 center,
        Vector3 dir,
        string keyword,
        Color color,
        float offset,
        float size,
        Vector3 axis)
    {
        Vector3 pos = center + root.TransformDirection(dir) * offset;
        Vector3 handleDir = root.TransformDirection(axis.normalized);
        Handles.color = color;

        if (Handles.Button(pos, Quaternion.identity, size * 0.8f, size * 0.8f, Handles.SphereHandleCap))
        {
            SelectBoneGroup(root, keyword);
        }

        EditorGUI.BeginChangeCheck();

        bool precise = (Event.current.modifiers & EventModifiers.Shift) != 0
                       || EditorSnapSettings.move.x > 0f;

        float snap = precise ? EditorSnapSettings.move.x : 0f;

        Vector3 newPos = Handles.Slider(pos, handleDir, size, Handles.SphereHandleCap, snap);

        if (EditorGUI.EndChangeCheck())
        {
            Vector3 delta = Vector3.Project(newPos - pos, handleDir);
            var targets = GetGroupTransforms(root, keyword);

            Undo.RecordObjects(targets, "Move Bone Group");

            foreach (var t in targets)
                t.position += delta;
        }

        GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = new GUIStyleState
            {
                textColor = Color.black
            }
        };

        Handles.Label(
            newPos + Vector3.up * size * 1.2f,
            keyword,
            labelStyle
        );
    }

    static Transform[] GetGroupTransforms(Transform root, string keyword)
    {
        List<Transform> results = new List<Transform>();
        foreach (Transform child in root)
        {
            if (child.name.Contains(keyword))
                results.Add(child);
        }
        return results.ToArray();
    }

    static void SelectBoneGroup(Transform root, string keyword)
    {
        List<GameObject> selection = new List<GameObject>();
        foreach (Transform child in root)
        {
            if (child.name.Contains(keyword))
                selection.Add(child.gameObject);
        }
        Selection.objects = selection.ToArray();
    }

    static Transform FindDirectChildNamed(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name)
                return child;
        }
        return null;
    }

    static Vector3 GetChildrenCenter(Transform root)
    {
        if (root.childCount == 0)
            return root.position;

        Vector3 sum = Vector3.zero;
        foreach (Transform child in root)
            sum += child.position;

        return sum / root.childCount;
    }
}
}
