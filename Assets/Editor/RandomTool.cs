using UnityEngine;
using UnityEditor;

public class DistributeOnX : EditorWindow
{
    float spacing = 5f;
    string axis = "x";

    [MenuItem("Tools/Distribute On X")]
    static void Init()
    {
        GetWindow<DistributeOnX>("Distribute X");
    }

    void OnGUI()
    {
        spacing = EditorGUILayout.FloatField("Spacing", spacing);
        axis = EditorGUILayout.TextField("Axis (x/y/z)", axis);

        if (GUILayout.Button("Apply"))
        {
            var selected = Selection.transforms;

            for (int i = 0; i < selected.Length; i++)
            {
                Vector3 pos = selected[i].position;
                if (axis == "x")
                    pos.x += (i+1) * spacing;
                else if (axis == "y")
                    pos.y += (i+1) * spacing;
                else if (axis == "z")
                    pos.z += (i+1) * spacing;
                selected[i].position = pos;
            }
        }
    }
}

public class AddCubeMeshToSelection : EditorWindow
{
    Vector3 size = Vector3.one;

    [MenuItem("Tools/Add Cube Mesh To Selection")]
    static void Init()
    {
        GetWindow<AddCubeMeshToSelection>("Add Cube Mesh");
    }

    void OnGUI()
    {
        GUILayout.Label("Cube Size", EditorStyles.boldLabel);
        size = EditorGUILayout.Vector3Field("Size", size);

        if (GUILayout.Button("Apply"))
        {
            foreach (var t in Selection.transforms)
            {
                AddCube(t);
            }
        }
    }

    void AddCube(Transform parent)
    {
        // Create cube
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);

        // Rename & parent
        cube.name = "PRF_DebugCube";
        cube.transform.SetParent(parent);
        cube.transform.localPosition = Vector3.zero;
        cube.transform.localRotation = Quaternion.identity;

        // Apply size
        cube.transform.localScale = size;

        // Optional: remove collider if you only want visuals
        DestroyImmediate(cube.GetComponent<BoxCollider>());
    }
}