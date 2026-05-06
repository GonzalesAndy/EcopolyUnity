using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Ecopoly.Core;

namespace Ecopoly.Editor
{
    [CustomEditor(typeof(TileDebugVisualizer))]
    public class TileDebugVisualizerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(8);

            var visualizer = (TileDebugVisualizer)target;

            // ── Permanent bake (removes the component) ────────────────────────
            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);
            if (GUILayout.Button("Bake Permanent & Remove Component", GUILayout.Height(40)))
            {
                if (EditorUtility.DisplayDialog(
                    "Bake Permanent",
                    "This will bake all tile visuals with real instanced materials and then " +
                    "REMOVE the TileDebugVisualizer component.\n\nSave the scene afterwards to persist the result. Continue?",
                    "Bake & Remove", "Cancel"))
                {
                    Scene scene = visualizer.gameObject.scene;

                    // Capture the hierarchy root before the component destroys itself.
                    GameObject go = visualizer.gameObject;
                    Undo.RegisterFullObjectHierarchyUndo(go, "Bake Permanent Tile Visuals");

                    // BakePermanentAndRemove calls DestroyImmediate(this) at the end,
                    // so 'visualizer' is invalid after this line.
                    visualizer.BakePermanentAndRemove();

                    EditorSceneManager.MarkSceneDirty(scene);
                }
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            EditorGUILayout.Space(2);

            // ── Normal bake (keeps the component) ────────────────────────────
            if (GUILayout.Button("Bake Tile Visuals", GUILayout.Height(28)))
            {
                Undo.RegisterFullObjectHierarchyUndo(visualizer.gameObject, "Bake Tile Visuals");
                visualizer.Bake();
                EditorSceneManager.MarkSceneDirty(visualizer.gameObject.scene);
            }

            EditorGUILayout.Space(2);

            if (GUILayout.Button("Clear Baked Visuals", GUILayout.Height(24)))
            {
                Undo.RegisterFullObjectHierarchyUndo(visualizer.gameObject, "Clear Baked Visuals");
                visualizer.Clear();
                EditorSceneManager.MarkSceneDirty(visualizer.gameObject.scene);
            }
        }
    }
}
