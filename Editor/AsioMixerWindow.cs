using System.IO;
using UnityEditor;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// A mixer-style window showing every Custom ASIO Audio Source of the scene as a channel strip:
    /// output channel, level meter, volume, mute / solo, loop and transport controls.
    /// </summary>
    public class AsioMixerWindow : EditorWindow
    {
        private const float StripWidth = 116f;
        private const float StripContentWidth = StripWidth - 10f;
        // Text controls must have a fixed width, otherwise a long name or file name widens the whole strip.
        private static readonly GUILayoutOption ContentWidth = GUILayout.Width(StripContentWidth);
        private const float FaderHeight = 140f;
        private const double PlayModeRepaintInterval = 1.0 / 30.0;

        private Vector2 _scrollPosition;
        private double _lastRepaintTime;

        [MenuItem("AsioAudioUnity/ASIO Mixer", false, 40)]
        [MenuItem("Window/Audio/ASIO Mixer")]
        public static void Open()
        {
            AsioMixerWindow window = GetWindow<AsioMixerWindow>();
            window.titleContent = new GUIContent("ASIO Mixer");
            window.minSize = new Vector2(StripWidth + 20f, FaderHeight + 200f);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.hierarchyChanged += Repaint;
            Undo.undoRedoPerformed += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.hierarchyChanged -= Repaint;
            Undo.undoRedoPerformed -= Repaint;
        }

        private void OnSelectionChange() => Repaint();

        // Keep meters and positions live in Play mode.
        private void OnEditorUpdate()
        {
            if (!EditorApplication.isPlaying) return;
            if (EditorApplication.timeSinceStartup - _lastRepaintTime < PlayModeRepaintInterval) return;
            _lastRepaintTime = EditorApplication.timeSinceStartup;
            Repaint();
        }

        private void OnGUI()
        {
            AsioAudioManager manager = AsioEditorUtility.FindManager();
            CustomAsioAudioSource[] allSources = AsioEditorUtility.FindAllSources();
            int outputChannelCount = AsioEditorUtility.GetKnownOutputChannelCount(manager);

            DrawToolbar(manager);

            if (allSources.Length == 0)
            {
                DrawEmptyState();
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            EditorGUILayout.BeginHorizontal();
            foreach (CustomAsioAudioSource source in allSources)
            {
                DrawStrip(source, allSources, outputChannelCount);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.LabelField("Edit mode: settings can be changed, playback controls and meters work in Play mode.", EditorStyles.centeredGreyMiniLabel);
            }
        }

        private void DrawToolbar(AsioAudioManager manager)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            Color statusColor;
            string statusText;
            if (manager == null)
            {
                statusColor = AsioEditorUtility.ErrorColor;
                statusText = "No ASIO Audio Manager in the scene";
            }
            else if (!EditorApplication.isPlaying)
            {
                statusColor = AsioEditorUtility.StoppedColor;
                statusText = (string.IsNullOrEmpty(manager.AsioDriverName) ? "No driver set" : manager.AsioDriverName) + "  ·  " + manager.TargetSampleRate + " Hz";
            }
            else if (manager.IsConnected)
            {
                statusColor = AsioEditorUtility.PlayingColor;
                statusText = manager.AsioDriverName + "  ·  " + manager.TargetSampleRate + " Hz  ·  " + manager.AsioDriverOutputChannelCount + " outputs";
            }
            else
            {
                statusColor = AsioEditorUtility.ErrorColor;
                statusText = "Not connected (see Console)";
            }

            Rect dotRect = GUILayoutUtility.GetRect(16f, 18f, GUILayout.Width(16f));
            AsioEditorUtility.DrawDot(dotRect, statusColor);
            GUILayout.Label(statusText, EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();

            if (manager == null)
            {
                if (GUILayout.Button("Create Manager", EditorStyles.toolbarButton)) EditorApplication.ExecuteMenuItem("GameObject/AsioAudioUnity/ASIO Audio Manager");
            }
            else
            {
                if (EditorApplication.isPlaying && manager.isActiveAndEnabled && GUILayout.Button("Reconnect", EditorStyles.toolbarButton)) manager.Reconnect();
                if (GUILayout.Button("Manager", EditorStyles.toolbarButton))
                {
                    Selection.activeObject = manager;
                    EditorGUIUtility.PingObject(manager);
                }
            }

            if (GUILayout.Button(new GUIContent("+ Source", "Create a Custom ASIO Audio Source on the next free channel"), EditorStyles.toolbarButton))
            {
                EditorApplication.ExecuteMenuItem("GameObject/AsioAudioUnity/Custom ASIO Audio Source");
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawEmptyState()
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("There is no Custom ASIO Audio Source in the scene.", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Create Custom ASIO Audio Source", GUILayout.Width(220f))) EditorApplication.ExecuteMenuItem("GameObject/AsioAudioUnity/Custom ASIO Audio Source");
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
        }

        private void DrawStrip(CustomAsioAudioSource source, CustomAsioAudioSource[] allSources, int outputChannelCount)
        {
            bool isPlaying = EditorApplication.isPlaying;
            bool isActive = source.gameObject.activeInHierarchy && source.enabled;
            bool isSelected = Selection.Contains(source.gameObject);

            Color previousBackground = GUI.backgroundColor;
            if (isSelected) GUI.backgroundColor = new Color(0.55f, 0.75f, 1f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(StripWidth));
            GUI.backgroundColor = previousBackground;

            // Name (click to select)
            GUIStyle nameStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
            if (!isActive) nameStyle.normal.textColor = AsioEditorUtility.StoppedColor;
            if (GUILayout.Button(new GUIContent(source.name, source.name + (isActive ? "" : " (inactive)") + "\nClick to select"), nameStyle, ContentWidth))
            {
                Selection.activeObject = source.gameObject;
                EditorGUIUtility.PingObject(source.gameObject);
            }

            // Output channel
            AsioEditorUtility.ChannelIssue issue = AsioEditorUtility.GetChannelIssue(source, allSources, outputChannelCount);
            AsioEditorUtility.BuildChannelOptions(source.TargetOutputChannel, outputChannelCount, allSources, new[] { source }, out GUIContent[] channelLabels, out int[] channelValues);
            Color previousColor = GUI.color;
            if (issue != AsioEditorUtility.ChannelIssue.None) GUI.color = AsioEditorUtility.ErrorColor;
            EditorGUI.BeginChangeCheck();
            int channel = EditorGUILayout.IntPopup(source.TargetOutputChannel, channelLabels, channelValues, EditorStyles.miniPullDown, ContentWidth);
            if (EditorGUI.EndChangeCheck()) AsioEditorUtility.RecordChange(source, "Change Output Channel", () => source.TargetOutputChannel = channel);
            GUI.color = previousColor;
            if (issue != AsioEditorUtility.ChannelIssue.None)
            {
                GUILayout.Label(new GUIContent(issue == AsioEditorUtility.ChannelIssue.Duplicate ? "Channel conflict" : issue == AsioEditorUtility.ChannelIssue.OutOfRange ? "No such channel" : "No channel",
                    AsioEditorUtility.DescribeChannelIssue(issue, source, allSources, outputChannelCount)), ErrorMiniLabel(), ContentWidth);
            }

            // File
            bool fileMissing = string.IsNullOrEmpty(source.AudioFilePath) || !File.Exists(source.ResolvedAudioFilePath);
            GUIStyle fileStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft };
            if (fileMissing) fileStyle.normal.textColor = AsioEditorUtility.ErrorColor;
            string fileText = string.IsNullOrEmpty(source.AudioFilePath) ? "No audio file" : Path.GetFileName(source.AudioFilePath);
            GUILayout.Label(new GUIContent(fileText, fileMissing && !string.IsNullOrEmpty(source.AudioFilePath) ? "File not found: " + source.AudioFilePath : source.AudioFilePath), fileStyle, ContentWidth);

            // Meter and fader
            Rect faderAreaRect = GUILayoutUtility.GetRect(StripContentWidth, FaderHeight, ContentWidth);
            Rect meterRect = new Rect(faderAreaRect.center.x - 18f, faderAreaRect.y + 4f, 8f, faderAreaRect.height - 8f);
            Rect faderRect = new Rect(faderAreaRect.center.x + 2f, faderAreaRect.y, 20f, faderAreaRect.height);
            AsioEditorUtility.DrawMeter(meterRect, isPlaying ? source.MeterLevel : 0f, true);

            EditorGUI.BeginChangeCheck();
            float volume = GUI.VerticalSlider(faderRect, source.Volume, 1f, 0f);
            if (EditorGUI.EndChangeCheck()) AsioEditorUtility.RecordChange(source, "Change Volume", () => source.Volume = volume);

            string volumeText = source.Volume <= 0.0001f ? "-∞ dB" : (20f * Mathf.Log10(source.Volume)).ToString("0.0") + " dB";
            GUILayout.Label(volumeText, EditorStyles.centeredGreyMiniLabel, ContentWidth);

            // Mute / Solo
            EditorGUILayout.BeginHorizontal();
            DrawToggle(source, source.Mute, "M", "Mute", AsioEditorUtility.ErrorColor, EditorStyles.miniButtonLeft, value => source.Mute = value);
            DrawToggle(source, source.Solo, "S", "Solo", AsioEditorUtility.PausedColor, EditorStyles.miniButtonMid, value => source.Solo = value);
            DrawToggle(source, source.Loop, "Loop", "Loop", AsioEditorUtility.PlayingColor, EditorStyles.miniButtonRight, value => source.Loop = value);
            EditorGUILayout.EndHorizontal();

            // Transport
            using (new EditorGUI.DisabledScope(!isPlaying || !isActive))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("▶", "Play"), EditorStyles.miniButtonLeft)) source.Play();
                if (GUILayout.Button(new GUIContent("II", "Pause"), EditorStyles.miniButtonMid)) source.Pause();
                if (GUILayout.Button(new GUIContent("■", "Stop"), EditorStyles.miniButtonRight)) source.Stop();
                EditorGUILayout.EndHorizontal();
            }

            // Status and position
            Rect progressRect = GUILayoutUtility.GetRect(StripContentWidth, 14f, ContentWidth);
            double length = source.AudioFileTotalLength;
            if (!isPlaying || length <= 0)
            {
                AsioEditorUtility.AudioFileInfo info = AsioEditorUtility.GetAudioFileInfo(source.ResolvedAudioFilePath);
                length = info != null ? info.LengthSeconds : 0;
            }
            double seekTo = AsioEditorUtility.DrawProgressBar(progressRect, isPlaying ? source.CurrentTimestamp : 0, length, AsioEditorUtility.GetStatusColor(source.AudioStatus), isPlaying && isActive);
            if (seekTo >= 0) source.Seek(seekTo);

            EditorGUILayout.EndVertical();
        }

        private static void DrawToggle(CustomAsioAudioSource source, bool value, string label, string tooltip, Color activeColor, GUIStyle style, System.Action<bool> setter)
        {
            Color previousBackground = GUI.backgroundColor;
            if (value) GUI.backgroundColor = activeColor;

            EditorGUI.BeginChangeCheck();
            bool newValue = GUILayout.Toggle(value, new GUIContent(label, tooltip), style);
            if (EditorGUI.EndChangeCheck()) AsioEditorUtility.RecordChange(source, tooltip, () => setter(newValue));

            GUI.backgroundColor = previousBackground;
        }

        private static GUIStyle ErrorMiniLabel()
        {
            GUIStyle style = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
            style.normal.textColor = AsioEditorUtility.ErrorColor;
            return style;
        }
    }
}
