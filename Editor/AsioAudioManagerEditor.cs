using UnityEditor;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// The ASIO Audio Manager is created automatically, and its settings are in Project Settings > ASIO Audio.
    /// In Play mode, this shows the running manager's status; on a scene manager from before version 3.0, it offers the migration.
    /// </summary>
    [CustomEditor(typeof(AsioAudioManager))]
    public class AsioAudioManagerEditor : Editor
    {
        private AsioAudioManager Manager => (AsioAudioManager)target;

        public override bool RequiresConstantRepaint() => EditorApplication.isPlaying;

        public override void OnInspectorGUI()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Since version 3.0, the ASIO Audio Manager doesn't need to be in the scene: it is created automatically in Play mode, "
                    + "and its settings are in Project Settings > ASIO Audio.", MessageType.Warning);
                if (GUILayout.Button(Manager.HasLegacySceneSettings ? "Move its Settings to Project Settings and Remove it" : "Remove it", GUILayout.Height(26f)))
                {
                    AsioSettingsMigration.Migrate();
                    GUIUtility.ExitGUI();
                }
                if (Manager.HasLegacySceneSettings)
                {
                    EditorGUILayout.LabelField("Driver \"" + Manager.LegacyDriverName + "\", " + Manager.LegacySampleRate + " Hz, " + Manager.LegacySnapshots.Count + " snapshot(s).", EditorStyles.miniLabel);
                }
                return;
            }

            Color statusColor = Manager.IsConnected ? AsioEditorUtility.PlayingColor : Manager.IsUsingUnityAudio ? AsioEditorUtility.PausedColor : AsioEditorUtility.ErrorColor;
            string statusText = Manager.IsConnected
                ? "Connected to \"" + Manager.AsioDriverName + "\"  ·  " + Manager.AsioDriverOutputChannelCount + " outputs  ·  " + Manager.TargetSampleRate + " Hz"
                : Manager.IsUsingUnityAudio ? "No ASIO driver: playing through Unity's audio (all channels mixed)  ·  " + Manager.OutputSampleRate + " Hz"
                : "Not connected: see the Console for details.";

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            Rect rowRect = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
            AsioEditorUtility.DrawDot(new Rect(rowRect.x + 2f, rowRect.y, 12f, rowRect.height), statusColor);
            GUI.Label(new Rect(rowRect.x + 20f, rowRect.y, rowRect.width - 20f, rowRect.height), statusText, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(Manager.AsioAudioSources.Count + " source(s) playing through it.", EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reconnect", EditorStyles.miniButtonLeft)) Manager.Reconnect();
            if (GUILayout.Button("Open Mixer", EditorStyles.miniButtonMid)) AsioAudioMixerWindow.Open();
            if (GUILayout.Button("Settings", EditorStyles.miniButtonRight)) AsioEditorUtility.OpenSettings();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }
    }
}
