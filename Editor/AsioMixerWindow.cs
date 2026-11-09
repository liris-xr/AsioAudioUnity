using System.IO;
using UnityEditor;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// A mixer-style window showing every ASIO Audio Source of the scene as a channel strip:
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
            AsioAudioSource[] allSources = AsioEditorUtility.FindAllSources();
            int outputChannelCount = AsioEditorUtility.GetKnownOutputChannelCount(manager);

            DrawToolbar(manager);

            if (allSources.Length == 0)
            {
                DrawEmptyState();
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            EditorGUILayout.BeginHorizontal();
            if (manager != null)
            {
                DrawMasterStrip(manager);
                GUILayout.Space(8f);
            }
            foreach (AsioAudioSource source in allSources)
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
                if (EditorGUILayout.DropdownButton(new GUIContent("Snapshots", "Save and recall mixes: master volume, and volume / mute / solo of every source."), FocusType.Passive, EditorStyles.toolbarDropDown))
                {
                    ShowSnapshotMenu(manager);
                }
            }

            if (GUILayout.Button(new GUIContent("+ Source", "Create a ASIO Audio Source on the next free channel"), EditorStyles.toolbarButton))
            {
                EditorApplication.ExecuteMenuItem("GameObject/AsioAudioUnity/ASIO Audio Source");
            }

            EditorGUILayout.EndHorizontal();
        }

        private static void ShowSnapshotMenu(AsioAudioManager manager)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Save Current Mix as New Snapshot"), false, () => AsioEditorUtility.SaveSnapshot(manager, AsioEditorUtility.NextSnapshotName(manager)));

            if (manager.Snapshots.Count > 0) menu.AddSeparator("");
            for (int i = 0; i < manager.Snapshots.Count; i++)
            {
                int index = i;
                string label = (i + 1) + ". " + manager.Snapshots[i].Name.Replace("/", "∕"); // A '/' would create a submenu
                menu.AddItem(new GUIContent("Recall/" + label), false, () => AsioEditorUtility.RecallSnapshot(manager, index));
                menu.AddItem(new GUIContent("Update with Current Mix/" + label), false, () => AsioEditorUtility.SaveSnapshot(manager, manager.Snapshots[index].Name));
                menu.AddItem(new GUIContent("Delete/" + label), false, () => AsioEditorUtility.DeleteSnapshot(manager, index));
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Rename in the ASIO Audio Manager…"), false, () =>
            {
                Selection.activeObject = manager;
                EditorGUIUtility.PingObject(manager);
            });
            menu.ShowAsContext();
        }

        private void DrawEmptyState()
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("There is no ASIO Audio Source in the scene.", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Create ASIO Audio Source", GUILayout.Width(220f))) EditorApplication.ExecuteMenuItem("GameObject/AsioAudioUnity/ASIO Audio Source");
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
        }

        // The master volume of the manager, applied to all sources, with the level sent to the driver.
        private void DrawMasterStrip(AsioAudioManager manager)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(StripWidth));

            GUIStyle nameStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
            if (GUILayout.Button(new GUIContent("Master", "Volume applied to all sources\nClick to select the ASIO Audio Manager"), nameStyle, ContentWidth))
            {
                Selection.activeObject = manager;
                EditorGUIUtility.PingObject(manager);
            }
            GUILayout.Label("All outputs", EditorStyles.centeredGreyMiniLabel, ContentWidth);
            GUILayout.Label(manager.IsConnected ? "To the driver" : "", EditorStyles.centeredGreyMiniLabel, ContentWidth);
            GUILayout.Space(4f); // aligns the fader with the source strips (their channel popup is taller than a label)

            Rect faderAreaRect = GUILayoutUtility.GetRect(StripContentWidth, FaderHeight, ContentWidth);
            Rect faderRect = new Rect(faderAreaRect.center.x - AsioVolumeFader.VerticalWidth / 2f, faderAreaRect.y, AsioVolumeFader.VerticalWidth, faderAreaRect.height);

            EditorGUI.BeginChangeCheck();
            float volume = AsioVolumeFader.Draw(faderRect, manager.MasterVolume, EditorApplication.isPlaying && manager.IsConnected ? manager.MasterMeterLevel : 0f, true);
            if (EditorGUI.EndChangeCheck()) AsioEditorUtility.RecordChange(manager, "Change Master Volume", () => manager.MasterVolume = volume);

            GUILayout.Label(new GUIContent(AsioVolume.FormatLinear(manager.MasterVolume), "Master volume (double-click the fader to reset to 0 dB)"), EditorStyles.centeredGreyMiniLabel, ContentWidth);

            // How many sources play (on the row of the sources' Mute / Solo / Loop buttons)
            bool isPlaying = EditorApplication.isPlaying;
            int playingCount = 0, pausedCount = 0;
            foreach (AsioAudioSource source in manager.AsioAudioSources)
            {
                if (!source) continue;
                if (source.AudioStatus == AsioAudioStatus.Playing) playingCount++;
                else if (source.AudioStatus == AsioAudioStatus.Paused) pausedCount++;
            }
            string summary = !isPlaying ? "Edit mode" : playingCount + " playing" + (pausedCount > 0 ? ", " + pausedCount + " paused" : "");
            GUILayout.Label(summary, EditorStyles.centeredGreyMiniLabel, ContentWidth, GUILayout.Height(18f));

            // Transport for all sources (on the row of the sources' transport buttons)
            using (new EditorGUI.DisabledScope(!isPlaying || !manager.isActiveAndEnabled))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("▶", "Play all sources (they start on the same sample)"), EditorStyles.miniButtonLeft)) manager.PlayAll();
                if (GUILayout.Button(new GUIContent("II", "Pause all sources"), EditorStyles.miniButtonMid)) manager.PauseAll();
                if (GUILayout.Button(new GUIContent("■", "Stop all sources"), EditorStyles.miniButtonMid)) manager.StopAll();
                if (GUILayout.Button(new GUIContent("|◀", "Play all sources from the start, together"), EditorStyles.miniButtonRight)) manager.RestartAll();
                EditorGUILayout.EndHorizontal();
            }
            GUILayout.Label("All sources", EditorStyles.centeredGreyMiniLabel, ContentWidth);

            EditorGUILayout.EndVertical();
        }

        private void DrawStrip(AsioAudioSource source, AsioAudioSource[] allSources, int outputChannelCount)
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

            // Fader with its dB scale and meter (double-click: 0 dB)
            Rect faderAreaRect = GUILayoutUtility.GetRect(StripContentWidth, FaderHeight, ContentWidth);
            Rect faderRect = new Rect(faderAreaRect.center.x - AsioVolumeFader.VerticalWidth / 2f, faderAreaRect.y, AsioVolumeFader.VerticalWidth, faderAreaRect.height);

            EditorGUI.BeginChangeCheck();
            float volume = AsioVolumeFader.Draw(faderRect, source.Volume, isPlaying ? source.MeterLevel : 0f, true);
            if (EditorGUI.EndChangeCheck()) AsioEditorUtility.RecordChange(source, "Change Volume", () => source.Volume = volume);

            GUILayout.Label(new GUIContent(AsioVolume.FormatLinear(source.Volume), "Volume (double-click the fader to reset to 0 dB)"), EditorStyles.centeredGreyMiniLabel, ContentWidth);

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

        private static void DrawToggle(AsioAudioSource source, bool value, string label, string tooltip, Color activeColor, GUIStyle style, System.Action<bool> setter)
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
