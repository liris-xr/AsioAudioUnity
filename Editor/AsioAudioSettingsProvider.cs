using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// Project Settings > ASIO Audio: the project's ASIO driver, sample rate, format, master volume, snapshots and overlay.
    /// </summary>
    internal class AsioAudioSettingsProvider : SettingsProvider
    {
        public const string SettingsPath = "Project/ASIO Audio";
        private const string DefaultAssetPath = "Assets/Settings/ASIO Audio Settings.asset";

        private static readonly GUIContent[] BitsPerSampleLabels = { new GUIContent("16-bit integer"), new GUIContent("32-bit float") };
        private static readonly int[] BitsPerSampleValues = { (int)BitsPerSample.Bits16, (int)BitsPerSample.Bits32 };

        private SerializedObject _serializedSettings;
        private Vector2 _scroll;

        private AsioAudioSettingsProvider() : base(SettingsPath, SettingsScope.Project, new[] { "ASIO", "Audio", "Driver", "Control Panel", "Buffer", "Sample Rate", "REAPER", "Spat", "Snapshot", "Master" }) { }

        // The driver loaded to show its control panel in Edit mode is released when leaving this page.
        public override void OnDeactivate()
        {
            AsioDriverControlPanel.Release();
            base.OnDeactivate();
        }

        [SettingsProvider]
        public static SettingsProvider Create() => new AsioAudioSettingsProvider();

        // ---------- Settings asset ----------

        /// <summary>The settings asset, created (in Assets/Settings) and registered if it doesn't exist yet.</summary>
        public static AsioAudioSettings GetOrCreateSettingsAsset()
        {
            if (AsioAudioSettings.HasSettingsAsset) return AsioAudioSettings.Instance;

            Directory.CreateDirectory(Path.GetDirectoryName(DefaultAssetPath));
            AsioAudioSettings settings = ScriptableObject.CreateInstance<AsioAudioSettings>();
            AssetDatabase.CreateAsset(settings, AssetDatabase.GenerateUniqueAssetPath(DefaultAssetPath));
            AssetDatabase.SaveAssets();
            Register(settings);
            Debug.Log("ASIO Audio Unity: settings created at " + AssetDatabase.GetAssetPath(settings) + ".", settings);
            return settings;
        }

        /// <summary>Makes an asset the project's ASIO Audio Settings (in the Editor and in builds).</summary>
        public static void Register(AsioAudioSettings settings)
        {
            EditorBuildSettings.AddConfigObject(AsioAudioSettings.ConfigName, settings, true);
            AsioAudioSettings.ResetInstance();
            AddToPreloadedAssets(settings);
        }

        /// <summary>Builds load the settings as a Preloaded Asset (Player Settings), before any scene.</summary>
        public static void AddToPreloadedAssets(AsioAudioSettings settings)
        {
            List<Object> preloaded = PlayerSettings.GetPreloadedAssets().ToList();
            preloaded.RemoveAll(asset => asset == null || (asset is AsioAudioSettings && asset != settings));
            if (!preloaded.Contains(settings)) preloaded.Add(settings);
            PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
        }

        // ---------- GUI ----------

        public override void OnInspectorUpdate()
        {
            if (EditorApplication.isPlaying) Repaint(); // live status and meter
        }

        public override void OnGUI(string searchContext)
        {
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 180f;
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(10, 10, 6, 10) });

            if (!AsioAudioSettings.HasSettingsAsset)
            {
                DrawNoSettings();
            }
            else
            {
                AsioAudioSettings settings = AsioAudioSettings.Instance;
                if (_serializedSettings == null || _serializedSettings.targetObject != settings) _serializedSettings = new SerializedObject(settings);
                _serializedSettings.Update();
                DrawSettings(settings);
                _serializedSettings.ApplyModifiedProperties();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
            EditorGUIUtility.labelWidth = labelWidth;
        }

        private static void DrawNoSettings()
        {
            EditorGUILayout.HelpBox("The project has no ASIO Audio Settings yet. Create them to choose the ASIO driver, sample rate and format used by all scenes.", MessageType.Info);
            if (GUILayout.Button("Create ASIO Audio Settings", GUILayout.Height(28f))) GetOrCreateSettingsAsset();

            AsioAudioManager[] sceneManagers = AsioSettingsMigration.FindSceneManagers();
            if (sceneManagers.Length > 0)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.HelpBox("The open scene has an ASIO Audio Manager from before version 3.0. Its settings can be moved here (the component is then removed from the scene).", MessageType.Warning);
                if (GUILayout.Button("Migrate the Scene's ASIO Audio Manager")) AsioSettingsMigration.Migrate();
            }
        }

        private void DrawSettings(AsioAudioSettings settings)
        {
            SerializedProperty driverName = _serializedSettings.FindProperty("_asioDriverName");
            SerializedProperty sampleRate = _serializedSettings.FindProperty("_targetSampleRate");
            SerializedProperty bitsPerSample = _serializedSettings.FindProperty("_targetBitsPerSample");
            SerializedProperty masterVolume = _serializedSettings.FindProperty("_masterVolume");

            AsioAudioManager manager = AsioEditorUtility.LiveManager;
            AsioAudioSource[] allSources = AsioEditorUtility.FindAllSources();
            int outputChannelCount = AsioEditorUtility.GetKnownOutputChannelCount();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Settings Asset"), new GUIContent(AssetDatabase.GetAssetPath(settings)));
            if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(60f)))
            {
                Selection.activeObject = settings;
                EditorGUIUtility.PingObject(settings);
            }
            EditorGUILayout.EndHorizontal();

            if (AsioSettingsMigration.FindSceneManagers().Length > 0)
            {
                EditorGUILayout.HelpBox("The open scene still has an ASIO Audio Manager from before version 3.0: it is no longer needed.", MessageType.Warning);
                if (GUILayout.Button("Remove it (its settings replace these settings)")) AsioSettingsMigration.Migrate();
            }

            EditorGUILayout.Space(4f);
            DrawStatus(manager, driverName.stringValue);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("ASIO Driver", EditorStyles.boldLabel);
            DrawDriverPopup(driverName);
            DrawSampleRatePopup(sampleRate);
            EditorGUILayout.IntPopup(bitsPerSample, BitsPerSampleLabels, BitsPerSampleValues, new GUIContent("Sample Format", "The sample format the mix is handed to NAudio in. NAudio converts it to the driver's own format."));
            DrawDriverSampleFormat(bitsPerSample);
            EditorGUILayout.PropertyField(_serializedSettings.FindProperty("_fallBackToUnityAudio"), new GUIContent("Fall Back To Unity Audio",
                "When no ASIO driver can be used (none set, not installed, or failing to start), play the sources through Unity's own audio output instead, "
                + "all channels mixed together, with a warning. Useful to work on a computer without the show's ASIO setup."));
            if (EditorApplication.isPlaying) EditorGUILayout.LabelField("Changing these settings reconnects the driver.", EditorStyles.centeredGreyMiniLabel);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Master", EditorStyles.boldLabel);
            float masterMeter = manager != null && (manager.IsConnected || manager.IsUsingUnityAudio) ? manager.MasterMeterLevel : -1f;
            EditorGUI.BeginChangeCheck();
            float volume = AsioVolumeFader.SliderRow(new GUIContent("Master Volume", "Volume applied to all sources, after their own volume (like the master fader of a mixing desk). The meter shows the level sent to the ASIO driver."),
                masterVolume.floatValue, masterMeter);
            if (EditorGUI.EndChangeCheck()) masterVolume.floatValue = volume;

            EditorGUILayout.Space(6f);
            DrawSnapshots();

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Editor", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_serializedSettings.FindProperty("_pauseWithEditor"), new GUIContent("Pause With Editor",
                "Pausing Play mode (the editor's Pause button) also pauses the playing sources, and resuming plays them again, together. "
                + "Applications following the transport (REAPER, with the REAPER Controller's Follow Transport) pause and resume too. "
                + "Without it, the ASIO audio keeps playing while the editor is paused."));

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("In-game Overlay", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_serializedSettings.FindProperty("_displayInfoOnGameWindow"), new GUIContent("Show", "Display the ASIO status, driver settings and source controls in the Game window (also in builds)."));
            EditorGUILayout.PropertyField(_serializedSettings.FindProperty("_overlayToggleKey"), new GUIContent("Toggle Key", "Key that shows or hides the overlay at runtime (None: no key)."));
            EditorGUILayout.PropertyField(_serializedSettings.FindProperty("_overlayScale"), new GUIContent("Scale", "Size of the overlay, e.g. 2 on a 4K screen."));

            EditorGUILayout.Space(6f);
            DrawChannelMap(allSources, outputChannelCount, manager);
        }

        private static void DrawStatus(AsioAudioManager manager, string driverName)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            Rect rowRect = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
            Color statusColor;
            string statusText;

            if (!EditorApplication.isPlaying)
            {
                statusColor = AsioEditorUtility.StoppedColor;
                statusText = "The ASIO driver connects in Play mode, when the first ASIO Audio Source is enabled.";
            }
            else if (manager == null)
            {
                statusColor = AsioEditorUtility.StoppedColor;
                statusText = "No ASIO Audio Source has played yet.";
            }
            else if (manager.IsConnected)
            {
                statusColor = AsioEditorUtility.PlayingColor;
                statusText = "Connected  ·  " + manager.AsioDriverOutputChannelCount + " outputs  ·  " + manager.TargetSampleRate + " Hz  ·  " + manager.AsioAudioSources.Count + " source(s)";
            }
            else if (manager.IsUsingUnityAudio)
            {
                statusColor = AsioEditorUtility.PausedColor;
                statusText = "No ASIO driver: playing through Unity's audio, all channels mixed  ·  " + manager.AsioAudioSources.Count + " source(s)";
            }
            else
            {
                statusColor = AsioEditorUtility.ErrorColor;
                statusText = "Not connected: see the Console for details.";
            }

            AsioEditorUtility.DrawDot(new Rect(rowRect.x + 2f, rowRect.y, 12f, rowRect.height), statusColor);
            GUI.Label(new Rect(rowRect.x + 20f, rowRect.y, rowRect.width - 20f, rowRect.height), statusText, EditorStyles.boldLabel);

            GUIContent controlPanelContent = new GUIContent("Driver Control Panel", "Open the ASIO driver's own settings (buffer size, clock, routing...).");
            if (manager != null)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Reconnect", EditorStyles.miniButtonLeft)) manager.Reconnect();
                using (new EditorGUI.DisabledScope(!manager.IsConnected))
                {
                    if (GUILayout.Button(controlPanelContent, EditorStyles.miniButtonRight)) manager.AsioOutPlayer.ShowControlPanel();
                }
                EditorGUILayout.EndHorizontal();
            }
            else if (!EditorApplication.isPlaying)
            {
                // Edit mode: the driver is loaded just for its control panel
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(driverName)))
                {
                    if (GUILayout.Button(controlPanelContent, AsioDriverControlPanel.LoadedDriverName != null ? EditorStyles.miniButtonLeft : EditorStyles.miniButton))
                    {
                        AsioDriverControlPanel.Show(driverName);
                    }
                }
                if (AsioDriverControlPanel.LoadedDriverName != null
                    && GUILayout.Button(new GUIContent("Release Driver", "Unload the driver (it is also released when leaving this page, entering Play mode or starting a preview)."), EditorStyles.miniButtonRight, GUILayout.Width(100f)))
                {
                    AsioDriverControlPanel.Release();
                }
                EditorGUILayout.EndHorizontal();

                if (AsioDriverControlPanel.LoadedDriverName != null)
                {
                    EditorGUILayout.LabelField("\"" + AsioDriverControlPanel.LoadedDriverName + "\" is loaded for its control panel: other applications may not be able to use it until it is released.", EditorStyles.wordWrappedMiniLabel);
                }
                if (AsioDriverControlPanel.LastError != null) EditorGUILayout.HelpBox(AsioDriverControlPanel.LastError, MessageType.Warning);
            }
            EditorGUILayout.EndVertical();
        }

        private static void DrawDriverPopup(SerializedProperty driverName)
        {
            string[] driverNames = AsioEditorUtility.GetDriverNames();
            string currentName = driverName.stringValue;

            EditorGUILayout.BeginHorizontal();
            if (driverNames.Length == 0)
            {
                EditorGUILayout.PropertyField(driverName, new GUIContent("Driver"));
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
                    if (!selectedNotInstalledEntry) driverName.stringValue = options[selectedIndex];
                }
            }
            if (GUILayout.Button(new GUIContent(EditorGUIUtility.IconContent("Refresh").image, "Refresh the list of ASIO drivers"), EditorStyles.iconButton, GUILayout.Width(20f)))
            {
                AsioEditorUtility.GetDriverNames(true);
            }
            EditorGUILayout.EndHorizontal();

            if (driverNames.Length == 0) EditorGUILayout.HelpBox("No ASIO driver is installed on this machine.", MessageType.Warning);
            else if (string.IsNullOrEmpty(currentName)) EditorGUILayout.HelpBox("Select the ASIO driver to play on.", MessageType.Warning);
            else if (!driverNames.Contains(currentName)) EditorGUILayout.HelpBox("The driver \"" + currentName + "\" is not installed on this machine.", MessageType.Warning);
        }

        private static void DrawSampleRatePopup(SerializedProperty sampleRate)
        {
            int currentRate = sampleRate.intValue;
            int[] rates = AsioEditorUtility.CommonSampleRates.Contains(currentRate)
                ? AsioEditorUtility.CommonSampleRates
                : AsioEditorUtility.CommonSampleRates.Append(currentRate).OrderBy(rate => rate).ToArray();
            GUIContent[] labels = rates.Select(rate => new GUIContent(rate + " Hz")).ToArray();
            EditorGUILayout.IntPopup(sampleRate, labels, rates, new GUIContent("Sample Rate", "The sample rate the ASIO driver runs at. It must be supported by the driver. Audio files are resampled to it."));
        }

        // The driver's outputs and own sample format (set by the driver, not by Unity), live when connected, otherwise remembered from the last time
        // the driver was loaded (Play mode, a preview, or the Driver Control Panel).
        private static void DrawDriverSampleFormat(SerializedProperty bitsPerSample)
        {
            string driverFormat = AsioEditorUtility.GetKnownDriverSampleFormat();
            AsioAudioManager manager = AsioEditorUtility.LiveManager;
            bool live = manager != null && manager.IsConnected && !string.IsNullOrEmpty(manager.AsioDriverSampleFormat);
            const string unknown = "Unknown: open the Driver Control Panel, or enter Play mode once";
            const string remembered = "  (last read from the driver)";

            int outputs = AsioEditorUtility.GetKnownOutputChannelCount();
            EditorGUILayout.LabelField(new GUIContent("Driver Outputs", "The number of output channels of the ASIO driver: the highest output channel a source can use."),
                new GUIContent(outputs > 0 ? outputs + (live ? "" : remembered) : unknown));

            string text = string.IsNullOrEmpty(driverFormat) ? unknown : driverFormat + (live ? "" : remembered);
            EditorGUILayout.LabelField(new GUIContent("Driver Format", "The sample format the ASIO driver works with. It is set by the driver (some drivers have an option in their control panel), not by Unity."), new GUIContent(text));

            if (bitsPerSample.intValue == (int)BitsPerSample.Bits16 && !string.IsNullOrEmpty(driverFormat) && driverFormat != "16-bit integer" && driverFormat != "32-bit integer")
            {
                EditorGUILayout.HelpBox("This driver works with " + driverFormat + " samples, which can't be fed with 16-bit integers: 32-bit float will be sent instead.", MessageType.Info);
            }
        }

        // Saved mixes: name (editable), Recall, Update (save the current mix into it) and Delete.
        private void DrawSnapshots()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent("Snapshots", "Saved mixes: the master volume, and the volume, mute and solo of every source (by output channel). Recall them here, from the ASIO Audio Mixer, from code (RecallSnapshot) or over OSC."), EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Save Current Mix", "Save the mix of the open scene as a new snapshot."), EditorStyles.miniButton, GUILayout.Width(110f)))
            {
                _serializedSettings.ApplyModifiedProperties();
                AsioEditorUtility.SaveSnapshot(AsioEditorUtility.NextSnapshotName());
                _serializedSettings.Update();
            }
            EditorGUILayout.EndHorizontal();

            SerializedProperty snapshots = _serializedSettings.FindProperty("_snapshots");
            if (snapshots.arraySize == 0)
            {
                EditorGUILayout.LabelField("No snapshot yet.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            for (int i = 0; i < snapshots.arraySize; i++)
            {
                SerializedProperty snapshot = snapshots.GetArrayElementAtIndex(i);
                int sourceCount = snapshot.FindPropertyRelative("Sources").arraySize;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(snapshot.FindPropertyRelative("Name"), GUIContent.none);
                GUILayout.Label(sourceCount + (sourceCount == 1 ? " channel" : " channels"), EditorStyles.miniLabel, GUILayout.Width(70f));
                bool recall = GUILayout.Button(new GUIContent("Recall", "Apply this snapshot to the open scene"), EditorStyles.miniButtonLeft, GUILayout.Width(52f));
                bool update = GUILayout.Button(new GUIContent("Update", "Save the current mix into this snapshot"), EditorStyles.miniButtonMid, GUILayout.Width(52f));
                bool delete = GUILayout.Button(new GUIContent("×", "Delete this snapshot"), EditorStyles.miniButtonRight, GUILayout.Width(22f));
                EditorGUILayout.EndHorizontal();

                if (recall || update || delete)
                {
                    _serializedSettings.ApplyModifiedProperties();
                    if (recall) AsioEditorUtility.RecallSnapshot(i);
                    if (update) AsioEditorUtility.SaveSnapshot(AsioEditorUtility.Settings.Snapshots[i].Name);
                    if (delete) AsioEditorUtility.DeleteSnapshot(i);
                    _serializedSettings.Update();
                    GUIUtility.ExitGUI(); // The list changed
                }
            }
        }

        private static void DrawChannelMap(AsioAudioSource[] allSources, int outputChannelCount, AsioAudioManager manager)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Output Channels (open scene)", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Open Mixer", EditorStyles.miniButton, GUILayout.Width(80f))) AsioAudioMixerWindow.Open();
            EditorGUILayout.EndHorizontal();

            if (allSources.Length == 0)
            {
                EditorGUILayout.HelpBox("There is no ASIO Audio Source in the open scene.", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            foreach (AsioAudioSource source in allSources)
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
                GUI.Label(channelRect, new GUIContent(AsioEditorUtility.DescribeChannels(source), AsioEditorUtility.DescribeChannelIssue(issue, source, allSources, outputChannelCount)), channelStyle);

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

            bool live = manager != null && manager.IsConnected;
            string outputsText = outputChannelCount > 0
                ? "The driver has " + outputChannelCount + " outputs" + (live ? "." : " (last read from the driver).")
                : "The number of outputs of the driver is unknown: open the Driver Control Panel, or enter Play mode once.";
            EditorGUILayout.LabelField(outputsText, EditorStyles.wordWrappedMiniLabel);
        }
    }

    /// <summary>Makes sure builds include the ASIO Audio Settings (as a Preloaded Asset).</summary>
    internal class AsioAudioSettingsBuildProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (AsioAudioSettings.HasSettingsAsset) AsioAudioSettingsProvider.AddToPreloadedAssets(AsioAudioSettings.Instance);
            else Debug.LogWarning("ASIO Audio Unity: the project has no ASIO Audio Settings (Project Settings > ASIO Audio): the build will have no ASIO driver set.");
        }
    }
}
