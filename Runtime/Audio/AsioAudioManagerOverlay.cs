using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.Wave;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// The in-game overlay of the ASIO Audio Manager: a draggable window showing the ASIO driver status and settings,
    /// and the controls of every registered ASIO Audio Source (transport, mute / solo, volume, level meter, position).
    /// Drawn with IMGUI, so it works with any input system and without any scene setup.
    /// </summary>
    internal sealed class AsioAudioManagerOverlay
    {
        private const int WindowId = 0x4153494F; // "ASIO"
        private const float WindowWidth = 480f;
        private const float Margin = 10f;
        private const float SourceRowHeight = 76f; // Height of one source in the list (3 lines + padding)

        private static readonly int[] CommonSampleRates = { 44100, 48000, 88200, 96000 };
        private static readonly string[] SampleFormatLabels = { "16-bit integer", "32-bit float" };

        private static readonly Color PlayingColor = new Color(0.40f, 0.85f, 0.50f);
        private static readonly Color PausedColor = new Color(0.95f, 0.78f, 0.30f);
        private static readonly Color StoppedColor = new Color(0.65f, 0.65f, 0.65f);
        private static readonly Color ErrorColor = new Color(0.95f, 0.40f, 0.40f);
        private static readonly Color TrackColor = new Color(0f, 0f, 0f, 0.45f);

        private readonly AsioAudioManager _manager;
        private Rect _windowRect = new Rect(Margin, Margin, WindowWidth, 0f);
        private readonly List<AsioAudioSource> _sortedSources = new List<AsioAudioSource>();
        private Vector2 _sourcesScrollPosition;
        private bool _showSettings;
        private bool _collapsed;
        private string[] _driverNames;

        // GUIStyles can only be created during OnGUI: they are built on first draw, then reused.
        private GUIStyle _titleStyle;
        private GUIStyle _smallStyle;
        private GUIStyle _boldStyle;
        private GUIStyle _channelStyle;
        private GUIStyle _statusStyle;
        private GUIStyle _rowStyle;
        private GUIStyle _toggleButtonStyle;

        public AsioAudioManagerOverlay(AsioAudioManager manager)
        {
            _manager = manager;
        }

        public void Draw()
        {
            EnsureStyles();

            float scale = Mathf.Clamp(_manager.OverlayScale, 0.5f, 3f);
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            float screenWidth = Screen.width / scale;
            float screenHeight = Screen.height / scale;

            _windowRect.width = Mathf.Min(WindowWidth, screenWidth - 2f * Margin);
            _windowRect.height = 0f; // GUILayout.Window only grows from the given size: let it fit its content again (e.g. after collapsing)
            _windowRect =GUILayout.Window(WindowId, _windowRect, DrawWindow, "ASIO Audio", GUILayout.MaxHeight(screenHeight - 2f * Margin));

            // Keep the window on screen (after dragging, or when the screen is resized).
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, Mathf.Max(0f, screenWidth - _windowRect.width));
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Mathf.Max(0f, screenHeight - _windowRect.height));

            GUI.matrix = previousMatrix;
        }

        private void DrawWindow(int windowId)
        {
            DrawHeader();

            if (!_collapsed)
            {
                if (_showSettings) DrawSettings();
                DrawSources();

                if (_manager.OverlayToggleKey != KeyCode.None)
                {
                    GUILayout.Label("Press " + _manager.OverlayToggleKey + " to show / hide this window.", _smallStyle);
                }
            }

            // Drag the window by its title bar.
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }

        // ---------- Header: connection status ----------

        private void DrawHeader()
        {
            bool connected = _manager.IsConnected;

            GUILayout.BeginHorizontal();
            _statusStyle.normal.textColor = connected ? PlayingColor : ErrorColor;
            GUILayout.Label(connected ? "● Connected" : "● Not connected", _statusStyle);
            GUILayout.FlexibleSpace();

            if (!_collapsed && GUILayout.Toggle(_showSettings, "Settings", _toggleButtonStyle) != _showSettings) _showSettings = !_showSettings;
            if (_manager.isActiveAndEnabled && GUILayout.Button("Reconnect")) _manager.Reconnect();
            if (GUILayout.Button(new GUIContent(_collapsed ? "+" : "–", _collapsed ? "Expand" : "Collapse"), GUILayout.Width(24f))) _collapsed = !_collapsed;
            if (GUILayout.Button("×", GUILayout.Width(24f))) _manager.DisplayInfoOnGameWindow = false;
            GUILayout.EndHorizontal();

            string driverName = string.IsNullOrEmpty(_manager.AsioDriverName) ? "No driver set" : _manager.AsioDriverName;
            string details = driverName + "  ·  " + _manager.TargetSampleRate + " Hz  ·  " + SampleFormatLabels[_manager.TargetBitsPerSample == BitsPerSample.Bits16 ? 0 : 1];
            if (connected) details += "  ·  " + _manager.AsioDriverOutputChannelCount + " outputs";
            if (connected && !string.IsNullOrEmpty(_manager.AsioDriverSampleFormat)) details += "\nDriver format: " + _manager.AsioDriverSampleFormat;
            GUILayout.Label(details, _smallStyle);

            if (!_collapsed) DrawMaster();
        }

        // Master volume (fader law of a mixing desk, shown in dB) and the level sent to the driver
        private void DrawMaster()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Master", _boldStyle, GUILayout.Width(52f));

            GUILayout.BeginVertical();
            GUILayout.Space(6f);
            float faderPosition = AsioVolume.LinearToFaderPosition(_manager.MasterVolume);
            float newFaderPosition = GUILayout.HorizontalSlider(faderPosition, 0f, 1f, GUILayout.MinWidth(60f));
            if (!Mathf.Approximately(newFaderPosition, faderPosition)) _manager.MasterVolume = AsioVolume.FaderPositionToLinear(newFaderPosition);
            GUILayout.EndVertical();

            GUILayout.Label(AsioVolume.FormatLinear(_manager.MasterVolume), _smallStyle, GUILayout.Width(46f));

            GUILayout.BeginVertical(GUILayout.Width(40f));
            GUILayout.Space(8f);
            DrawMeter(GUILayoutUtility.GetRect(40f, 6f), _manager.MasterMeterLevel);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        // ---------- Settings: driver, sample rate, format ----------

        private void DrawSettings()
        {
            GUILayout.BeginVertical(_rowStyle);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Driver", _boldStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Refresh list")) _driverNames = null;
            GUILayout.EndHorizontal();

            if (_driverNames == null)
            {
                try { _driverNames = AsioOut.GetDriverNames(); }
                catch (Exception) { _driverNames = new string[0]; }
            }

            if (_driverNames.Length == 0) GUILayout.Label("No ASIO driver is installed on this machine.", _smallStyle);
            foreach (string driverName in _driverNames)
            {
                bool selected = driverName == _manager.AsioDriverName;
                if (GUILayout.Toggle(selected, driverName, _toggleButtonStyle) && !selected) _manager.AsioDriverName = driverName;
            }

            GUILayout.Space(4f);
            GUILayout.Label("Sample rate (must be supported by the driver)", _boldStyle);
            int[] rates = CommonSampleRates.Contains(_manager.TargetSampleRate)
                ? CommonSampleRates
                : CommonSampleRates.Append(_manager.TargetSampleRate).OrderBy(rate => rate).ToArray();
            int currentRateIndex = Array.IndexOf(rates, _manager.TargetSampleRate);
            int selectedRateIndex = GUILayout.SelectionGrid(currentRateIndex, rates.Select(rate => rate + " Hz").ToArray(), rates.Length);
            if (selectedRateIndex != currentRateIndex) _manager.TargetSampleRate = rates[selectedRateIndex];

            GUILayout.Space(4f);
            GUILayout.Label("Sample format", _boldStyle);
            int currentFormat = _manager.TargetBitsPerSample == BitsPerSample.Bits16 ? 0 : 1;
            int selectedFormat = GUILayout.Toolbar(currentFormat, SampleFormatLabels);
            if (selectedFormat != currentFormat) _manager.TargetBitsPerSample = selectedFormat == 0 ? BitsPerSample.Bits16 : BitsPerSample.Bits32;

            GUILayout.EndVertical();
        }

        // ---------- Sources ----------

        private void DrawSources()
        {
            int sourceCount = _manager.AsioAudioSources.Count;
            GUILayout.Label("Sources (" + sourceCount + ")", _boldStyle);

            if (sourceCount == 0)
            {
                GUILayout.Label("No ASIO Audio Source is registered.", _smallStyle);
                return;
            }

            // A scroll view inside an auto-sized window needs an explicit height: fit all sources, up to the space left on screen.
            float maxListHeight = Mathf.Max(SourceRowHeight, Screen.height / Mathf.Clamp(_manager.OverlayScale, 0.5f, 3f) - 2f * Margin - (_showSettings ? 355f + 22f * (_driverNames?.Length ?? 0) : 150f));
            float listHeight = Mathf.Min(sourceCount * SourceRowHeight, maxListHeight);
            _sourcesScrollPosition = GUILayout.BeginScrollView(_sourcesScrollPosition, GUILayout.Height(listHeight));

            // Listed by output channel. Drawn from a copy: a source could unregister itself from a button's callback (e.g. OnStop listeners).
            _sortedSources.Clear();
            foreach (AsioAudioSource source in _manager.AsioAudioSources)
            {
                if (source != null) _sortedSources.Add(source);
            }
            _sortedSources.Sort((a, b) => a.TargetOutputChannel.CompareTo(b.TargetOutputChannel));
            foreach (AsioAudioSource source in _sortedSources)
            {
                if (source != null) DrawSource(source);
            }

            GUILayout.EndScrollView();
        }

        private void DrawSource(AsioAudioSource source)
        {
            GUILayout.BeginVertical(_rowStyle);

            // Line 1: channel, name, status, time
            GUILayout.BeginHorizontal();
            GUILayout.Label(source.TargetOutputChannel.ToString(), _channelStyle, GUILayout.Width(26f));
            GUILayout.Label(source.gameObject.name, _boldStyle, GUILayout.MinWidth(60f));
            GUILayout.FlexibleSpace();
            _statusStyle.normal.textColor = GetStatusColor(source.AudioStatus);
            GUILayout.Label(source.AudioStatus.ToString(), _statusStyle);
            GUILayout.Label(FormatTime(source.CurrentTimestamp) + " / " + FormatTime(source.AudioFileTotalLength), _smallStyle, GUILayout.Width(80f));
            GUILayout.EndHorizontal();

            // Line 2: position (click or drag to seek)
            Rect progressRect = GUILayoutUtility.GetRect(1f, 6f, GUILayout.ExpandWidth(true));
            float progress = source.AudioFileTotalLength > 0 ? Mathf.Clamp01((float)(source.CurrentTimestamp / source.AudioFileTotalLength)) : 0f;
            DrawRect(progressRect, TrackColor);
            DrawRect(new Rect(progressRect.x, progressRect.y, progressRect.width * progress, progressRect.height), GetStatusColor(source.AudioStatus));
            HandleSeek(source, progressRect);

            // Line 3: transport, mute / solo, volume, meter
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Play", GUILayout.Width(46f))) source.Play();
            if (GUILayout.Button("Pause", GUILayout.Width(50f))) source.Pause();
            if (GUILayout.Button("Stop", GUILayout.Width(46f))) source.Stop();
            GUILayout.Space(6f);
            source.Mute = DrawColoredToggle(source.Mute, "M", ErrorColor);
            source.Solo = DrawColoredToggle(source.Solo, "S", PausedColor);
            GUILayout.Space(6f);

            GUILayout.BeginVertical();
            GUILayout.Space(6f);
            // Fader law of a mixing desk (half travel is -20 dB), shown in dB
            float faderPosition = AsioVolume.LinearToFaderPosition(source.Volume);
            float newFaderPosition = GUILayout.HorizontalSlider(faderPosition, 0f, 1f, GUILayout.MinWidth(60f));
            if (!Mathf.Approximately(newFaderPosition, faderPosition)) source.Volume = AsioVolume.FaderPositionToLinear(newFaderPosition);
            GUILayout.EndVertical();

            GUILayout.Label(AsioVolume.FormatLinear(source.Volume), _smallStyle, GUILayout.Width(46f));

            GUILayout.BeginVertical(GUILayout.Width(40f));
            GUILayout.Space(8f);
            Rect meterRect = GUILayoutUtility.GetRect(40f, 6f);
            DrawMeter(meterRect, source.MeterLevel);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private static void HandleSeek(AsioAudioSource source, Rect rect)
        {
            Event currentEvent = Event.current;
            if (source.AudioFileTotalLength <= 0) return;
            if (currentEvent.type != EventType.MouseDown && currentEvent.type != EventType.MouseDrag) return;
            if (currentEvent.button != 0 || !rect.Contains(currentEvent.mousePosition)) return;

            source.Seek(Mathf.Clamp01((currentEvent.mousePosition.x - rect.x) / rect.width) * source.AudioFileTotalLength);
            currentEvent.Use();
        }

        private bool DrawColoredToggle(bool value, string label, Color activeColor)
        {
            Color previousBackground = GUI.backgroundColor;
            if (value) GUI.backgroundColor = activeColor;
            bool newValue = GUILayout.Toggle(value, label, _toggleButtonStyle, GUILayout.Width(26f));
            GUI.backgroundColor = previousBackground;
            return newValue;
        }

        // ---------- Drawing helpers ----------

        private static void DrawRect(Rect rect, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private static void DrawMeter(Rect rect, float linearLevel)
        {
            DrawRect(rect, TrackColor);
            float decibels = linearLevel > 0.000001f ? 20f * Mathf.Log10(linearLevel) : -120f;
            float fill = Mathf.Clamp01(Mathf.InverseLerp(-60f, 0f, decibels));
            if (fill <= 0f) return;
            Color color = decibels > -3f ? ErrorColor : decibels > -12f ? PausedColor : PlayingColor;
            DrawRect(new Rect(rect.x, rect.y, rect.width * fill, rect.height), color);
        }

        private static Color GetStatusColor(AsioAudioStatus status)
        {
            switch (status)
            {
                case AsioAudioStatus.Playing: return PlayingColor;
                case AsioAudioStatus.Paused: return PausedColor;
                default: return StoppedColor;
            }
        }

        private static string FormatTime(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
            TimeSpan time = TimeSpan.FromSeconds(seconds);
            return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null) return;

            _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            _boldStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, clipping = TextClipping.Clip, wordWrap = false };
            _smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = false, clipping = TextClipping.Clip };
            _smallStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
            _channelStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            _statusStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            _rowStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(6, 6, 4, 4), margin = new RectOffset(0, 0, 2, 2) };
            _toggleButtonStyle = new GUIStyle(GUI.skin.button);
        }
    }
}
