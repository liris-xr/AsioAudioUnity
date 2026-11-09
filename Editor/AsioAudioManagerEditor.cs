using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AsioAudioUnity
{
    [CustomEditor(typeof(AsioAudioManager))]
    public class AsioAudioManagerEditor : Editor
    {
        private static readonly GUIContent[] BitsPerSampleLabels = { new GUIContent("16-bit integer"), new GUIContent("32-bit float") };
        private static readonly int[] BitsPerSampleValues = { (int)BitsPerSample.Bits16, (int)BitsPerSample.Bits32 };

        private SerializedProperty _asioDriverName;
        private SerializedProperty _targetSampleRate;
        private SerializedProperty _targetBitsPerSample;
        private SerializedProperty _displayInfoOnGameWindow;

        private AsioAudioManager Manager => (AsioAudioManager)target;

        private void OnEnable()
        {
            _asioDriverName = serializedObject.FindProperty("_asioDriverName");
            _targetSampleRate = serializedObject.FindProperty("_targetSampleRate");
            _targetBitsPerSample = serializedObject.FindProperty("_targetBitsPerSample");
            _displayInfoOnGameWindow = serializedObject.FindProperty("_displayInfoOnGameWindow");
        }

        public override bool RequiresConstantRepaint() => EditorApplication.isPlaying;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            CustomAsioAudioSource[] allSources = AsioEditorUtility.FindAllSources();
            int outputChannelCount = AsioEditorUtility.GetKnownOutputChannelCount(Manager);

            DrawStatus(outputChannelCount);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("ASIO Driver", EditorStyles.boldLabel);
            DrawDriverPopup();
            DrawSampleRatePopup();
            EditorGUILayout.IntPopup(_targetBitsPerSample, BitsPerSampleLabels, BitsPerSampleValues, new GUIContent("Sample Format", "The sample format sent to the ASIO driver."));
            if (EditorApplication.isPlaying) EditorGUILayout.LabelField("Changing these settings reconnects the driver.", EditorStyles.centeredGreyMiniLabel);

            EditorGUILayout.Space(6f);
            EditorGUILayout.PropertyField(_displayInfoOnGameWindow, new GUIContent("Game Window Overlay", "Display the ASIO status and source controls in the Game window."));

            EditorGUILayout.Space(6f);
            DrawChannelMap(allSources, outputChannelCount);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawStatus(int outputChannelCount)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            Rect rowRect = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
            Color statusColor;
            string statusText;

            if (!EditorApplication.isPlaying)
            {
                statusColor = AsioEditorUtility.StoppedColor;
                statusText = "Connects to the ASIO driver when entering Play mode.";
            }
            else if (Manager.IsConnected)
            {
                statusColor = AsioEditorUtility.PlayingColor;
                statusText = "Connected  ·  " + Manager.AsioDriverOutputChannelCount + " outputs  ·  " + Manager.TargetSampleRate + " Hz";
            }
            else
            {
                statusColor = AsioEditorUtility.ErrorColor;
                statusText = Manager.isActiveAndEnabled ? "Not connected: see the Console for details." : "Disabled.";
            }

            AsioEditorUtility.DrawDot(new Rect(rowRect.x + 2f, rowRect.y, 12f, rowRect.height), statusColor);
            GUI.Label(new Rect(rowRect.x + 20f, rowRect.y, rowRect.width - 20f, rowRect.height), statusText, EditorStyles.boldLabel);

            if (EditorApplication.isPlaying && Manager.isActiveAndEnabled)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Reconnect", EditorStyles.miniButtonLeft)) Manager.Reconnect();
                using (new EditorGUI.DisabledScope(!Manager.IsConnected))
                {
                    if (GUILayout.Button(new GUIContent("Driver Control Panel", "Open the ASIO driver's own settings (buffer size, clock...)."), EditorStyles.miniButtonRight)) Manager.AsioOutPlayer.ShowControlPanel();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawDriverPopup()
        {
            string[] driverNames = AsioEditorUtility.GetDriverNames();
            string currentName = _asioDriverName.stringValue;

            EditorGUILayout.BeginHorizontal();

            if (driverNames.Length == 0)
            {
                EditorGUILayout.PropertyField(_asioDriverName, new GUIContent("Driver"));
            }
            else
            {
                // Keep a driver name that isn't installed on this machine selectable (the project may be used on another machine).
                bool currentIsInstalled = driverNames.Contains(currentName);
                string[] options = currentIsInstalled || string.IsNullOrEmpty(currentName)
                    ? driverNames
                    : new[] { currentName + "  (not installed)" }.Concat(driverNames).ToArray();
                int currentIndex = currentIsInstalled ? System.Array.IndexOf(options, currentName) : string.IsNullOrEmpty(currentName) ? -1 : 0;

                EditorGUI.BeginChangeCheck();
                int selectedIndex = EditorGUILayout.Popup(new GUIContent("Driver", "The ASIO driver to play on."), currentIndex, options.Select(option => new GUIContent(option.Replace("/", "∕"))).ToArray());
                if (EditorGUI.EndChangeCheck() && selectedIndex >= 0)
                {
                    bool selectedNotInstalledEntry = !currentIsInstalled && !string.IsNullOrEmpty(currentName) && selectedIndex == 0;
                    if (!selectedNotInstalledEntry) _asioDriverName.stringValue = options[selectedIndex];
                }
            }

            if (GUILayout.Button(new GUIContent(EditorGUIUtility.IconContent("Refresh").image, "Refresh the list of ASIO drivers"), EditorStyles.iconButton, GUILayout.Width(20f)))
            {
                AsioEditorUtility.GetDriverNames(true);
            }

            EditorGUILayout.EndHorizontal();

            if (driverNames.Length == 0)
            {
                EditorGUILayout.HelpBox("No ASIO driver is installed on this machine.", MessageType.Warning);
            }
            else if (string.IsNullOrEmpty(currentName))
            {
                EditorGUILayout.HelpBox("Select the ASIO driver to play on.", MessageType.Warning);
            }
            else if (!driverNames.Contains(currentName))
            {
                EditorGUILayout.HelpBox("The driver \"" + currentName + "\" is not installed on this machine.", MessageType.Warning);
            }
        }

        private void DrawSampleRatePopup()
        {
            int currentRate = _targetSampleRate.intValue;
            int[] rates = AsioEditorUtility.CommonSampleRates.Contains(currentRate)
                ? AsioEditorUtility.CommonSampleRates
                : AsioEditorUtility.CommonSampleRates.Append(currentRate).OrderBy(rate => rate).ToArray();
            GUIContent[] labels = rates.Select(rate => new GUIContent(rate + " Hz")).ToArray();

            EditorGUILayout.IntPopup(_targetSampleRate, labels, rates, new GUIContent("Sample Rate", "The sample rate the ASIO driver runs at. It must be supported by the driver. Audio files are resampled to it."));
        }

        private void DrawChannelMap(CustomAsioAudioSource[] allSources, int outputChannelCount)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Output Channels", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Open Mixer", EditorStyles.miniButton, GUILayout.Width(80f))) AsioMixerWindow.Open();
            EditorGUILayout.EndHorizontal();

            if (allSources.Length == 0)
            {
                EditorGUILayout.HelpBox("There is no Custom ASIO Audio Source in the scene.", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            foreach (CustomAsioAudioSource source in allSources)
            {
                AsioEditorUtility.ChannelIssue issue = AsioEditorUtility.GetChannelIssue(source, allSources, outputChannelCount);
                bool fileMissing = string.IsNullOrEmpty(source.AudioFilePath) || !File.Exists(source.ResolvedAudioFilePath);

                Rect rowRect = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
                Rect channelRect = new Rect(rowRect.x + 2f, rowRect.y, 28f, rowRect.height);
                Rect dotRect = new Rect(channelRect.xMax, rowRect.y, 12f, rowRect.height);
                Rect nameRect = new Rect(dotRect.xMax + 4f, rowRect.y, (rowRect.xMax - dotRect.xMax) * 0.45f, rowRect.height);
                Rect fileRect = new Rect(nameRect.xMax + 4f, rowRect.y, rowRect.xMax - nameRect.xMax - 4f, rowRect.height);

                GUIStyle channelStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleRight };
                if (issue != AsioEditorUtility.ChannelIssue.None) channelStyle.normal.textColor = AsioEditorUtility.ErrorColor;
                string issueText = AsioEditorUtility.DescribeChannelIssue(issue, source, allSources, outputChannelCount);
                GUI.Label(channelRect, new GUIContent(source.TargetOutputChannel > 0 ? source.TargetOutputChannel.ToString() : "–", issueText), channelStyle);

                AsioEditorUtility.DrawDot(dotRect, EditorApplication.isPlaying ? AsioEditorUtility.GetStatusColor(source.AudioStatus) : AsioEditorUtility.StoppedColor);

                string sourceName = source.name + (source.gameObject.activeInHierarchy && source.enabled ? "" : "  (inactive)");
                if (GUI.Button(nameRect, new GUIContent(sourceName, "Select this source"), EditorStyles.label))
                {
                    Selection.activeObject = source;
                    EditorGUIUtility.PingObject(source);
                }

                GUIStyle fileStyle = new GUIStyle(EditorStyles.miniLabel);
                if (fileMissing) fileStyle.normal.textColor = AsioEditorUtility.ErrorColor;
                string fileText = string.IsNullOrEmpty(source.AudioFilePath) ? "No audio file" : Path.GetFileName(source.AudioFilePath) + (fileMissing ? "  (not found)" : "");
                GUI.Label(fileRect, new GUIContent(fileText, source.AudioFilePath), fileStyle);
            }
            EditorGUILayout.EndVertical();

            int issueCount = allSources.Count(source => AsioEditorUtility.GetChannelIssue(source, allSources, outputChannelCount) != AsioEditorUtility.ChannelIssue.None);
            if (issueCount > 0)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.HelpBox(issueCount + " source(s) have no valid output channel (hover the channel numbers for details).", MessageType.Warning);
                if (GUILayout.Button(new GUIContent("Fix", "Give these sources the lowest free channels."), GUILayout.Width(40f), GUILayout.ExpandHeight(true)))
                {
                    AsioEditorUtility.AutoAssignChannels(allSources.Where(source => AsioEditorUtility.GetChannelIssue(source, allSources, outputChannelCount) != AsioEditorUtility.ChannelIssue.None).ToList(), allSources);
                }
                EditorGUILayout.EndHorizontal();
            }

            string outputsText = outputChannelCount > 0
                ? "The driver has " + outputChannelCount + " outputs" + (Manager.IsConnected ? "." : " (last seen in Play mode).")
                : "The number of outputs of the driver will be known after entering Play mode once.";
            EditorGUILayout.LabelField(outputsText, EditorStyles.wordWrappedMiniLabel);
        }
    }
}
