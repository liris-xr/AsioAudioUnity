using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AsioAudioUnity
{
    [CustomEditor(typeof(AsioAudioSource)), CanEditMultipleObjects]
    public class AsioAudioSourceEditor : Editor
    {
        private enum Glyph { Play, Pause, Stop, Restart }

        private static bool _showAdvanced;
        private static bool _showEvents;

        private SerializedProperty _targetOutputChannel;
        private SerializedProperty _volume;
        private SerializedProperty _mute;
        private SerializedProperty _solo;
        private SerializedProperty _playOnEnable;
        private SerializedProperty _loop;
        private SerializedProperty _preloadIntoMemory;
        private SerializedProperty _fadeInDuration;
        private SerializedProperty _fadeOutDuration;
        private SerializedProperty _fadeInCurve;
        private SerializedProperty _loopCrossfade;
        private SerializedProperty _fadeOutCurve;
        private SerializedProperty _onPlay;
        private SerializedProperty _onPause;
        private SerializedProperty _onStop;

        private AsioWaveformView _waveformView;

        private static GUIStyle _cardStyle, _cardTitleStyle, _pillStyle, _dropTitleStyle, _dropHintStyle, _rightMiniLabelStyle, _transportLabelStyle, _foldoutStyle;

        private static Color AccentColor => EditorGUIUtility.isProSkin ? new Color(0.35f, 0.6f, 0.95f) : new Color(0.25f, 0.5f, 0.9f);

        private AsioAudioSource[] Sources => targets.Cast<AsioAudioSource>().ToArray();

        private void OnEnable()
        {
            _targetOutputChannel = serializedObject.FindProperty("_targetOutputChannel");
            _volume = serializedObject.FindProperty("_volume");
            _mute = serializedObject.FindProperty("_mute");
            _solo = serializedObject.FindProperty("_solo");
            _playOnEnable = serializedObject.FindProperty("_playOnEnable");
            _loop = serializedObject.FindProperty("_loop");
            _preloadIntoMemory = serializedObject.FindProperty("_preloadIntoMemory");
            _fadeInDuration = serializedObject.FindProperty("_fadeInDuration");
            _fadeOutDuration = serializedObject.FindProperty("_fadeOutDuration");
            _fadeInCurve = serializedObject.FindProperty("_fadeInCurve");
            _loopCrossfade = serializedObject.FindProperty("_loopCrossfade");
            _fadeOutCurve = serializedObject.FindProperty("_fadeOutCurve");
            _onPlay = serializedObject.FindProperty("_onPlay");
            _onPause = serializedObject.FindProperty("_onPause");
            _onStop = serializedObject.FindProperty("_onStop");
        }

        private void OnDisable()
        {
            _waveformView?.Dispose();
            _waveformView = null;
        }

        // Keep the status, waveform and level meter live in Play mode.
        public override bool RequiresConstantRepaint() => EditorApplication.isPlaying || AsioEditorPreview.Source != null;

        public override void OnInspectorGUI()
        {
            EnsureStyles();

            AsioAudioSettings settings = AsioEditorUtility.Settings;
            AsioAudioSource[] allSources = AsioEditorUtility.FindAllSources();
            int outputChannelCount = AsioEditorUtility.GetKnownOutputChannelCount();

            // The audio file is set through the component's property (it reloads the file in Play mode), before the serialized object is read.
            DrawFileCard();

            serializedObject.Update();

            if (string.IsNullOrEmpty(settings.AsioDriverName))
            {
                EditorGUILayout.HelpBox("No ASIO driver is set for the project: this source won't play. Choose one in Project Settings > ASIO Audio.", MessageType.Warning);
                if (GUILayout.Button("Open Project Settings > ASIO Audio")) AsioEditorUtility.OpenSettings();
            }

            DrawPlaybackCard();
            DrawMixCard(allSources, outputChannelCount);
            DrawAdvancedCard(settings);
            DrawEventsCard();

            serializedObject.ApplyModifiedProperties();
        }

        // ---------- Cards ----------

        private static void BeginCard()
        {
            EditorGUILayout.BeginVertical(_cardStyle);
        }

        private static void EndCard()
        {
            EditorGUILayout.EndVertical();
        }

        private static void CardTitle(string title)
        {
            GUILayout.Label(title, _cardTitleStyle);
        }

        // ---------- Audio file ----------

        private void DrawFileCard()
        {
            AsioAudioSource[] sources = Sources;
            string firstPath = sources[0].AudioFilePath;
            bool samePath = sources.All(source => source.AudioFilePath == firstPath);
            bool hasFile = samePath && !string.IsNullOrEmpty(firstPath);
            bool fileExists = hasFile && File.Exists(sources[0].ResolvedAudioFilePath);
            AsioEditorUtility.AudioFileInfo info = fileExists ? AsioEditorUtility.GetAudioFileInfo(sources[0].ResolvedAudioFilePath) : null;

            BeginCard();
            CardTitle("Audio File");

            // Drop zone: icon, file name and format
            Rect dropArea = GUILayoutUtility.GetRect(0f, 48f, GUILayout.ExpandWidth(true));
            bool dragHover = HandleAudioFileDrop(dropArea);

            if (Event.current.type == EventType.Repaint)
            {
                Color background = EditorGUIUtility.isProSkin ? new Color(0.16f, 0.16f, 0.16f) : new Color(0.82f, 0.82f, 0.82f);
                Color border = dragHover ? AccentColor : EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.12f) : new Color(0f, 0f, 0f, 0.18f);
                EditorGUI.DrawRect(dropArea, background);
                DrawBorder(dropArea, border, dragHover ? 2f : 1f);

                Texture icon = EditorGUIUtility.IconContent("AudioClip Icon").image;
                if (icon != null) GUI.DrawTexture(new Rect(dropArea.x + 10f, dropArea.y + 8f, 32f, 32f), icon, ScaleMode.ScaleToFit);
            }

            Rect textArea = new Rect(dropArea.x + 52f, dropArea.y + 6f, dropArea.width - 60f, dropArea.height - 12f);
            string title = !samePath ? "—  (different files)" : hasFile ? Path.GetFileName(firstPath) : "No audio file";
            string hint = !samePath ? "Select a single source to see its file."
                : !hasFile ? "Drop an audio file here, or use Browse…"
                : !fileExists ? "File not found: " + firstPath
                : info != null ? AsioEditorUtility.DescribeAudioFile(info) : "Unreadable file";
            GUI.Label(new Rect(textArea.x, textArea.y, textArea.width, 18f), new GUIContent(title, samePath ? firstPath : null), _dropTitleStyle);
            GUI.Label(new Rect(textArea.x, textArea.y + 18f, textArea.width, 16f), hint, _dropHintStyle);

            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Browse…", EditorStyles.miniButtonLeft)) BrowseForAudioFile();
            using (new EditorGUI.DisabledScope(!fileExists))
            {
                if (GUILayout.Button(new GUIContent("Show in Explorer", "Open the folder containing the file."), EditorStyles.miniButtonMid)) EditorUtility.RevealInFinder(sources[0].ResolvedAudioFilePath);
            }
            using (new EditorGUI.DisabledScope(sources.All(source => string.IsNullOrEmpty(source.AudioFilePath))))
            {
                if (GUILayout.Button("Clear", EditorStyles.miniButtonRight)) SetAudioFilePath("");
            }
            EditorGUILayout.EndHorizontal();

            // File problems
            AsioAudioSource[] missingFile = sources.Where(source => !string.IsNullOrEmpty(source.AudioFilePath) && !File.Exists(source.ResolvedAudioFilePath)).ToArray();
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

            EndCard();
        }

        // Returns true while a valid audio file is dragged over the area.
        private bool HandleAudioFileDrop(Rect dropArea)
        {
            Event currentEvent = Event.current;
            if (currentEvent.type != EventType.DragUpdated && currentEvent.type != EventType.DragPerform) return false;
            if (!dropArea.Contains(currentEvent.mousePosition) || DragAndDrop.paths.Length == 0) return false;

            string filePath = DragAndDrop.paths[0];
            if (!AsioEditorUtility.IsAudioFile(filePath))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                return false;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            // Only assign the file when it is actually dropped on the area (not when the drag merely passes over it).
            if (currentEvent.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                SetAudioFilePath(AsioEditorUtility.ToProjectRelativePath(filePath));
            }

            currentEvent.Use();
            return true;
        }

        private void BrowseForAudioFile()
        {
            string currentPath = Sources[0].ResolvedAudioFilePath;
            string startDirectory = !string.IsNullOrEmpty(currentPath) && Directory.Exists(Path.GetDirectoryName(currentPath)) ? Path.GetDirectoryName(currentPath) : Application.dataPath;

            string selectedPath = EditorUtility.OpenFilePanelWithFilters("Select an audio file", startDirectory, new[] { "Audio files", "wav,mp3,aiff,aif,flac", "All files", "*" });
            if (!string.IsNullOrEmpty(selectedPath)) SetAudioFilePath(AsioEditorUtility.ToProjectRelativePath(selectedPath));

            GUIUtility.ExitGUI(); // The file dialog breaks the current GUI layout
        }

        private void SetAudioFilePath(string audioFilePath)
        {
            foreach (AsioAudioSource source in Sources)
            {
                AsioEditorUtility.RecordChange(source, "Set Audio File", () => source.AudioFilePath = audioFilePath);
            }
        }

        // ---------- Playback ----------

        private void DrawPlaybackCard()
        {
            AsioAudioSource[] sources = Sources;
            bool isPlaying = EditorApplication.isPlaying;

            BeginCard();

            // Title, with the status and time on the right
            EditorGUILayout.BeginHorizontal();
            CardTitle("Playback");
            GUILayout.FlexibleSpace();
            bool previewing = sources.Length == 1 && AsioEditorPreview.IsPreviewing(sources[0]);
            if (sources.Length == 1)
            {
                AsioAudioSource source = sources[0];
                double length = GetLength(source);
                double time = isPlaying ? source.CurrentTimestamp : previewing ? AsioEditorPreview.CurrentTime : 0;
                GUILayout.Label(AsioEditorUtility.FormatTime(time) + " / " + AsioEditorUtility.FormatTime(length), _rightMiniLabelStyle);
                if (isPlaying) DrawStatusPill(source.AudioStatus.ToString(), AsioEditorUtility.GetStatusColor(source.AudioStatus));
                else DrawStatusPill(previewing ? "Preview" : "Edit mode", previewing ? AsioEditorUtility.PlayingColor : AsioEditorUtility.StoppedColor);
            }
            EditorGUILayout.EndHorizontal();

            // Waveform (or a simple progress bar if the file can't be read)
            if (sources.Length == 1)
            {
                AsioAudioSource source = sources[0];
                Color statusColor = previewing ? AsioEditorUtility.PlayingColor : AsioEditorUtility.GetStatusColor(source.AudioStatus);
                double currentSeconds = isPlaying ? source.CurrentTimestamp : previewing ? AsioEditorPreview.CurrentTime : 0;
                bool live = isPlaying || previewing;
                WaveformData waveform = WaveformData.Get(source.ResolvedAudioFilePath);
                double seekTo;

                if (waveform == null)
                {
                    Rect barRect = GUILayoutUtility.GetRect(0f, 16f, GUILayout.ExpandWidth(true));
                    seekTo = AsioEditorUtility.DrawProgressBar(barRect, currentSeconds, GetLength(source), statusColor, live);
                }
                else
                {
                    _waveformView ??= new AsioWaveformView();
                    bool isAdvancing = previewing || (isPlaying && source.AudioStatus == AsioAudioStatus.Playing);

                    // Fades drawn on the waveform, editable with their handles
                    _waveformView.ShowFades = true;
                    _waveformView.FadeInSeconds = _fadeInDuration.floatValue;
                    _waveformView.FadeOutSeconds = _fadeOutDuration.floatValue;
                    _waveformView.FadeOutAtEnd = !_loop.boolValue;
                    _waveformView.FadeInCurve = (AsioFadeCurve)_fadeInCurve.intValue;
                    _waveformView.FadeOutCurve = (AsioFadeCurve)_fadeOutCurve.intValue;
                    _waveformView.LoopCrossfadeSeconds = _loop.boolValue ? _loopCrossfade.floatValue : 0f;

                    seekTo = _waveformView.Draw(waveform, currentSeconds, live, isAdvancing, live, live ? statusColor : AsioEditorUtility.StoppedColor);
                    if (_waveformView.FadesChanged)
                    {
                        _fadeInDuration.floatValue = _waveformView.FadeInSeconds;
                        _fadeOutDuration.floatValue = _waveformView.FadeOutSeconds;
                        _fadeInCurve.intValue = (int)_waveformView.FadeInCurve;
                        _fadeOutCurve.intValue = (int)_waveformView.FadeOutCurve;
                        if (_loop.boolValue) _loopCrossfade.floatValue = _waveformView.LoopCrossfadeSeconds;
                    }
                    if (_waveformView.NeedsRepaint && !isPlaying) Repaint();
                }

                if (seekTo >= 0)
                {
                    if (isPlaying) source.Seek(seekTo);
                    else if (previewing) AsioEditorPreview.Seek(seekTo);
                }
                EditorGUILayout.Space(4f);
            }

            if (isPlaying)
            {
                // Transport
                AsioAudioStatus status = sources.Length == 1 ? sources[0].AudioStatus : AsioAudioStatus.Stopped;
                EditorGUILayout.BeginHorizontal();
                if (TransportButton("Play", Glyph.Play, status == AsioAudioStatus.Playing, AsioEditorUtility.PlayingColor, "Start or resume playback")) foreach (AsioAudioSource source in sources) source.Play();
                if (TransportButton("Pause", Glyph.Pause, status == AsioAudioStatus.Paused, AsioEditorUtility.PausedColor, "Pause, keeping the position")) foreach (AsioAudioSource source in sources) source.Pause();
                if (TransportButton("Stop", Glyph.Stop, false, Color.white, "Stop and go back to the start")) foreach (AsioAudioSource source in sources) source.Stop();
                if (TransportButton("Restart", Glyph.Restart, false, Color.white, "Play again from the start")) foreach (AsioAudioSource source in sources) source.Restart();
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                // Preview in Edit mode
                using (new EditorGUI.DisabledScope(sources.Length != 1))
                {
                    EditorGUILayout.BeginHorizontal();
                    if (TransportButton("Preview", Glyph.Play, previewing, AsioEditorUtility.PlayingColor, "Listen to this source without entering Play mode, on its output channel, at its volume and the master volume."))
                    {
                        if (!previewing) AsioEditorPreview.Start(sources[0]);
                    }
                    using (new EditorGUI.DisabledScope(!previewing))
                    {
                        if (TransportButton("Stop", Glyph.Stop, false, Color.white, "Stop the preview")) AsioEditorPreview.Stop();
                    }
                    EditorGUILayout.EndHorizontal();
                }

                if (sources.Length == 1 && AsioEditorPreview.LastError != null && !AsioEditorPreview.IsPreviewing(sources[0]))
                {
                    EditorGUILayout.HelpBox(AsioEditorPreview.LastError, MessageType.Warning);
                }
            }

            // Playback options
            EditorGUILayout.Space(2f);
            EditorGUILayout.BeginHorizontal();
            DrawToggleButton(_loop, new GUIContent("Loop", "Restart seamlessly at the end of the file. Can be changed while playing."), AccentColor, EditorStyles.miniButtonLeft);
            DrawToggleButton(_playOnEnable, new GUIContent("Play On Enable", "Start playing when the component is enabled (like Play On Awake)."), AccentColor, EditorStyles.miniButtonRight);
            EditorGUILayout.EndHorizontal();

            // Smooth loop
            using (new EditorGUI.DisabledScope(!_loop.boolValue))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(_loopCrossfade, new GUIContent("Loop Crossfade", "Smooth loop: the end of the file is crossfaded with its start over this many seconds (at most half the file), "
                    + "so the loop point can't be heard; each loop then continues from this time. 0: the file restarts directly. Only when Loop is on. Also editable with the handle at the bottom of the waveform."));
                GUILayout.Label("s", EditorStyles.miniLabel, GUILayout.Width(10f));
                GUILayout.Space(134f);
                EditorGUILayout.EndHorizontal();
            }

            // Fades: duration and curve
            const string curveTooltip = "Shape of the fade: Squared (default, gentle near silence), Linear, Inverted Squared (fast near silence), or S-Curve (gentle at both ends). "
                + "Also editable with a right-click on the fade, on the waveform.";
            DrawFadeRow(_fadeInDuration, _fadeInCurve, new GUIContent("Fade In", "Fade-in duration of Play (and of resuming), in seconds. 0: a few milliseconds, just enough to avoid clicks. "
                + "Also editable with the handle on the waveform."), curveTooltip);
            DrawFadeRow(_fadeOutDuration, _fadeOutCurve, new GUIContent("Fade Out", "Fade-out duration of Pause and Stop, and of the end of a non-looping file, in seconds. 0: a few milliseconds, just enough to avoid clicks. "
                + "Also editable with the handle on the waveform."), curveTooltip);

            EndCard();
        }

        private static double GetLength(AsioAudioSource source)
        {
            double length = source.AudioFileTotalLength;
            if (!EditorApplication.isPlaying || length <= 0)
            {
                AsioEditorUtility.AudioFileInfo info = AsioEditorUtility.GetAudioFileInfo(source.ResolvedAudioFilePath);
                if (info != null) length = info.LengthSeconds;
            }
            return length;
        }

        private static void DrawStatusPill(string text, Color color)
        {
            GUIContent content = new GUIContent(text);
            Vector2 size = _pillStyle.CalcSize(content);
            Rect rect = GUILayoutUtility.GetRect(size.x + 4f, 18f, GUILayout.ExpandWidth(false));
            rect.y += 1f;
            rect.height = 16f;
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, new Color(color.r, color.g, color.b, 0.22f));
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), color);
            }
            Color previous = _pillStyle.normal.textColor;
            _pillStyle.normal.textColor = color;
            GUI.Label(rect, content, _pillStyle);
            _pillStyle.normal.textColor = previous;
        }

        private static bool TransportButton(string text, Glyph glyph, bool active, Color activeColor, string tooltip)
        {
            Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.button, GUILayout.Height(26f), GUILayout.ExpandWidth(true));

            Color previousBackground = GUI.backgroundColor;
            if (active) GUI.backgroundColor = activeColor;
            bool clicked = GUI.Button(rect, new GUIContent("", tooltip));
            GUI.backgroundColor = previousBackground;

            if (Event.current.type == EventType.Repaint)
            {
                float textWidth = _transportLabelStyle.CalcSize(new GUIContent(text)).x;
                bool showText = rect.width >= textWidth + 30f;
                float total = showText ? 10f + 6f + textWidth : 10f;
                float start = Mathf.Round(rect.center.x - total / 2f);

                Color glyphColor = active ? activeColor : EditorStyles.label.normal.textColor;
                if (!GUI.enabled) glyphColor.a *= 0.4f;
                DrawGlyph(new Rect(start, Mathf.Round(rect.center.y - 5f), 10f, 10f), glyph, glyphColor);
                if (showText) GUI.Label(new Rect(start + 16f, rect.y, textWidth + 2f, rect.height), text, _transportLabelStyle);
            }
            return clicked;
        }

        private static void DrawGlyph(Rect rect, Glyph glyph, Color color)
        {
            Color previous = Handles.color;
            Handles.color = color;
            switch (glyph)
            {
                case Glyph.Play:
                    Handles.DrawAAConvexPolygon(new Vector3(rect.x + 1f, rect.y), new Vector3(rect.xMax, rect.center.y), new Vector3(rect.x + 1f, rect.yMax));
                    break;
                case Glyph.Pause:
                    EditorGUI.DrawRect(new Rect(rect.x + 1f, rect.y, 3f, rect.height), color);
                    EditorGUI.DrawRect(new Rect(rect.xMax - 4f, rect.y, 3f, rect.height), color);
                    break;
                case Glyph.Stop:
                    EditorGUI.DrawRect(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f), color);
                    break;
                case Glyph.Restart:
                    EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), color);
                    Handles.DrawAAConvexPolygon(new Vector3(rect.xMax, rect.y), new Vector3(rect.xMax, rect.yMax), new Vector3(rect.x + 2f, rect.center.y));
                    break;
            }
            Handles.color = previous;
        }

        // ---------- Mix and output ----------

        private void DrawMixCard(AsioAudioSource[] allSources, int outputChannelCount)
        {
            AsioAudioSource[] sources = Sources;
            bool isPlaying = EditorApplication.isPlaying;

            BeginCard();
            CardTitle("Mix & Output");

            // Volume in dB, with the level meter under the slider in Play mode
            float meterLevel = sources.Length != 1 ? -1f
                : isPlaying ? sources[0].MeterLevel
                : AsioEditorPreview.IsPreviewing(sources[0]) ? AsioEditorPreview.MeterLevel
                : -1f;
            EditorGUI.BeginChangeCheck();
            float volume = AsioVolumeFader.SliderRow(new GUIContent("Volume", "In decibels: 0 dB is the file's original level, -6 dB half its amplitude, about -10 dB half as loud.\nDrag the slider (double-click: 0 dB) or type a value."),
                _volume.floatValue, meterLevel, _volume.hasMultipleDifferentValues);
            if (EditorGUI.EndChangeCheck()) _volume.floatValue = volume;

            // Mute / Solo
            EditorGUILayout.Space(2f);
            EditorGUILayout.BeginHorizontal();
            DrawToggleButton(_mute, new GUIContent("Mute", "Silence this source without stopping it."), AsioEditorUtility.ErrorColor, GUI.skin.button, GUILayout.Height(22f));
            DrawToggleButton(_solo, new GUIContent("Solo", "When any source is soloed, only soloed sources are heard."), AsioEditorUtility.PausedColor, GUI.skin.button, GUILayout.Height(22f));
            EditorGUILayout.EndHorizontal();

            // Output channel
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Output Channel", "The ASIO output this source plays on. Each source needs its own channel."), EditorStyles.label);
            GUILayout.FlexibleSpace();
            string driverText = outputChannelCount > 0 ? outputChannelCount + " outputs on the driver" : "Driver outputs unknown until Play mode";
            GUILayout.Label(driverText, _rightMiniLabelStyle);
            EditorGUILayout.EndHorizontal();

            if (sources.Length == 1)
            {
                DrawChannelGrid(sources[0], allSources, outputChannelCount);

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
                EditorGUILayout.LabelField(new GUIContent(channels, "Each source needs its own channel: select a single source to choose its channel."), EditorStyles.miniLabel);
                if (GUILayout.Button(new GUIContent("Auto-assign", "Give each selected source the lowest free channel."), EditorStyles.miniButton, GUILayout.Width(80f)))
                {
                    AsioEditorUtility.AutoAssignChannels(sources, allSources);
                    serializedObject.Update();
                }
                EditorGUILayout.EndHorizontal();
            }

            EndCard();
        }

        // A grid of channel buttons: the selected one highlighted, the ones used by other sources marked.
        private void DrawChannelGrid(AsioAudioSource source, AsioAudioSource[] allSources, int outputChannelCount)
        {
            int current = _targetOutputChannel.intValue;
            int highestUsed = allSources.Length == 0 ? 0 : allSources.Max(other => other.TargetOutputChannel);
            int channelCount = outputChannelCount > 0 ? outputChannelCount : Mathf.Max(16, highestUsed);
            int shown = Mathf.Max(channelCount, current);

            const float cellWidth = 32f, cellHeight = 20f, gap = 2f;
            float availableWidth = EditorGUIUtility.currentViewWidth - 48f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((availableWidth + gap) / (cellWidth + gap)));
            int cellCount = shown + 1; // "None", then each channel
            int rows = Mathf.CeilToInt(cellCount / (float)perRow);

            Rect area = GUILayoutUtility.GetRect(0f, rows * (cellHeight + gap), GUILayout.ExpandWidth(true));
            Color previousBackground = GUI.backgroundColor;

            for (int cell = 0; cell < cellCount; cell++)
            {
                int channel = cell; // cell 0 is "None"
                Rect cellRect = new Rect(area.x + cell % perRow * (cellWidth + gap), area.y + cell / perRow * (cellHeight + gap), cellWidth, cellHeight);

                AsioAudioSource user = channel > 0 ? allSources.FirstOrDefault(other => other != source && other.TargetOutputChannel == channel) : null;
                bool selected = channel == current && !_targetOutputChannel.hasMultipleDifferentValues;
                bool outOfRange = outputChannelCount > 0 && channel > outputChannelCount;

                string channelName = AsioEditorUtility.GetKnownOutputChannelName(channel);
                string tooltip = channel == 0 ? "No channel (the source won't play)"
                    : "Channel " + channel + (string.IsNullOrEmpty(channelName) ? "" : " (" + channelName + ")")
                      + (outOfRange ? " — doesn't exist on the driver" : "") + (user != null ? " — used by \"" + user.name + "\"" : "");

                GUI.backgroundColor = selected ? (user != null || outOfRange ? AsioEditorUtility.ErrorColor : AccentColor)
                    : user != null ? new Color(1f, 1f, 1f, 0.45f)
                    : previousBackground;

                GUIContent content = new GUIContent(channel == 0 ? "–" : channel.ToString(), tooltip);
                if (GUI.Toggle(cellRect, selected, content, EditorStyles.miniButton) && !selected)
                {
                    _targetOutputChannel.intValue = channel;
                }

                // Small dot on channels used by another source
                if (user != null && Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(new Rect(cellRect.xMax - 6f, cellRect.y + 3f, 3f, 3f), AsioEditorUtility.PausedColor);
                }
            }

            GUI.backgroundColor = previousBackground;

            // The selected channel, with the driver's name for it
            if (current > 0 && !_targetOutputChannel.hasMultipleDifferentValues)
            {
                string currentName = AsioEditorUtility.GetKnownOutputChannelName(current);
                EditorGUILayout.LabelField("Output " + current + (string.IsNullOrEmpty(currentName) ? "" : "  ·  " + currentName), EditorStyles.miniLabel);
            }
        }

        // ---------- Advanced ----------

        private void DrawAdvancedCard(AsioAudioSettings settings)
        {
            BeginCard();
            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "Advanced", true, _foldoutStyle);
            if (_showAdvanced)
            {
                EditorGUILayout.PropertyField(_preloadIntoMemory, new GUIContent("Preload Into Memory",
                    "Decode the whole file into memory when loading, instead of reading it from disk while playing. Avoids dropouts with many sources or slow disks, but uses about 10 MB per minute of audio and takes longer to load."));

                AsioAudioSource[] sources = Sources;
                if (sources.Length == 1)
                {
                    AsioEditorUtility.AudioFileInfo info = AsioEditorUtility.GetAudioFileInfo(sources[0].ResolvedAudioFilePath);
                    if (info != null)
                    {
                        EditorGUILayout.Space(2f);
                        string channels = info.Channels == 1 ? "Mono" : info.Channels == 2 ? "Stereo" : info.Channels + " channels";
                        string bits = info.BitsPerSample > 0 ? info.BitsPerSample + "-bit, " : "";
                        EditorGUILayout.LabelField("File Format", bits + (info.SampleRate / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + " kHz, " + channels, EditorStyles.miniLabel);

                        string output = info.Channels > 1 ? "Mixed down to mono" : "Mono";
                        if (info.SampleRate != settings.TargetSampleRate) output += ", resampled to " + (settings.TargetSampleRate / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + " kHz";
                        EditorGUILayout.LabelField("Played As", output, EditorStyles.miniLabel);
                    }
                }
            }
            EndCard();
        }

        // ---------- Events ----------

        private void DrawEventsCard()
        {
            BeginCard();
            _showEvents = EditorGUILayout.Foldout(_showEvents, "Events", true, _foldoutStyle);
            if (_showEvents)
            {
                EditorGUILayout.PropertyField(_onPlay);
                EditorGUILayout.PropertyField(_onPause);
                EditorGUILayout.PropertyField(_onStop);
            }
            EndCard();
        }

        // ---------- Helpers ----------

        private static void DrawToggleButton(SerializedProperty property, GUIContent content, Color activeColor, GUIStyle style, params GUILayoutOption[] options)
        {
            Color previousColor = GUI.backgroundColor;
            if (property.boolValue && !property.hasMultipleDifferentValues) GUI.backgroundColor = activeColor;

            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            bool value = GUILayout.Toggle(property.boolValue, content, style, options);
            if (EditorGUI.EndChangeCheck()) property.boolValue = value;
            EditorGUI.showMixedValue = false;

            GUI.backgroundColor = previousColor;
        }

        private static void DrawBorder(Rect rect, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private static void EnsureStyles()
        {
            if (_cardStyle != null) return;
            _cardStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(10, 10, 8, 10), margin = new RectOffset(0, 0, 6, 0) };
            _cardTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, margin = new RectOffset(0, 0, 0, 4) };
            _pillStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(8, 6, 0, 0) };
            _dropTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, clipping = TextClipping.Clip };
            _dropHintStyle = new GUIStyle(EditorStyles.miniLabel) { clipping = TextClipping.Clip };
            _rightMiniLabelStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
            _transportLabelStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(0, 0, 0, 0) };
            _foldoutStyle = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold, fontSize = 12 };
        }

        private static void DrawFadeRow(SerializedProperty duration, SerializedProperty curve, GUIContent label, string curveTooltip)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(duration, label);
            GUILayout.Label("s", EditorStyles.miniLabel, GUILayout.Width(10f));
            EditorGUILayout.PropertyField(curve, new GUIContent(string.Empty, curveTooltip), GUILayout.Width(130f));
            EditorGUILayout.EndHorizontal();
        }
    }
}
