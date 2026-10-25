#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ithappy
{
    [CustomEditor(typeof(EntryPoint))]
    public class EntryPointEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            EntryPoint entryPoint = (EntryPoint)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Character Finder", EditorStyles.boldLabel);
            
            entryPoint.TargetObject = (GameObject)EditorGUILayout.ObjectField("Target Object", entryPoint.TargetObject, typeof(GameObject), true);

            EditorGUILayout.Space();
            
            if (GUILayout.Button("Find Characters in Children"))
            {
                if (entryPoint.TargetObject != null)
                {
                    entryPoint.FindCharactersInChildren();
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", "Please assign a Target Object first!", "OK");
                }
            }
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Current Characters Count: {entryPoint.Characters?.Length ?? 0}");
            
            if (entryPoint.Characters != null && entryPoint.Characters.Length > 0)
            {
                if (GUILayout.Button("Clear Characters Array"))
                {
                    entryPoint.Characters = new CharacterBase[0];
                    EditorUtility.SetDirty(entryPoint);
                }
            }
            
            EditorGUILayout.HelpBox(
                "Assign a GameObject and click the button to find all CharacterBase components in its children.",
                MessageType.Info);
        }
    }
}
#endif