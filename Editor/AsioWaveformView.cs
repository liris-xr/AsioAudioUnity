using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using NAudio.Wave;
using UnityEditor;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// Peak and RMS levels of an audio file, mixed down to mono like it is played, computed in a background thread and cached.
    /// </summary>
    internal sealed class WaveformData
    {
        public const int FramesPerBucket = 256;
        private const int MaxCachedFiles = 12;

        private static readonly Dictionary<string, WaveformData> Cache = new Dictionary<string, WaveformData>();
        private static readonly List<string> CacheOrder = new List<string>(); // least recently used first

        public readonly string FilePath;
        public readonly DateTime LastWrite;

        // Written by the analysis thread; read only once IsReady is true.
        public float[] Min;
        public float[] Max;
        public float[] MeanSquare;
        public int BucketCount;
        public int SampleRate;
        public long TotalFrames;

        public volatile bool IsReady;
        public volatile float Progress;
        public volatile string Error;
        private volatile bool _cancelled;

        public double LengthSeconds => SampleRate > 0 ? (double)TotalFrames / SampleRate : 0;

        private WaveformData(string filePath, DateTime lastWrite)
        {
            FilePath = filePath;
            LastWrite = lastWrite;
        }

        /// <summary>The waveform of a file (analysis started on first request), or null if the file doesn't exist.</summary>
        public static WaveformData Get(string resolvedPath)
        {
            if (string.IsNullOrEmpty(resolvedPath) || !File.Exists(resolvedPath)) return null;

            DateTime lastWrite = File.GetLastWriteTimeUtc(resolvedPath);
            if (Cache.TryGetValue(resolvedPath, out WaveformData cached) && cached.LastWrite == lastWrite)
            {
                CacheOrder.Remove(resolvedPath);
                CacheOrder.Add(resolvedPath);
                return cached;
            }

            if (cached != null) cached._cancelled = true;
            WaveformData data = new WaveformData(resolvedPath, lastWrite);
            Cache[resolvedPath] = data;
            CacheOrder.Remove(resolvedPath);
            CacheOrder.Add(resolvedPath);

            while (CacheOrder.Count > MaxCachedFiles)
            {
                Cache[CacheOrder[0]]._cancelled = true;
                Cache.Remove(CacheOrder[0]);
                CacheOrder.RemoveAt(0);
            }

            new Thread(data.Analyse) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal, Name = "ASIO Waveform" }.Start();
            return data;
        }

        private void Analyse()
        {
            try
            {
                using (AudioFileReader reader = new AudioFileReader(FilePath))
                {
                    int channels = reader.WaveFormat.Channels;
                    long estimatedFrames = Math.Max(1, reader.Length / reader.WaveFormat.BlockAlign);
                    int capacity = (int)Math.Min(int.MaxValue - 2, estimatedFrames / FramesPerBucket + 2);

                    float[] min = new float[capacity];
                    float[] max = new float[capacity];
                    float[] meanSquare = new float[capacity];
                    float[] buffer = new float[FramesPerBucket * 64 * channels];
                    float channelWeight = 1f / channels;

                    int bucket = 0, framesInBucket = 0;
                    float bucketMin = float.MaxValue, bucketMax = float.MinValue;
                    double bucketSquares = 0;
                    long frames = 0;
                    int samplesRead;

                    while ((samplesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (_cancelled) return;

                        int framesRead = samplesRead / channels;
                        for (int frame = 0; frame < framesRead; frame++)
                        {
                            float sum = 0f;
                            int frameStart = frame * channels;
                            for (int channel = 0; channel < channels; channel++) sum += buffer[frameStart + channel];
                            float sample = sum * channelWeight;

                            if (sample < bucketMin) bucketMin = sample;
                            if (sample > bucketMax) bucketMax = sample;
                            bucketSquares += sample * sample;

                            if (++framesInBucket == FramesPerBucket)
                            {
                                if (bucket == min.Length) Grow(ref min, ref max, ref meanSquare);
                                min[bucket] = bucketMin; max[bucket] = bucketMax; meanSquare[bucket] = (float)(bucketSquares / framesInBucket);
                                bucket++;
                                framesInBucket = 0; bucketMin = float.MaxValue; bucketMax = float.MinValue; bucketSquares = 0;
                            }
                        }

                        frames += framesRead;
                        Progress = Mathf.Clamp01((float)frames / estimatedFrames);
                    }

                    if (framesInBucket > 0)
                    {
                        if (bucket == min.Length) Grow(ref min, ref max, ref meanSquare);
                        min[bucket] = bucketMin; max[bucket] = bucketMax; meanSquare[bucket] = (float)(bucketSquares / framesInBucket);
                        bucket++;
                    }

                    Min = min; Max = max; MeanSquare = meanSquare;
                    BucketCount = bucket;
                    SampleRate = reader.WaveFormat.SampleRate;
                    TotalFrames = frames;
                    IsReady = true; // volatile write: publishes the fields above
                }
            }
            catch (Exception e)
            {
                Error = e.GetBaseException().Message;
            }
        }

        private static void Grow(ref float[] min, ref float[] max, ref float[] meanSquare)
        {
            int size = min.Length * 2 + 16;
            Array.Resize(ref min, size);
            Array.Resize(ref max, size);
            Array.Resize(ref meanSquare, size);
        }
    }

    /// <summary>
    /// Draws a waveform graph with time and amplitude axes, a playhead, zoom, scroll and seeking.
    /// One instance per Inspector (it holds the view state and the rendered texture).
    /// </summary>
    internal sealed class AsioWaveformView
    {
        private const float AmplitudeAxisWidth = 36f;
        private const float GraphHeight = 120f;
        private const float TimeAxisHeight = 16f;
        private const float MinTimeLabelSpacing = 70f;
        private const float DecibelFloor = -48f;

        private static readonly double[] TimeSteps = { 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600 };
        private static readonly float[] AmplitudeZooms = { 1f, 2f, 4f, 8f, 16f };
        private static readonly string[] AmplitudeZoomLabels = { "Amplitude ×1", "Amplitude ×2", "Amplitude ×4", "Amplitude ×8", "Amplitude ×16" };
        private static readonly string[] ScaleLabels = { "Linear", "dB" };

        private const string FollowPrefKey = "AsioAudioUnity.Waveform.Follow";
        private const string DecibelPrefKey = "AsioAudioUnity.Waveform.Decibels";

        private static bool FollowPlayhead
        {
            get => EditorPrefs.GetBool(FollowPrefKey, true);
            set => EditorPrefs.SetBool(FollowPrefKey, value);
        }

        private static bool DecibelScale
        {
            get => EditorPrefs.GetBool(DecibelPrefKey, false);
            set => EditorPrefs.SetBool(DecibelPrefKey, value);
        }

        // View state
        private double _zoom = 1.0;    // 1: the whole file is visible
        private double _viewStart;     // seconds
        private int _amplitudeZoomIndex;
        private WaveformData _viewedData;

        // Rendered waveform (alpha mask, tinted when drawn)
        private Texture2D _texture;
        private Color32[] _pixels;
        private WaveformData _renderedData;
        private int _renderedWidth, _renderedHeight;
        private double _renderedStart, _renderedDuration;
        private float _renderedAmplitudeZoom;
        private bool _renderedDecibels;

        private GUIStyle _axisLabelStyle, _rightAxisLabelStyle, _overlayLabelStyle, _hoverLabelStyle;

        /// <summary>True while the waveform is being analysed or the mouse is over it: the Inspector should repaint.</summary>
        public bool NeedsRepaint { get; private set; }

        public void Dispose()
        {
            if (_texture != null) UnityEngine.Object.DestroyImmediate(_texture);
            _texture = null;
        }

        /// <summary>
        /// Draws the waveform of a file. Returns the position to seek to (in seconds) when the user clicks or drags on it, otherwise -1.
        /// </summary>
        public double Draw(WaveformData data, double currentSeconds, bool showPlayhead, bool isAdvancing, bool canSeek, Color playedColor)
        {
            NeedsRepaint = false;
            EnsureStyles();

            if (data != _viewedData)
            {
                _viewedData = data;
                _zoom = 1.0;
                _viewStart = 0;
            }

            bool ready = data != null && data.IsReady && data.BucketCount > 0;
            double length = ready ? data.LengthSeconds : 0;

            DrawToolbar(ready);

            Rect area = GUILayoutUtility.GetRect(0f, GraphHeight + TimeAxisHeight, GUILayout.ExpandWidth(true));
            Rect graph = new Rect(area.x + AmplitudeAxisWidth, area.y, Mathf.Max(1f, area.width - AmplitudeAxisWidth), GraphHeight);
            Rect timeAxis = new Rect(graph.x, graph.yMax, graph.width, TimeAxisHeight);
            Rect scrollbarRect = GUILayoutUtility.GetRect(0f, 14f, GUILayout.ExpandWidth(true));
            scrollbarRect.xMin += AmplitudeAxisWidth;

            Event currentEvent = Event.current;
            Color background = EditorGUIUtility.isProSkin ? new Color(0.13f, 0.13f, 0.13f) : new Color(0.72f, 0.72f, 0.72f);
            if (currentEvent.type == EventType.Repaint) EditorGUI.DrawRect(graph, background);

            if (!ready)
            {
                string message = data == null ? "No audio file"
                    : data.Error != null ? "Could not read the waveform: " + data.Error
                    : "Analysing waveform…  " + Mathf.RoundToInt(data.Progress * 100f) + " %";
                GUI.Label(graph, message, _overlayLabelStyle);
                if (data != null && data.Error == null) NeedsRepaint = true;
                using (new EditorGUI.DisabledScope(true)) GUI.HorizontalScrollbar(scrollbarRect, 0f, 1f, 0f, 1f);
                return -1;
            }

            float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
            int pixelWidth = Mathf.Max(1, Mathf.RoundToInt(graph.width * pixelsPerPoint));
            int pixelHeight = Mathf.Max(1, Mathf.RoundToInt(graph.height * pixelsPerPoint));

            // At most one analysis bucket per pixel column.
            double maxZoom = Math.Max(1.0, (double)data.BucketCount / pixelWidth);
            _zoom = Math.Max(1.0, Math.Min(_zoom, maxZoom));
            double visible = length / _zoom;

            // Follow the playhead by pages, so the view doesn't move all the time.
            if (FollowPlayhead && isAdvancing && _zoom > 1.0 && GUIUtility.hotControl == 0
                && (currentSeconds < _viewStart || currentSeconds > _viewStart + visible * 0.95))
            {
                _viewStart = currentSeconds - visible * 0.05;
            }
            _viewStart = Math.Max(0, Math.Min(_viewStart, length - visible));

            double seekTo = HandleInput(graph, data, length, ref visible, maxZoom, canSeek, currentEvent);

            // Scrollbar (time)
            using (new EditorGUI.DisabledScope(_zoom <= 1.0))
            {
                float newStart = GUI.HorizontalScrollbar(scrollbarRect, (float)_viewStart, (float)visible, 0f, (float)length);
                if (_zoom > 1.0 && Math.Abs(newStart - _viewStart) > 1e-6) _viewStart = Math.Max(0, Math.Min(newStart, length - visible));
            }

            if (currentEvent.type == EventType.Repaint)
            {
                bool decibels = DecibelScale;
                float amplitudeZoom = decibels ? 1f : AmplitudeZooms[_amplitudeZoomIndex];

                DrawAmplitudeGrid(area, graph, decibels, amplitudeZoom);
                DrawTimeAxis(graph, timeAxis, visible);

                RenderTexture(data, pixelWidth, pixelHeight, _viewStart, visible, amplitudeZoom, decibels);

                Color unplayedColor = EditorGUIUtility.isProSkin ? new Color(0.55f, 0.7f, 0.9f) : new Color(0.2f, 0.35f, 0.6f);
                float playheadX = graph.x + (float)((currentSeconds - _viewStart) / visible) * graph.width;
                float playedFraction = showPlayhead ? Mathf.Clamp01((playheadX - graph.x) / graph.width) : 0f;

                Color previousColor = GUI.color;
                if (playedFraction > 0f)
                {
                    GUI.color = playedColor;
                    GUI.DrawTextureWithTexCoords(new Rect(graph.x, graph.y, graph.width * playedFraction, graph.height), _texture, new Rect(0f, 0f, playedFraction, 1f));
                }
                if (playedFraction < 1f)
                {
                    GUI.color = unplayedColor;
                    GUI.DrawTextureWithTexCoords(new Rect(graph.x + graph.width * playedFraction, graph.y, graph.width * (1f - playedFraction), graph.height), _texture, new Rect(playedFraction, 0f, 1f - playedFraction, 1f));
                }
                GUI.color = previousColor;

                if (showPlayhead && playheadX >= graph.x && playheadX <= graph.xMax)
                {
                    EditorGUI.DrawRect(new Rect(playheadX - 1f, graph.y, 2f, graph.height + 4f), EditorGUIUtility.isProSkin ? Color.white : Color.black);
                }

                DrawHover(graph, data, visible, decibels);
            }

            // Tooltip with the controls
            GUI.Label(graph, new GUIContent("", (canSeek ? "Click or drag to seek.\n" : "") + "Ctrl + wheel: zoom.  Shift + wheel or middle-drag: scroll."));

            return seekTo;
        }

        // ---------- Toolbar ----------

        private void DrawToolbar(bool ready)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            bool follow = GUILayout.Toggle(FollowPlayhead, new GUIContent("Follow", "Scroll the view to keep the playhead visible when zoomed in."), EditorStyles.toolbarButton);
            if (follow != FollowPlayhead) FollowPlayhead = follow;

            int scale = EditorGUILayout.Popup(DecibelScale ? 1 : 0, ScaleLabels, EditorStyles.toolbarPopup, GUILayout.Width(58f));
            if ((scale == 1) != DecibelScale) DecibelScale = scale == 1;

            using (new EditorGUI.DisabledScope(DecibelScale))
            {
                _amplitudeZoomIndex = EditorGUILayout.Popup(_amplitudeZoomIndex, AmplitudeZoomLabels, EditorStyles.toolbarPopup, GUILayout.Width(98f));
            }

            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(!ready))
            {
                GUILayout.Label("×" + _zoom.ToString(_zoom < 10 ? "0.#" : "0", CultureInfo.InvariantCulture), EditorStyles.miniLabel);
                if (GUILayout.Button(new GUIContent("−", "Zoom out"), EditorStyles.toolbarButton, GUILayout.Width(22f))) ZoomAround(_viewStart + _viewedLength() / _zoom / 2, 1 / 1.5);
                if (GUILayout.Button(new GUIContent("+", "Zoom in"), EditorStyles.toolbarButton, GUILayout.Width(22f))) ZoomAround(_viewStart + _viewedLength() / _zoom / 2, 1.5);
                if (GUILayout.Button(new GUIContent("Fit", "Show the whole file"), EditorStyles.toolbarButton, GUILayout.Width(30f))) { _zoom = 1.0; _viewStart = 0; }
            }

            EditorGUILayout.EndHorizontal();
        }

        private double _viewedLength() => _viewedData != null && _viewedData.IsReady ? _viewedData.LengthSeconds : 0;

        // Zooms by a factor, keeping the given time at the same place on screen (clamped later to the allowed range).
        private void ZoomAround(double anchorSeconds, double factor)
        {
            double length = _viewedLength();
            if (length <= 0) return;

            double oldVisible = length / _zoom;
            double anchorFraction = oldVisible > 0 ? (anchorSeconds - _viewStart) / oldVisible : 0.5;
            _zoom = Math.Max(1.0, _zoom * factor);
            double newVisible = length / _zoom;
            _viewStart = Math.Max(0, Math.Min(anchorSeconds - anchorFraction * newVisible, length - newVisible));
        }

        // ---------- Input ----------

        private double HandleInput(Rect graph, WaveformData data, double length, ref double visible, double maxZoom, bool canSeek, Event currentEvent)
        {
            int controlId = GUIUtility.GetControlID(FocusType.Passive, graph);
            bool mouseOver = graph.Contains(currentEvent.mousePosition);
            if (canSeek) EditorGUIUtility.AddCursorRect(graph, MouseCursor.SlideArrow);
            if (mouseOver) NeedsRepaint = true;

            double visibleDuration = visible;
            double TimeAt(float x) => _viewStart + Mathf.Clamp01((x - graph.x) / graph.width) * visibleDuration;

            switch (currentEvent.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (!mouseOver) break;
                    if (currentEvent.button == 2 || (currentEvent.button == 0 && currentEvent.alt))
                    {
                        GUIUtility.hotControl = controlId; // pan
                        currentEvent.Use();
                    }
                    else if (currentEvent.button == 0 && canSeek)
                    {
                        GUIUtility.hotControl = controlId;
                        currentEvent.Use();
                        return TimeAt(currentEvent.mousePosition.x);
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != controlId) break;
                    currentEvent.Use();
                    if (currentEvent.button == 2 || currentEvent.alt)
                    {
                        _viewStart = Math.Max(0, Math.Min(_viewStart - currentEvent.delta.x / graph.width * visible, length - visible));
                    }
                    else if (canSeek)
                    {
                        return TimeAt(currentEvent.mousePosition.x);
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != controlId) break;
                    GUIUtility.hotControl = 0;
                    currentEvent.Use();
                    break;

                case EventType.ScrollWheel:
                    if (!mouseOver) break;
                    float delta = Mathf.Abs(currentEvent.delta.y) > Mathf.Abs(currentEvent.delta.x) ? currentEvent.delta.y : currentEvent.delta.x;
                    if (EditorGUI.actionKey)
                    {
                        ZoomAround(TimeAt(currentEvent.mousePosition.x), delta > 0 ? 1 / 1.25 : 1.25);
                        _zoom = Math.Min(_zoom, maxZoom);
                        visible = length / _zoom;
                        _viewStart = Math.Max(0, Math.Min(_viewStart, length - visible));
                        currentEvent.Use();
                    }
                    else if (currentEvent.shift && _zoom > 1.0)
                    {
                        _viewStart = Math.Max(0, Math.Min(_viewStart + Math.Sign(delta) * visible * 0.1, length - visible));
                        currentEvent.Use();
                    }
                    break;
            }
            return -1;
        }

        // ---------- Axes ----------

        private void DrawAmplitudeGrid(Rect area, Rect graph, bool decibels, float amplitudeZoom)
        {
            Color gridColor = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.07f) : new Color(0f, 0f, 0f, 0.1f);
            Color centerColor = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.18f) : new Color(0f, 0f, 0f, 0.25f);
            float centerY = graph.center.y;
            float half = graph.height / 2f;

            void Line(float normalized, string label)
            {
                foreach (float sign in normalized > 0f ? new[] { 1f, -1f } : new[] { 1f })
                {
                    float y = centerY - sign * normalized * half;
                    EditorGUI.DrawRect(new Rect(graph.x, Mathf.Round(y), graph.width, 1f), normalized > 0f ? gridColor : centerColor);
                    if (label != null)
                    {
                        string text = sign < 0f && !decibels ? "-" + label : label;
                        Rect labelRect = new Rect(area.x, Mathf.Clamp(y - 7f, graph.y - 2f, graph.yMax - 12f), AmplitudeAxisWidth - 4f, 14f);
                        GUI.Label(labelRect, text, _rightAxisLabelStyle);
                    }
                }
            }

            if (decibels)
            {
                Line(0f, "-∞");
                Line(DecibelToNormalized(-24f), "-24");
                Line(DecibelToNormalized(-12f), "-12");
                Line(DecibelToNormalized(-6f), null);
                Line(1f, "0 dB");
            }
            else
            {
                Line(0f, "0");
                Line(0.5f, FormatAmplitude(0.5f / amplitudeZoom));
                Line(1f, FormatAmplitude(1f / amplitudeZoom));
            }
        }

        private void DrawTimeAxis(Rect graph, Rect timeAxis, double visible)
        {
            double secondsPerPoint = visible / graph.width;
            double step = TimeSteps[TimeSteps.Length - 1];
            foreach (double candidate in TimeSteps)
            {
                if (candidate / secondsPerPoint >= MinTimeLabelSpacing) { step = candidate; break; }
            }

            int decimals = step >= 1 ? 0 : step >= 0.1 ? 1 : step >= 0.01 ? 2 : 3;
            Color tickColor = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.35f) : new Color(0f, 0f, 0f, 0.4f);
            Color gridColor = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.05f) : new Color(0f, 0f, 0f, 0.07f);

            double firstTick = Math.Ceiling(_viewStart / step) * step;
            for (double tick = firstTick; tick <= _viewStart + visible + 1e-9; tick += step)
            {
                float x = Mathf.Round(graph.x + (float)((tick - _viewStart) / secondsPerPoint));
                EditorGUI.DrawRect(new Rect(x, graph.y, 1f, graph.height), gridColor);
                EditorGUI.DrawRect(new Rect(x, timeAxis.y, 1f, 4f), tickColor);
                if (x + 40f <= timeAxis.xMax + 4f) GUI.Label(new Rect(x + 2f, timeAxis.y + 1f, 70f, 14f), FormatTime(tick, decimals), _axisLabelStyle);
            }
        }

        private void DrawHover(Rect graph, WaveformData data, double visible, bool decibels)
        {
            Vector2 mouse = Event.current.mousePosition;
            if (!graph.Contains(mouse) || GUIUtility.hotControl != 0) return;

            EditorGUI.DrawRect(new Rect(Mathf.Round(mouse.x), graph.y, 1f, graph.height), EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.35f) : new Color(0f, 0f, 0f, 0.35f));

            // Levels under the mouse (one point wide)
            double secondsPerPoint = visible / graph.width;
            double start = _viewStart + (mouse.x - graph.x) * secondsPerPoint;
            GetLevels(data, start, start + secondsPerPoint, out float min, out float max, out float rms, out bool hasData);

            string text = FormatTime(start, 3);
            if (hasData)
            {
                float peak = Mathf.Max(Mathf.Abs(min), Mathf.Abs(max));
                text += "   peak " + FormatDecibels(peak) + "   RMS " + FormatDecibels(rms);
            }

            GUIContent content = new GUIContent(text);
            Vector2 size = _hoverLabelStyle.CalcSize(content);
            float x = mouse.x + 8f + size.x > graph.xMax ? Mathf.Max(graph.x, mouse.x - 8f - size.x) : mouse.x + 8f;
            Rect labelRect = new Rect(x, graph.y + 2f, size.x, 16f);
            EditorGUI.DrawRect(labelRect, EditorGUIUtility.isProSkin ? new Color(0f, 0f, 0f, 0.6f) : new Color(1f, 1f, 1f, 0.75f));
            GUI.Label(labelRect, content, _hoverLabelStyle);
        }

        // ---------- Rendering ----------

        private void RenderTexture(WaveformData data, int width, int height, double start, double duration, float amplitudeZoom, bool decibels)
        {
            if (_texture != null && data == _renderedData && width == _renderedWidth && height == _renderedHeight
                && start == _renderedStart && duration == _renderedDuration && amplitudeZoom == _renderedAmplitudeZoom && decibels == _renderedDecibels)
            {
                return;
            }

            if (_texture == null || _texture.width != width || _texture.height != height)
            {
                if (_texture != null) UnityEngine.Object.DestroyImmediate(_texture);
                _texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                _pixels = new Color32[width * height];
            }

            Array.Clear(_pixels, 0, _pixels.Length);
            Color32 peakColor = new Color32(255, 255, 255, 120);
            Color32 rmsColor = new Color32(255, 255, 255, 255);
            float center = (height - 1) / 2f;
            float half = (height - 1) / 2f;

            int ToPixel(float amplitude)
            {
                float normalized = decibels
                    ? Mathf.Sign(amplitude) * DecibelToNormalized(LinearToDecibels(Mathf.Abs(amplitude)))
                    : Mathf.Clamp(amplitude * amplitudeZoom, -1f, 1f);
                return Mathf.Clamp(Mathf.RoundToInt(center + normalized * half), 0, height - 1);
            }

            double secondsPerPixel = duration / width;
            for (int x = 0; x < width; x++)
            {
                double columnStart = start + x * secondsPerPixel;
                GetLevels(data, columnStart, columnStart + secondsPerPixel, out float min, out float max, out float rms, out bool hasData);
                if (!hasData) continue;

                int peakBottom = ToPixel(min), peakTop = ToPixel(max);
                int rmsBottom = Mathf.Max(peakBottom, ToPixel(-rms)), rmsTop = Mathf.Min(peakTop, ToPixel(rms));

                for (int y = peakBottom; y <= peakTop; y++)
                {
                    _pixels[y * width + x] = y >= rmsBottom && y <= rmsTop ? rmsColor : peakColor;
                }
            }

            _texture.SetPixels32(_pixels);
            _texture.Apply(false);

            _renderedData = data;
            _renderedWidth = width; _renderedHeight = height;
            _renderedStart = start; _renderedDuration = duration;
            _renderedAmplitudeZoom = amplitudeZoom; _renderedDecibels = decibels;
        }

        // Min, max and RMS of the (mono) signal between two times.
        private static void GetLevels(WaveformData data, double startSeconds, double endSeconds, out float min, out float max, out float rms, out bool hasData)
        {
            double bucketsPerSecond = (double)data.SampleRate / WaveformData.FramesPerBucket;
            int firstBucket = Math.Max(0, (int)Math.Floor(startSeconds * bucketsPerSecond));
            int lastBucket = Math.Min(data.BucketCount, Math.Max(firstBucket + 1, (int)Math.Ceiling(endSeconds * bucketsPerSecond)));

            min = float.MaxValue; max = float.MinValue;
            double squares = 0;
            for (int bucket = firstBucket; bucket < lastBucket; bucket++)
            {
                if (data.Min[bucket] < min) min = data.Min[bucket];
                if (data.Max[bucket] > max) max = data.Max[bucket];
                squares += data.MeanSquare[bucket];
            }

            hasData = lastBucket > firstBucket;
            if (!hasData) { min = max = rms = 0f; return; }
            rms = (float)Math.Sqrt(squares / (lastBucket - firstBucket));
        }

        // ---------- Formatting ----------

        private static float LinearToDecibels(float linear) => linear > 0.0000001f ? 20f * Mathf.Log10(linear) : -140f;

        private static float DecibelToNormalized(float decibels) => Mathf.Clamp01((decibels - DecibelFloor) / -DecibelFloor);

        private static string FormatDecibels(float linear)
        {
            float decibels = LinearToDecibels(linear);
            return decibels <= -100f ? "-∞ dB" : decibels.ToString("0.0", CultureInfo.InvariantCulture) + " dB";
        }

        private static string FormatAmplitude(float value) => value.ToString(value >= 0.1f ? "0.##" : "0.###", CultureInfo.InvariantCulture);

        private static string FormatTime(double seconds, int decimals)
        {
            if (seconds < 0) seconds = 0;
            double rounded = Math.Round(seconds, decimals);
            int hours = (int)(rounded / 3600);
            int minutes = (int)(rounded / 60) % 60;
            double secs = rounded - Math.Floor(rounded / 60) * 60; // seconds within the minute
            string secondsText = decimals > 0 ? secs.ToString("00." + new string('0', decimals), CultureInfo.InvariantCulture) : ((int)secs).ToString("00");
            return hours > 0 ? hours + ":" + minutes.ToString("00") + ":" + secondsText : minutes + ":" + secondsText;
        }

        private void EnsureStyles()
        {
            if (_axisLabelStyle != null) return;
            _axisLabelStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperLeft, padding = new RectOffset(0, 0, 0, 0), fontSize = 9 };
            _rightAxisLabelStyle = new GUIStyle(_axisLabelStyle) { alignment = TextAnchor.MiddleRight };
            _overlayLabelStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true, padding = new RectOffset(4, 4, 0, 0) };
            _hoverLabelStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(4, 4, 0, 0) };
        }
    }
}
