using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RagdollFootball.Field.EditorTools
{
    [CustomEditor(typeof(SoccerField))]
    [CanEditMultipleObjects]
    public class SoccerFieldEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();

            SoccerField[] fields = targets.Cast<SoccerField>().ToArray();
            if (fields.Any(f => !f.MeetsLaw1Dimensions))
            {
                EditorGUILayout.HelpBox(
                    "Outside Law 1: the touchline must be 90-120 m, the goal line 45-90 m, " +
                    "and the touchline longer than the goal line.", MessageType.Warning);
            }

            EditorGUILayout.HelpBox(
                "Rebuild after changing the settings. Rebuilding replaces the \"" + SoccerFieldBuilder.GeneratedChildName +
                "\" child (one undo step), so keep your own objects outside it. Its meshes are stored in this scene.",
                MessageType.Info);

            bool inPrefab = fields.Any(f => EditorUtility.IsPersistent(f) || PrefabStageUtility.GetPrefabStage(f.gameObject) != null);
            using (new EditorGUI.DisabledScope(Application.isPlaying || inPrefab))
            {
                if (GUILayout.Button("Rebuild Field", GUILayout.Height(28f)))
                {
                    foreach (SoccerField field in fields) SoccerFieldBuilder.Build(field);
                    GUIUtility.ExitGUI(); // the generated hierarchy changed under the inspector
                }
            }
            if (inPrefab)
                EditorGUILayout.HelpBox("Fields cannot be built inside prefabs, because the generated meshes live in the scene. " +
                                        "Rebuild the field in a scene.", MessageType.None);
        }
    }
}
