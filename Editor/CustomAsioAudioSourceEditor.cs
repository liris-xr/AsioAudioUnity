using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AsioAudioUnity
{
    [CustomEditor(typeof(CustomAsioAudioSource)), CanEditMultipleObjects]
    public class CustomAsioAudioSourceEditor : Editor
    {
        private static bool _showEvents;

        private SerializedProperty _targetOutputChannel;
        private SerializedProperty _referencedAsioAudioManager;
        private SerializedProperty _volume;
        private SerializedProperty _mute;
        private SerializedProperty _solo;
        private SerializedProperty _playOnEnable;
        private SerializedProperty _loop;
        private SerializedProperty _preloadIntoMemory;
        private SerializedProperty _onPlay;
        private SerializedProperty _onPause;
        private SerializedProperty _onStop;

        private CustomAsioAudioSource[] Sources => targets.Cast<CustomAsioAudioSource>().ToArray();

        private void OnEnable()
        {
            _targetOutputChannel = serializedObject.FindProperty("_targetOutputChannel");
            _referencedAsioAudioManager = serializedObject.FindProperty("_referencedAsioAudioManager");
            _volume = serializedObject.FindProperty("_volume");
            _mute = serializedObject.FindProperty("_mute");
            _solo = serializedObject.FindProperty("_solo");
            _playOnEnable = serializedObject.FindProperty("_playOnEnable");
            _loop = serializedObject.FindProperty("_loop");
            _preloadIntoMemory = serializedObject.FindProperty("_preloadIntoMemory");
            _onPlay = serializedObject.FindProperty("_onPlay");
            _onPause = serializedObject.FindProperty("_onPause");
            _onStop = serializedObject.FindProperty("_onStop");
        }

        // Keep the status, progress bar and level meter live in Play mode.
        public override bool RequiresConstantRepaint() => EditorApplication.isPlaying;

        public override void OnInspectorGUI()
        {
            AsioAudioManager manager = AsioEditorUtility.FindManager();
            CustomAsioAudioSource[] allSources = AsioEditorUtility.FindAllSources();
            int outputChannelCount = AsioEditorUtility.GetKnownOutputChannelCount(manager);

            // The audio file is set through the component's property (it reloads the file in Play mode), before the serialized object is read.
            DrawAudioFileSection();

            serializedObject.Update();

            if (manager == null)
            {
                EditorGUILayout.HelpBox("There is no ASIO Audio Manager in the scene: this source won't play.", MessageType.Warning);
                if (GUILayout.Button("Create ASIO Audio Manager")) EditorApplication.ExecuteMenuItem("GameObject/AsioAudioUnity/ASIO Audio Manager");
            }

            EditorGUILayout.Space(6f);
            DrawPlaybackSection();

            EditorGUILayout.Space(6f);
            DrawOutputSection(allSources, outputChannelCount);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_playOnEnable);
            EditorGUILayout.PropertyField(_loop);
            EditorGUILayout.PropertyField(_preloadIntoMemory, new GUIContent("Preload Into Memory",
                "Decode the whole file into memory when loading, instead of reading it from disk while playing. Avoids dropouts with many sources or slow disks, but uses about 10 MB per minute of audio and takes longer to load."));
            EditorGUILayout.PropertyField(_referencedAsioAudioManager, new GUIContent("ASIO Audio Manager", "Leave empty to use the ASIO Audio Manager of the scene."));

            EditorGUILayout.Space(6f);
            _showEvents = EditorGUILayout.Foldout(_showEvents, "Events", true);
            if (_showEvents)
            {
                EditorGUILayout.PropertyField(_onPlay);
                EditorGUILayout.PropertyField(_onPause);
                EditorGUILayout.PropertyField(_onStop);
            }

            serializedObject.ApplyModifiedProperties();
        }

        // ---------- Audio file ----------

        private void DrawAudioFileSection()
        {
            EditorGUILayout.LabelField("Audio File", EditorStyles.boldLabel);

            CustomAsioAudioSource[] sources = Sources;
            string firstPath = sources[0].AudioFilePath;
            bool samePath = sources.All(source => source.AudioFilePath == firstPath);

            string dropText = !samePath ? "—  (different files)" : string.IsNullOrEmpty(firstPath) ? "Drop an audio file here" : Path.GetFileName(firstPath);
            Rect dropArea = GUILayoutUtility.GetRect(0f, 36f, GUILayout.ExpandWidth(true));
            GUIStyle dropStyle = new GUIStyle(EditorStyles.helpBox) { alignment = TextAnchor.MiddleCenter, fontSize = 13, clipping = TextClipping.Clip };
            GUI.Box(dropArea, new GUIContent(dropText, samePath ? firstPath : null), dropStyle);
            HandleAudioFileDrop(dropArea);

            EditorGUILayout.BeginHorizontal();
            if (samePath && !string.IsNullOrEmpty(firstPath))
            {
                AsioEditorUtility.AudioFileInfo info = AsioEditorUtility.GetAudioFileInfo(sources[0].ResolvedAudioFilePath);
                EditorGUILayout.LabelField(info != null ? AsioEditorUtility.DescribeAudioFile(info) : "", EditorStyles.miniLabel);
            }
            else GUILayout.FlexibleSpace();

            if (GUILayout.Button("Browse…", EditorStyles.miniButtonLeft, GUILayout.Width(64f))) BrowseForAudioFile();
            using (new EditorGUI.DisabledScope(sources.All(source => string.IsNullOrEmpty(source.AudioFilePath))))
            {
                if (GUILayout.Button("Clear", EditorStyles.miniButtonRight, GUILayout.Width(44f))) SetAudioFilePath("");
            }
            EditorGUILayout.EndHorizontal();

            // File problems
            CustomAsioAudioSource[] missingFile = sources.Where(source => !string.IsNullOrEmpty(source.AudioFilePath) && !File.Exists(source.ResolvedAudioFilePath)).ToArray();
            if (missingFile.Length > 0)
            {
                EditorGUILayout.HelpBox("Audio file not found: " + string.Join(", ", missingFile.Select(source => source.AudioFilePath)), MessageType.Error);
            }
            else if (sources.Any(source => string.IsNullOrEmpty(source.AudioFilePath)))
            {
                EditorGUILayout.HelpBox("No audio file is set: this source will be ignored.", MessageType.Warning);
            }
            if (sources.Any(source => AsioEditorUtility.IsOutsideProject(source.AudioFilePath)))
            {
                EditorGUILayout.HelpBox("The audio file is outside the project folder: it won't be copied to builds.", MessageType.Info);
            }
        }

        private void HandleAudioFileDrop(Rect dropArea)
        {
            Event currentEvent = Event.current;
            if (currentEvent.type != EventType.DragUpdated && currentEvent.type != EventType.DragPerform) return;
            if (!dropArea.Contains(currentEvent.mousePosition) || DragAndDrop.paths.Length == 0) return;

            string filePath = DragAndDrop.paths[0];
            if (!AsioEditorUtility.IsAudioFile(filePath))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            // Only assign the file when it is actually dropped on the area (not when the drag merely passes over it).
            if (currentEvent.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                SetAudioFilePath(AsioEditorUtility.ToProjectRelativePath(filePath));
            }

            currentEvent.Use();
        }

        private void BrowseForAudioFile()
        {
            string currentPath = Sources[0].ResolvedAudioFilePath;
            string startDirectory = !string.IsNullOrEmpty(currentPath) && Directory.Exists(Path.GetDirectoryName(currentPath)) ? Path.GetDirectoryName(currentPath) : Application.dataPath;

            string selectedPath = EditorUtility.OpenFilePanelWithFilters("Select an audio file", startDirectory, new[] { "Audio files", "wav,mp3,ogg,aiff,aif,flac", "All files", "*" });
            if (!string.IsNullOrEmpty(selectedPath)) SetAudioFilePath(AsioEditorUtility.ToProjectRelativePath(selectedPath));

            GUIUtility.ExitGUI(); // The file dialog breaks the current GUI layout
        }

        private void SetAudioFilePath(string audioFilePath)
        {
            foreach (CustomAsioAudioSource source in Sources)
            {
                AsioEditorUtility.RecordChange(source, "Set Audio File", () => source.AudioFilePath = audioFilePath);
            }
        }

        // ---------- Playback ----------

        private void DrawPlaybackSection()
        {
            EditorGUILayout.LabelField("Playback", EditorStyles.boldLabel);
            CustomAsioAudioSource[] sources = Sources;
            bool isPlaying = EditorApplication.isPlaying;

            if (sources.Length == 1)
            {
                CustomAsioAudioSource source = sources[0];
                double length = source.AudioFileTotalLength;
                if (!isPlaying || length <= 0)
                {
                    AsioEditorUtility.AudioFileInfo info = AsioEditorUtility.GetAudioFileInfo(source.ResolvedAudioFilePath);
                    if (info != null) length = info.LengthSeconds;
                }

                Rect statusRect = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
                Rect dotRect = new Rect(statusRect.x, statusRect.y, 14f, statusRect.height);
                Rect labelRect = new Rect(statusRect.x + 18f, statusRect.y, 70f, statusRect.height);
                Rect barRect = new Rect(labelRect.xMax, statusRect.y + 1f, statusRect.xMax - labelRect.xMax, statusRect.height - 2f);

                Color statusColor = AsioEditorUtility.GetStatusColor(source.AudioStatus);
                AsioEditorUtility.DrawDot(dotRect, isPlaying ? statusColor : AsioEditorUtility.StoppedColor);
                GUI.Label(labelRect, isPlaying ? source.AudioStatus.ToString() : "Edit mode");

                double seekTo = AsioEditorUtility.DrawProgressBar(barRect, isPlaying ? source.CurrentTimestamp : 0, length, statusColor, isPlaying);
                if (seekTo >= 0) source.Seek(seekTo);
            }

            using (new EditorGUI.DisabledScope(!isPlaying))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Play", GUILayout.Height(24f))) foreach (CustomAsioAudioSource source in sources) source.Play();
                if (GUILayout.Button("Pause", GUILayout.Height(24f))) foreach (CustomAsioAudioSource source in sources) source.Pause();
                if (GUILayout.Button("Stop", GUILayout.Height(24f))) foreach (CustomAsioAudioSource source in sources) source.Stop();
                EditorGUILayout.EndHorizontal();
            }
            if (!isPlaying) EditorGUILayout.LabelField("Enter Play mode to control playback.", EditorStyles.centeredGreyMiniLabel);
        }

        // ---------- Output ----------

        private void DrawOutputSection(CustomAsioAudioSource[] allSources, int outputChannelCount)
        {
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            CustomAsioAudioSource[] sources = Sources;

            // Output channel
            if (sources.Length == 1)
            {
                AsioEditorUtility.BuildChannelOptions(_targetOutputChannel.intValue, outputChannelCount, allSources, sources, out GUIContent[] labels, out int[] values);
                EditorGUILayout.IntPopup(_targetOutputChannel, labels, values, new GUIContent("Output Channel", "The ASIO output this source plays on. Each source needs its own channel."));

                AsioEditorUtility.ChannelIssue issue = AsioEditorUtility.GetChannelIssue(sources[0], allSources, outputChannelCount);
                if (issue != AsioEditorUtility.ChannelIssue.None)
                {
                    EditorGUILayout.HelpBox(AsioEditorUtility.DescribeChannelIssue(issue, sources[0], allSources, outputChannelCount), issue == AsioEditorUtility.ChannelIssue.Duplicate ? MessageType.Error : MessageType.Warning);
                }
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                string channels = string.Join(", ", sources.Select(source => source.TargetOutputChannel > 0 ? source.TargetOutputChannel.ToString() : "None"));
                EditorGUILayout.LabelField(new GUIContent("Output Channels", "Each source needs its own channel: select a single source to choose its channel."), new GUIContent(channels));
                if (GUILayout.Button(new GUIContent("Auto-assign", "Give each selected source the lowest free channel."), EditorStyles.miniButton, GUILayout.Width(80f)))
                {
                    AsioEditorUtility.AutoAssignChannels(sources, allSources);
                    serializedObject.Update();
                }
                EditorGUILayout.EndHorizontal();
            }

            if (outputChannelCount == 0) EditorGUILayout.LabelField("The number of outputs of the ASIO driver will be known after entering Play mode once.", EditorStyles.wordWrappedMiniLabel);

            // Volume, with the level meter in Play mode
            EditorGUILayout.PropertyField(_volume);
            if (EditorApplication.isPlaying && sources.Length == 1)
            {
                Rect meterRect = EditorGUI.IndentedRect(GUILayoutUtility.GetRect(0f, 6f, GUILayout.ExpandWidth(true)));
                meterRect.xMin += EditorGUIUtility.labelWidth;
                AsioEditorUtility.DrawMeter(meterRect, sources[0].MeterLevel, false);
                EditorGUILayout.Space(2f);
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel(new GUIContent("Mute / Solo", "Mute silences this source. When any source is soloed, only soloed sources are heard."));
            DrawToggleButton(_mute, "Mute", AsioEditorUtility.ErrorColor);
            DrawToggleButton(_solo, "Solo", AsioEditorUtility.PausedColor);
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawToggleButton(SerializedProperty property, string label, Color activeColor)
        {
            Color previousColor = GUI.backgroundColor;
            if (property.boolValue && !property.hasMultipleDifferentValues) GUI.backgroundColor = activeColor;

            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            bool value = GUILayout.Toggle(property.boolValue, label, EditorStyles.miniButton);
            if (EditorGUI.EndChangeCheck()) property.boolValue = value;
            EditorGUI.showMixedValue = false;

            GUI.backgroundColor = previousColor;
        }
    }
}
