using UnityEditor;
using UnityEngine;

#pragma warning disable 0618 // Edits the obsolete per-object position senders, to replace them

namespace AsioAudioUnity
{
    /// <summary>
    /// Inspector of the obsolete Source / Room Position OSC components: says they are replaced by the Spat Controller,
    /// and moves their role to the scene's Spat Controller (created if needed).
    /// </summary>
    [CustomEditor(typeof(ObjectPositionOsc), true)]
    [CanEditMultipleObjects]
    internal class LegacyPositionOscEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("This component is obsolete: positions are sent to Spat Revolution by a single Spat Controller, which tracks every ASIO Audio Source "
                + "automatically (without an OSC Manager). It still works in existing scenes.", MessageType.Warning);
            if (GUILayout.Button("Replace with Spat Controller"))
            {
                Replace(targets);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.Space(4f);
            DrawDefaultInspector();
        }

        private static void Replace(Object[] components)
        {
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Replace Position OSC with Spat Controller");

            SpatController controller = Object.FindFirstObjectByType<SpatController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                GameObject go = new GameObject("Spat Controller");
                Undo.RegisterCreatedObjectUndo(go, "Create Spat Controller");
                controller = Undo.AddComponent<SpatController>(go);
            }
            Undo.RecordObject(controller, "Replace Position OSC with Spat Controller");

            foreach (Object target in components)
            {
                if (!(target is ObjectPositionOsc component)) continue;
                AsioAudioSource source = component.GetComponent<AsioAudioSource>();

                if (component is SourcePositionOsc && source != null)
                {
                    // Tracked automatically: only keep its Spat number if it isn't the output channel
                    if (component.Index != source.TargetOutputChannel && component.Index > 0) controller.SetSpatIndex(source, component.Index);
                }
                else
                {
                    controller.TrackedObjects.Add(new SpatTrackedObject
                    {
                        Target = component.transform,
                        Type = component is RoomPositionOsc ? SpatObjectType.Room : SpatObjectType.Source,
                        Index = Mathf.Max(1, component.Index),
                    });
                }
                Undo.DestroyObjectImmediate(component);
            }

            EditorUtility.SetDirty(controller);
            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeObject = controller.gameObject;
        }
    }
}
