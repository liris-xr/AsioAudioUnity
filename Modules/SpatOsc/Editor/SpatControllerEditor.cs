using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

#pragma warning disable 0618 // Detects the obsolete per-object position senders, to remove them

namespace AsioAudioUnity
{
    [CustomEditor(typeof(SpatController))]
    public class SpatControllerEditor : Editor
    {
        private static bool _showAddresses;
        private GUIStyle _cardStyle, _titleStyle;

        private SpatController Controller => (SpatController)target;

        public override bool RequiresConstantRepaint() => EditorApplication.isPlaying; // live positions

        public override void OnInspectorGUI()
        {
            EnsureStyles();
            serializedObject.Update();

            // Spat Revolution
            EditorGUILayout.BeginVertical(_cardStyle);
            GUILayout.Label("Spat Revolution", _titleStyle);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_spatAddress"), new GUIContent("Address", "IP address of the computer running Spat Revolution (127.0.0.1: this computer)."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_spatPort"), new GUIContent("Port", "The port of Spat Revolution's OSC input."));
            EditorGUILayout.HelpBox("In Spat Revolution: Preferences > OSC Main: enable OSC, and add an OSC connection \"input | Spat Revolution - Plugins\" on this port.", MessageType.None);
            EditorGUILayout.EndVertical();

            // Tracking
            EditorGUILayout.BeginVertical(_cardStyle);
            GUILayout.Label("Tracking", _titleStyle);
            SerializedProperty trackAllSources = serializedObject.FindProperty("_trackAllSources");
            EditorGUILayout.PropertyField(trackAllSources, new GUIContent("Track All ASIO Sources", "Send the position of every ASIO Audio Source of the loaded scenes, as the Spat source numbered like its output channel."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_trackedObjects"), new GUIContent("Other Objects", "Rooms, or sources that aren't ASIO Audio Sources. An entry with the same type and number as an ASIO source replaces it."), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_origin"), new GUIContent("Origin", "Positions are sent relative to this object (e.g. the listener or the room's center). Empty: the world origin."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_positionScale"), new GUIContent("Position Scale", "Multiplies the positions sent (e.g. to convert units)."));
            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();

            DrawTrackedTable(trackAllSources.boolValue);

            serializedObject.Update();

            // Sending
            EditorGUILayout.BeginVertical(_cardStyle);
            GUILayout.Label("Sending", _titleStyle);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_maxSendRate"), new GUIContent("Max Send Rate", "Maximum number of messages per second, per object (0: no limit)."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_keepAliveInterval"), new GUIContent("Keep Alive Interval", "Positions are re-sent every this many seconds even if nothing moves (0: only on movement)."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_movementThreshold"), new GUIContent("Movement Threshold", "Smallest movement (in Unity units) that sends a new position."));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_resetPositionsOnQuit"), new GUIContent("Reset Positions On Quit", "Send (0, 0, 0) for every tracked object when the application quits or Play mode ends."));
            if (GUILayout.Button(new GUIContent("Send Positions Now", "Send the position of everything tracked to Spat Revolution now (also works in Edit mode)."), GUILayout.Height(24f)))
            {
                serializedObject.ApplyModifiedProperties();
                Controller.SendAllPositions();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical(_cardStyle);
            _showAddresses = EditorGUILayout.Foldout(_showAddresses, new GUIContent("OSC Addresses", "Spat Revolution's addresses: {0} is the source or room number."), true);
            if (_showAddresses)
            {
                SerializedProperty addresses = serializedObject.FindProperty("_addresses");
                EditorGUILayout.PropertyField(addresses.FindPropertyRelative("SourcePosition"));
                EditorGUILayout.PropertyField(addresses.FindPropertyRelative("RoomPosition"));
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_logMessages"));
            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }

        // What is tracked, with the position sent to Spat Revolution.
        private void DrawTrackedTable(bool trackAllSources)
        {
            Controller.RefreshSources();
            var tracked = Controller.GetTrackedPositions().OrderBy(item => item.Type).ThenBy(item => item.Index).ToList();

            EditorGUILayout.BeginVertical(_cardStyle);
            GUILayout.Label(new GUIContent("Sent to Spat (" + tracked.Count + ")", "Spat number, object, and position sent (Spat's axes: x, y, z with Z up)."), _titleStyle);

            // ASIO Audio Sources: send or not, and Spat number (output channel by default, or any other number)
            if (trackAllSources)
            {
                AsioAudioSource[] sources = Controller.Sources.Where(source => source != null)
                    .OrderBy(source => source.TargetOutputChannel <= 0 ? int.MaxValue : source.TargetOutputChannel).ThenBy(source => source.name).ToArray();

                if (sources.Length > 0)
                {
                    Rect header = GUILayoutUtility.GetRect(0f, 16f, GUILayout.ExpandWidth(true));
                    GUI.Label(new Rect(header.x, header.y, 34f, header.height), new GUIContent("Send", "Send this source's position"), EditorStyles.miniBoldLabel);
                    GUI.Label(new Rect(header.x + 36f, header.y, 60f, header.height), new GUIContent("Spat #", "Its source number in Spat Revolution: its output channel by default, or any other number."), EditorStyles.miniBoldLabel);
                    GUI.Label(new Rect(header.x + 122f, header.y, header.width - 122f, header.height), "ASIO Audio Source  ·  position sent", EditorStyles.miniBoldLabel);
                }

                foreach (AsioAudioSource source in sources)
                {
                    SpatSourceMapping mapping = Controller.GetMapping(source);
                    bool send = mapping == null || mapping.Send;
                    int spatIndex = Controller.GetSpatIndex(source);
                    bool custom = mapping != null && (mapping.SpatIndex != source.TargetOutputChannel || !mapping.Send);

                    Rect row = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
                    EditorGUI.BeginChangeCheck();
                    bool newSend = EditorGUI.Toggle(new Rect(row.x + 8f, row.y + 1f, 18f, row.height), send);
                    int newIndex;
                    using (new EditorGUI.DisabledScope(!send))
                    {
                        newIndex = EditorGUI.IntField(new Rect(row.x + 36f, row.y + 1f, 52f, row.height - 2f), spatIndex);
                    }
                    if (EditorGUI.EndChangeCheck()) SetMapping(source, newSend, Mathf.Max(1, newIndex));

                    // Back to the output channel
                    using (new EditorGUI.DisabledScope(!custom))
                    {
                        if (GUI.Button(new Rect(row.x + 92f, row.y + 1f, 24f, row.height - 2f), new GUIContent(EditorGUIUtility.IconContent("Refresh").image, "Use the output channel (" + source.TargetOutputChannel + ") as the Spat number, and send it"), EditorStyles.miniButton))
                        {
                            SetMapping(source, true, source.TargetOutputChannel);
                        }
                    }

                    string channelText = source.TargetOutputChannel > 0 ? "  (channel " + source.TargetOutputChannel + ")" : "  (no channel)";
                    float nameWidth = (row.width - 122f) * 0.55f;
                    if (GUI.Button(new Rect(row.x + 122f, row.y, nameWidth, row.height), new GUIContent(source.name + channelText, "Select this source"), custom ? EditorStyles.boldLabel : EditorStyles.label))
                    {
                        Selection.activeObject = source;
                        EditorGUIUtility.PingObject(source);
                    }
                    string positionText = !send ? "not sent" : spatIndex <= 0 ? "no Spat number" : FormatPosition(Controller.ToSpatPosition(source.transform.position));
                    GUI.Label(new Rect(row.x + 124f + nameWidth, row.y, row.width - 126f - nameWidth, row.height), positionText, EditorStyles.miniLabel);
                }

                // Two sources on the same Spat number: Spat receives both positions alternately.
                var conflicts = tracked.Where(item => item.Source != null).GroupBy(item => item.Index).Where(group => group.Count() > 1).ToList();
                foreach (var conflict in conflicts)
                {
                    EditorGUILayout.HelpBox("Spat source " + conflict.Key + " is used by " + string.Join(", ", conflict.Select(item => "\"" + item.Name + "\"")) + ": give each source its own number.", MessageType.Warning);
                }
                EditorGUILayout.Space(4f);
            }

            var otherObjects = tracked.Where(item => item.Source == null).ToList();
            if (tracked.Count == 0 && !trackAllSources)
            {
                EditorGUILayout.LabelField("Nothing is tracked: tick Track All ASIO Sources, or add objects.", EditorStyles.wordWrappedMiniLabel);
            }
            foreach (SpatController.TrackedPosition item in otherObjects)
            {
                Vector3 position = Controller.ToSpatPosition(item.Target.position);
                Rect row = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
                GUI.Label(new Rect(row.x, row.y, 80f, row.height), (item.Type == SpatObjectType.Source ? "Source " : "Room ") + item.Index, EditorStyles.boldLabel);
                if (GUI.Button(new Rect(row.x + 82f, row.y, row.width * 0.4f, row.height), new GUIContent(item.Name, "Select this object"), EditorStyles.label))
                {
                    Selection.activeTransform = item.Target;
                    EditorGUIUtility.PingObject(item.Target);
                }
                string text = position.x.ToString("0.00", CultureInfo.InvariantCulture) + ",  " + position.y.ToString("0.00", CultureInfo.InvariantCulture) + ",  " + position.z.ToString("0.00", CultureInfo.InvariantCulture);
                GUI.Label(new Rect(row.x + 86f + row.width * 0.4f, row.y, row.width * 0.6f - 90f, row.height), text, EditorStyles.miniLabel);
            }

            // Sources that also send their position with a Source Position OSC component (through the OSC Manager): sent twice.
            if (trackAllSources)
            {
                ObjectPositionOsc[] duplicates = FindObjectsByType<ObjectPositionOsc>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(component => component is SourcePositionOsc && component.GetComponent<AsioAudioSource>() != null).ToArray();
                if (duplicates.Length > 0)
                {
                    EditorGUILayout.HelpBox(duplicates.Length + " ASIO Audio Source(s) also have a Source Position OSC component: their positions are sent twice (and to the OSC Manager's destination too).", MessageType.Warning);
                    if (GUILayout.Button("Remove the Source Position OSC Components"))
                    {
                        int undoGroup = Undo.GetCurrentGroup();
                        foreach (ObjectPositionOsc component in duplicates) Undo.DestroyObjectImmediate(component);
                        Undo.CollapseUndoOperations(undoGroup);
                        GUIUtility.ExitGUI();
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }

        // Records the Spat number / send state of a source. Sources sent with their output channel need no entry: it is removed.
        private void SetMapping(AsioAudioSource source, bool send, int spatIndex)
        {
            Undo.RecordObject(Controller, "Change Spat Source Number");
            SpatSourceMapping mapping = Controller.GetMapping(source);
            if (send && spatIndex == source.TargetOutputChannel)
            {
                if (mapping != null) Controller.SourceMappings.Remove(mapping);
            }
            else
            {
                Controller.SetSpatIndex(source, spatIndex);
                Controller.GetMapping(source).Send = send;
            }
            Controller.SourceMappings.RemoveAll(other => other == null || other.Source == null); // deleted sources
            PrefabUtility.RecordPrefabInstancePropertyModifications(Controller);
            EditorUtility.SetDirty(Controller);
        }

        private static string FormatPosition(Vector3 position)
        {
            return position.x.ToString("0.00", CultureInfo.InvariantCulture) + ",  " + position.y.ToString("0.00", CultureInfo.InvariantCulture) + ",  " + position.z.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private void EnsureStyles()
        {
            if (_cardStyle != null) return;
            _cardStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(10, 10, 8, 10), margin = new RectOffset(0, 0, 6, 0) };
            _titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, margin = new RectOffset(0, 0, 0, 4) };
        }
    }
}
