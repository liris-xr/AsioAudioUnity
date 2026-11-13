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
    /// Peak and RMS levels of an audio file, mixed down to mono like it is played, and of each of its channels (for multichannel files),
    /// computed in a background thread and cached.
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
        // Per channel ([channel][bucket]), for files with several channels (null for mono files).
        public float[][] ChannelMin;
        public float[][] ChannelMax;
        public float[][] ChannelMeanSquare;
        public int Channels = 1;
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

                    bool perChannel = channels > 1;
                    float[][] channelMin = null, channelMax = null, channelMeanSquare = null;
                    float[] channelBucketMin = null, channelBucketMax = null;
                    double[] channelBucketSquares = null;
                    if (perChannel)
                    {
                        channelMin = new float[channels][]; channelMax = new float[channels][]; channelMeanSquare = new float[channels][];
                        for (int c = 0; c < channels; c++) { channelMin[c] = new float[capacity]; channelMax[c] = new float[capacity]; channelMeanSquare[c] = new float[capacity]; }
                        channelBucketMin = new float[channels]; channelBucketMax = new float[channels]; channelBucketSquares = new double[channels];
                        for (int c = 0; c < channels; c++) { channelBucketMin[c] = float.MaxValue; channelBucketMax[c] = float.MinValue; }
                    }

                    int bucket = 0, framesInBucket = 0;
                    float bucketMin = float.MaxValue, bucketMax = float.MinValue;
                    double bucketSquares = 0;

                    void StoreBucket()
                    {
                        if (bucket == min.Length)
                        {
                            Grow(ref min, ref max, ref meanSquare);
                            if (perChannel) for (int c = 0; c < channels; c++) Grow(ref channelMin[c], ref channelMax[c], ref channelMeanSquare[c]);
                        }
                        min[bucket] = bucketMin; max[bucket] = bucketMax; meanSquare[bucket] = (float)(bucketSquares / framesInBucket);
                        if (perChannel)
                        {
                            for (int c = 0; c < channels; c++)
                            {
                                channelMin[c][bucket] = channelBucketMin[c]; channelMax[c][bucket] = channelBucketMax[c];
                                channelMeanSquare[c][bucket] = (float)(channelBucketSquares[c] / framesInBucket);
                                channelBucketMin[c] = float.MaxValue; channelBucketMax[c] = float.MinValue; channelBucketSquares[c] = 0;
                            }
                        }
                        bucket++;
                        framesInBucket = 0; bucketMin = float.MaxValue; bucketMax = float.MinValue; bucketSquares = 0;
                    }
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
                            for (int channel = 0; channel < channels; channel++)
                            {
                                float channelSample = buffer[frameStart + channel];
                                sum += channelSample;
                                if (perChannel)
                                {
                                    if (channelSample < channelBucketMin[channel]) channelBucketMin[channel] = channelSample;
                                    if (channelSample > channelBucketMax[channel]) channelBucketMax[channel] = channelSample;
                                    channelBucketSquares[channel] += channelSample * channelSample;
                                }
                            }
                            float sample = sum * channelWeight;

                            if (sample < bucketMin) bucketMin = sample;
                            if (sample > bucketMax) bucketMax = sample;
                            bucketSquares += sample * sample;

                            if (++framesInBucket == FramesPerBucket) StoreBucket();
                        }

                        frames += framesRead;
                        Progress = Mathf.Clamp01((float)frames / estimatedFrames);
                    }

                    if (framesInBucket > 0) StoreBucket();

                    Min = min; Max = max; MeanSquare = meanSquare;
                    ChannelMin = channelMin; ChannelMax = channelMax; ChannelMeanSquare = channelMeanSquare;
                    Channels = channels;
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
        private int _renderedLanes;

        /// <summary>Draws each channel of a multichannel file in its own lane (as it plays with Play All Channels), instead of the mono mix.</summary>
        public bool ShowChannels { get; set; }

        // Number of lanes drawn for this file: its channels with ShowChannels, otherwise 1 (the mono mix).
        private int LaneCount(WaveformData data) => ShowChannels && data != null && data.IsReady && data.Channels > 1 && data.ChannelMin != null ? data.Channels : 1;

        // The graph's height: taller with several lanes, within limits.
        private static float GraphHeightFor(int lanes) => lanes <= 1 ? GraphHeight : Mathf.Min(360f, Mathf.Max(GraphHeight, lanes * 60f));

        private static string LaneName(int lane, int lanes) => lanes == 2 ? (lane == 0 ? "L" : "R") : (lane + 1).ToString();

        private GUIStyle _axisLabelStyle, _rightAxisLabelStyle, _overlayLabelStyle, _hoverLabelStyle;

        /// <summary>True while the waveform is being analysed or the mouse is over it: the Inspector should repaint.</summary>
        public bool NeedsRepaint { get; private set; }

        // ---------- Fades (drawn like REAPER's item fades, with draggable handles) ----------

        private static readonly int FadeHandleHash = "AsioWaveformFadeHandle".GetHashCode();
        private int _draggedFade; // 0: none, 1: fade-in, 2: fade-out, 3: loop crossfade

        /// <summary>Draws the fade-in (from the start of the file) and fade-out (to its end) curves, with draggable handles.</summary>
        public bool ShowFades { get; set; }
        public float FadeInSeconds { get; set; }
        public float FadeOutSeconds { get; set; }
        /// <summary>Whether the fade-out also applies at the end of the file (not when looping): otherwise it is drawn dimmed (it only applies to Stop and Pause).</summary>
        public bool FadeOutAtEnd { get; set; } = true;
        public AsioFadeCurve FadeInCurve { get; set; } = AsioFadeCurve.Squared;
        public AsioFadeCurve FadeOutCurve { get; set; } = AsioFadeCurve.Squared;
        /// <summary>The smooth loop's crossfade (drawn over the end of the file, with a handle at the bottom), or 0 for none (no handle: set it only when looping).</summary>
        public float LoopCrossfadeSeconds { get; set; }
        /// <summary>Whether the loop crossfade handle is shown (when looping).</summary>
        public bool ShowLoopCrossfade => ShowFades && !FadeOutAtEnd;
        /// <summary>True after Draw when the user dragged a fade handle or picked a curve: read FadeInSeconds, FadeOutSeconds, FadeInCurve, FadeOutCurve and LoopCrossfadeSeconds.</summary>
        public bool FadesChanged { get; private set; }

        // A curve picked in the right-click menu (applied on the next Draw, since the menu answers outside of it). -1: none.
        private int _pickedFadeInCurve = -1;
        private int _pickedFadeOutCurve = -1;

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
            FadesChanged = false;
            if (_pickedFadeInCurve >= 0) { FadeInCurve = (AsioFadeCurve)_pickedFadeInCurve; FadesChanged = true; }
            if (_pickedFadeOutCurve >= 0) { FadeOutCurve = (AsioFadeCurve)_pickedFadeOutCurve; FadesChanged = true; }
            _pickedFadeInCurve = _pickedFadeOutCurve = -1;
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

            int lanes = LaneCount(data);
            float graphHeight = GraphHeightFor(lanes);
            Rect area = GUILayoutUtility.GetRect(0f, graphHeight + TimeAxisHeight, GUILayout.ExpandWidth(true));
            Rect graph = new Rect(area.x + AmplitudeAxisWidth, area.y, Mathf.Max(1f, area.width - AmplitudeAxisWidth), graphHeight);
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

            // Fade handles first, so dragging them doesn't seek
            HandleFadeInput(graph, length, visible, currentEvent);
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

                if (lanes > 1) DrawLaneGrid(area, graph, lanes);
                else DrawAmplitudeGrid(area, graph, decibels, amplitudeZoom);
                DrawTimeAxis(graph, timeAxis, visible);

                RenderTexture(data, pixelWidth, pixelHeight, _viewStart, visible, amplitudeZoom, decibels, lanes);

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

                if (ShowFades) DrawFades(graph, length, visible);

                if (showPlayhead && playheadX >= graph.x && playheadX <= graph.xMax)
                {
                    EditorGUI.DrawRect(new Rect(playheadX - 1f, graph.y, 2f, graph.height + 4f), EditorGUIUtility.isProSkin ? Color.white : Color.black);
                }

                if (_draggedFade == 0) DrawHover(graph, data, visible, decibels, lanes);
            }

            // Tooltip with the controls
            GUI.Label(graph, new GUIContent("", (canSeek ? "Click or drag to seek.\n" : "") + "Ctrl + wheel: zoom.  Shift + wheel or middle-drag: scroll."));

            return seekTo;
        }

        // ---------- Fades ----------

        private float TimeToX(Rect graph, double seconds, double visible) => graph.x + (float)((seconds - _viewStart) / visible) * graph.width;

        private void GetFadeHandles(Rect graph, double length, double visible, out Rect fadeInHandle, out Rect fadeOutHandle)
        {
            float fadeInX = TimeToX(graph, Math.Min(FadeInSeconds, length), visible);
            float fadeOutX = TimeToX(graph, Math.Max(0, length - FadeOutSeconds), visible);
            fadeInHandle = new Rect(Mathf.Round(fadeInX) - 5f, graph.y + 1f, 10f, 10f);
            fadeOutHandle = new Rect(Mathf.Round(fadeOutX) - 5f, graph.y + 1f, 10f, 10f);
        }

        private Rect GetLoopCrossfadeHandle(Rect graph, double length, double visible)
        {
            float x = TimeToX(graph, Math.Max(0, length - Math.Min(LoopCrossfadeSeconds, length / 2)), visible);
            return new Rect(Mathf.Round(x) - 5f, graph.yMax - 11f, 10f, 10f);
        }

        private void HandleFadeInput(Rect graph, double length, double visible, Event currentEvent)
        {
            if (!ShowFades) return;
            int controlId = GUIUtility.GetControlID(FadeHandleHash, FocusType.Passive, graph);
            GetFadeHandles(graph, length, visible, out Rect fadeInHandle, out Rect fadeOutHandle);
            bool fadeInVisible = fadeInHandle.center.x >= graph.x - 5f && fadeInHandle.center.x <= graph.xMax + 5f;
            bool fadeOutVisible = fadeOutHandle.center.x >= graph.x - 5f && fadeOutHandle.center.x <= graph.xMax + 5f;
            if (fadeInVisible) EditorGUIUtility.AddCursorRect(fadeInHandle, MouseCursor.ResizeHorizontal);
            if (fadeOutVisible) EditorGUIUtility.AddCursorRect(fadeOutHandle, MouseCursor.ResizeHorizontal);
            Rect loopHandle = GetLoopCrossfadeHandle(graph, length, visible);
            bool loopVisible = ShowLoopCrossfade && loopHandle.center.x >= graph.x - 5f && loopHandle.center.x <= graph.xMax + 5f;
            if (loopVisible) EditorGUIUtility.AddCursorRect(loopHandle, MouseCursor.ResizeHorizontal);

            switch (currentEvent.GetTypeForControl(controlId))
            {
                case EventType.MouseDown when currentEvent.button == 1:
                {
                    // Right-click on a fade (its handle or its area): choose its curve, like in REAPER
                    if (!graph.Contains(currentEvent.mousePosition)) break;
                    double clickedSeconds = _viewStart + (currentEvent.mousePosition.x - graph.x) / graph.width * visible;
                    int fade = fadeInHandle.Contains(currentEvent.mousePosition) || clickedSeconds <= FadeInSeconds ? 1
                        : fadeOutHandle.Contains(currentEvent.mousePosition) || clickedSeconds >= length - FadeOutSeconds ? 2 : 0;
                    if (fade == 0) break;
                    currentEvent.Use();
                    ShowFadeCurveMenu(fade == 1);
                    break;
                }

                case EventType.MouseDown:
                    if (currentEvent.button != 0) break;
                    int handle = fadeInVisible && fadeInHandle.Contains(currentEvent.mousePosition) ? 1 : fadeOutVisible && fadeOutHandle.Contains(currentEvent.mousePosition) ? 2
                        : loopVisible && loopHandle.Contains(currentEvent.mousePosition) ? 3 : 0;
                    if (handle == 0) break;
                    currentEvent.Use();
                    if (currentEvent.clickCount == 2)
                    {
                        // Double-click: no fade
                        if (handle == 1) FadeInSeconds = 0f; else if (handle == 2) FadeOutSeconds = 0f; else LoopCrossfadeSeconds = 0f;
                        FadesChanged = true;
                        break;
                    }
                    _draggedFade = handle;
                    GUIUtility.hotControl = controlId;
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != controlId || _draggedFade == 0) break;
                    currentEvent.Use();
                    double seconds = _viewStart + Mathf.Clamp01((currentEvent.mousePosition.x - graph.x) / graph.width) * visible;
                    if (_draggedFade == 1) FadeInSeconds = (float)Math.Round(Math.Max(0, Math.Min(seconds, length - FadeOutSeconds)), 2);
                    else if (_draggedFade == 2) FadeOutSeconds = (float)Math.Round(Math.Max(0, Math.Min(length - seconds, length - FadeInSeconds)), 2);
                    else LoopCrossfadeSeconds = (float)Math.Round(Math.Max(0, Math.Min(length - seconds, length / 2)), 2);
                    FadesChanged = true;
                    NeedsRepaint = true;
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != controlId) break;
                    GUIUtility.hotControl = 0;
                    _draggedFade = 0;
                    currentEvent.Use();
                    break;
            }
        }

        // Fade curves (the gain, with each fade's curve), the attenuated area shaded above them, and the handles at the top.
        private void DrawFades(Rect graph, double length, double visible)
        {
            bool pro = EditorGUIUtility.isProSkin;
            Color line = pro ? new Color(1f, 1f, 1f, 0.9f) : new Color(0f, 0f, 0f, 0.8f);
            Color shade = pro ? new Color(0f, 0f, 0f, 0.45f) : new Color(1f, 1f, 1f, 0.5f);
            Color accent = pro ? new Color(0.35f, 0.6f, 0.95f) : new Color(0.25f, 0.5f, 0.9f);

            DrawFadeCurve(graph, visible, 0, Math.Min(FadeInSeconds, length), true, FadeInCurve, line, shade);
            Color fadeOutLine = FadeOutAtEnd ? line : new Color(line.r, line.g, line.b, line.a * 0.4f);
            Color fadeOutShade = FadeOutAtEnd ? shade : new Color(shade.r, shade.g, shade.b, shade.a * 0.4f);
            DrawFadeCurve(graph, visible, Math.Max(0, length - FadeOutSeconds), length, false, FadeOutCurve, fadeOutLine, fadeOutShade);

            GetFadeHandles(graph, length, visible, out Rect fadeInHandle, out Rect fadeOutHandle);
            DrawFadeHandle(graph, fadeInHandle, _draggedFade == 1 ? accent : line, FadeInSeconds > 0f || _draggedFade == 1 ? "Fade in " + FormatSeconds(FadeInSeconds) : null, true);
            DrawFadeHandle(graph, fadeOutHandle, _draggedFade == 2 ? accent : fadeOutLine,
                FadeOutSeconds > 0f || _draggedFade == 2 ? "Fade out " + FormatSeconds(FadeOutSeconds) + (FadeOutAtEnd ? "" : " (Stop / Pause)") : null, false);

            if (ShowLoopCrossfade) DrawLoopCrossfade(graph, length, visible, _draggedFade == 3 ? accent : line, accent);
        }

        // The smooth loop: the end of the file fading out and its start fading in (equal power) over the end, and where each loop continues.
        private void DrawLoopCrossfade(Rect graph, double length, double visible, Color handleColor, Color accent)
        {
            double crossfade = Math.Min(LoopCrossfadeSeconds, length / 2);
            Rect handle = GetLoopCrossfadeHandle(graph, length, visible);
            if (crossfade > 0)
            {
                double start = length - crossfade;
                double visibleStart = Math.Max(start, _viewStart), visibleEnd = Math.Min(length, _viewStart + visible);
                if (visibleEnd > visibleStart)
                {
                    float fromX = TimeToX(graph, visibleStart, visible), toX = TimeToX(graph, visibleEnd, visible);
                    EditorGUI.DrawRect(new Rect(fromX, graph.y, toX - fromX, graph.height), new Color(accent.r, accent.g, accent.b, 0.1f));

                    const int segments = 48;
                    Vector3[] fadingOut = new Vector3[segments + 1], fadingIn = new Vector3[segments + 1];
                    for (int i = 0; i <= segments; i++)
                    {
                        double seconds = visibleStart + (visibleEnd - visibleStart) * i / segments;
                        double angle = (seconds - start) / crossfade * (Math.PI / 2);
                        float x = TimeToX(graph, seconds, visible);
                        fadingOut[i] = new Vector3(x, graph.yMax - (float)Math.Cos(angle) * graph.height, 0f);
                        fadingIn[i] = new Vector3(x, graph.yMax - (float)Math.Sin(angle) * graph.height, 0f);
                    }
                    Color previous = Handles.color;
                    Handles.color = accent;
                    Handles.DrawAAPolyLine(2f, fadingOut);
                    Handles.DrawAAPolyLine(2f, fadingIn);
                    Handles.color = previous;
                }

                // Where each loop continues (the end of the start's fade-in)
                float loopStartX = TimeToX(graph, crossfade, visible);
                if (loopStartX >= graph.x && loopStartX <= graph.xMax)
                {
                    EditorGUI.DrawRect(new Rect(Mathf.Round(loopStartX), graph.y, 1f, graph.height), new Color(accent.r, accent.g, accent.b, 0.8f));
                    GUIContent content = new GUIContent("Loop continues here");
                    Vector2 size = _hoverLabelStyle.CalcSize(content);
                    Rect labelRect = new Rect(Mathf.Min(loopStartX + 3f, graph.xMax - size.x), graph.yMax - 15f, size.x, 14f);
                    EditorGUI.DrawRect(labelRect, EditorGUIUtility.isProSkin ? new Color(0f, 0f, 0f, 0.55f) : new Color(1f, 1f, 1f, 0.7f));
                    GUI.Label(labelRect, content, _hoverLabelStyle);
                }
            }

            DrawFadeHandle(graph, handle, handleColor, crossfade > 0 || _draggedFade == 3 ? "Loop crossfade " + FormatSeconds((float)crossfade) : null, false);
        }

        private static readonly (AsioFadeCurve curve, string name)[] FadeCurveNames =
        {
            (AsioFadeCurve.Squared, "Squared (default)"),
            (AsioFadeCurve.Linear, "Linear"),
            (AsioFadeCurve.InvertedSquared, "Inverted Squared"),
            (AsioFadeCurve.SCurve, "S-Curve"),
        };

        public static string GetFadeCurveName(AsioFadeCurve curve)
        {
            foreach ((AsioFadeCurve c, string name) in FadeCurveNames) if (c == curve) return name;
            return curve.ToString();
        }

        private void ShowFadeCurveMenu(bool fadeIn)
        {
            GenericMenu menu = new GenericMenu();
            AsioFadeCurve current = fadeIn ? FadeInCurve : FadeOutCurve;
            menu.AddDisabledItem(new GUIContent(fadeIn ? "Fade-in curve" : "Fade-out curve"));
            menu.AddSeparator("");
            foreach ((AsioFadeCurve curve, string name) in FadeCurveNames)
            {
                menu.AddItem(new GUIContent(name), curve == current, () =>
                {
                    if (fadeIn) _pickedFadeInCurve = (int)curve; else _pickedFadeOutCurve = (int)curve;
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                });
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Apply to both fades"), false, () =>
            {
                _pickedFadeInCurve = _pickedFadeOutCurve = (int)current;
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            });
            menu.ShowAsContext();
        }

        private void DrawFadeCurve(Rect graph, double visible, double start, double end, bool rising, AsioFadeCurve curve, Color line, Color shade)
        {
            double duration = end - start;
            if (duration <= 0) return;

            // Only the visible part
            double visibleStart = Math.Max(start, _viewStart), visibleEnd = Math.Min(end, _viewStart + visible);
            if (visibleEnd <= visibleStart) return;

            float Gain(double seconds)
            {
                double x = rising ? (seconds - start) / duration : (end - seconds) / duration;
                return AsioFadeCurves.Evaluate(curve, (float)x);
            }

            float fromX = TimeToX(graph, visibleStart, visible), toX = TimeToX(graph, visibleEnd, visible);

            // Shaded attenuation, above the curve
            for (float x = fromX; x < toX; x += 2f)
            {
                double seconds = _viewStart + (x - graph.x) / graph.width * visible;
                float curveY = graph.yMax - Gain(seconds) * graph.height;
                EditorGUI.DrawRect(new Rect(x, graph.y, Mathf.Min(2f, toX - x), curveY - graph.y), shade);
            }

            // The curve
            const int segments = 48;
            Vector3[] points = new Vector3[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                double seconds = visibleStart + (visibleEnd - visibleStart) * i / segments;
                points[i] = new Vector3(TimeToX(graph, seconds, visible), graph.yMax - Gain(seconds) * graph.height, 0f);
            }
            Color previous = Handles.color;
            Handles.color = line;
            Handles.DrawAAPolyLine(2f, points);
            Handles.color = previous;
        }

        private void DrawFadeHandle(Rect graph, Rect handle, Color color, string label, bool labelOnRight)
        {
            if (handle.center.x < graph.x - 5f || handle.center.x > graph.xMax + 5f) return;
            EditorGUI.DrawRect(handle, color);
            EditorGUI.DrawRect(new Rect(handle.x + 2f, handle.y + 2f, handle.width - 4f, handle.height - 4f), EditorGUIUtility.isProSkin ? new Color(0.13f, 0.13f, 0.13f) : new Color(0.72f, 0.72f, 0.72f));
            if (label == null) return;

            GUIContent content = new GUIContent(label);
            Vector2 size = _hoverLabelStyle.CalcSize(content);
            float x = labelOnRight ? handle.xMax + 2f : handle.x - 2f - size.x;
            x = Mathf.Clamp(x, graph.x, graph.xMax - size.x);
            Rect labelRect = new Rect(x, handle.y - 2f, size.x, 14f);
            EditorGUI.DrawRect(labelRect, EditorGUIUtility.isProSkin ? new Color(0f, 0f, 0f, 0.55f) : new Color(1f, 1f, 1f, 0.7f));
            GUI.Label(labelRect, content, _hoverLabelStyle);
        }

        private static string FormatSeconds(float seconds) => seconds.ToString(seconds < 10f ? "0.00" : "0.0", CultureInfo.InvariantCulture) + " s";

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

        // With several lanes: a separator between lanes, each lane's center line, and its channel name (L / R, or its number) on the left.
        private void DrawLaneGrid(Rect area, Rect graph, int lanes)
        {
            Color centerColor = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.18f) : new Color(0f, 0f, 0f, 0.25f);
            Color separatorColor = EditorGUIUtility.isProSkin ? new Color(0f, 0f, 0f, 0.6f) : new Color(1f, 1f, 1f, 0.7f);
            float laneHeight = graph.height / lanes;
            for (int lane = 0; lane < lanes; lane++)
            {
                float top = graph.y + lane * laneHeight;
                if (lane > 0) EditorGUI.DrawRect(new Rect(graph.x, Mathf.Round(top), graph.width, 1f), separatorColor);
                EditorGUI.DrawRect(new Rect(graph.x, Mathf.Round(top + laneHeight / 2f), graph.width, 1f), centerColor);
                GUI.Label(new Rect(area.x, top + laneHeight / 2f - 7f, AmplitudeAxisWidth - 4f, 14f), LaneName(lane, lanes), _rightAxisLabelStyle);
            }
        }

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

        private void DrawHover(Rect graph, WaveformData data, double visible, bool decibels, int lanes)
        {
            Vector2 mouse = Event.current.mousePosition;
            if (!graph.Contains(mouse) || GUIUtility.hotControl != 0) return;

            EditorGUI.DrawRect(new Rect(Mathf.Round(mouse.x), graph.y, 1f, graph.height), EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.35f) : new Color(0f, 0f, 0f, 0.35f));

            // Levels under the mouse (one point wide)
            double secondsPerPoint = visible / graph.width;
            double start = _viewStart + (mouse.x - graph.x) * secondsPerPoint;
            int lane = lanes > 1 ? Mathf.Clamp((int)((mouse.y - graph.y) / (graph.height / lanes)), 0, lanes - 1) : -1;
            GetLevels(data, start, start + secondsPerPoint, lanes > 1 ? lane : -1, out float min, out float max, out float rms, out bool hasData);

            string text = (lanes > 1 ? "Channel " + LaneName(lane, lanes) + "   " : "") + FormatTime(start, 3);
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

        private void RenderTexture(WaveformData data, int width, int height, double start, double duration, float amplitudeZoom, bool decibels, int lanes)
        {
            if (_texture != null && data == _renderedData && width == _renderedWidth && height == _renderedHeight
                && start == _renderedStart && duration == _renderedDuration && amplitudeZoom == _renderedAmplitudeZoom && decibels == _renderedDecibels && lanes == _renderedLanes)
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
            double secondsPerPixel = duration / width;

            // One lane per channel (the first at the top: texture rows go upwards), or a single lane with the mono mix.
            for (int lane = 0; lane < lanes; lane++)
            {
                int laneHeight = height / lanes;
                int laneBottom = lanes == 1 ? 0 : height - (lane + 1) * laneHeight;
                if (lanes == 1) laneHeight = height;
                int laneTop = laneBottom + laneHeight - 1 - (lanes > 1 ? 1 : 0); // 1 pixel between lanes
                float center = (laneBottom + laneTop) / 2f;
                float half = (laneTop - laneBottom) / 2f;

                int ToPixel(float amplitude)
                {
                    float normalized = decibels
                        ? Mathf.Sign(amplitude) * DecibelToNormalized(LinearToDecibels(Mathf.Abs(amplitude)))
                        : Mathf.Clamp(amplitude * amplitudeZoom, -1f, 1f);
                    return Mathf.Clamp(Mathf.RoundToInt(center + normalized * half), laneBottom, laneTop);
                }

                for (int x = 0; x < width; x++)
                {
                    double columnStart = start + x * secondsPerPixel;
                    GetLevels(data, columnStart, columnStart + secondsPerPixel, lanes > 1 ? lane : -1, out float min, out float max, out float rms, out bool hasData);
                    if (!hasData) continue;

                    int peakBottom = ToPixel(min), peakTop = ToPixel(max);
                    int rmsBottom = Mathf.Max(peakBottom, ToPixel(-rms)), rmsTop = Mathf.Min(peakTop, ToPixel(rms));

                    for (int y = peakBottom; y <= peakTop; y++)
                    {
                        _pixels[y * width + x] = y >= rmsBottom && y <= rmsTop ? rmsColor : peakColor;
                    }
                }
            }

            _texture.SetPixels32(_pixels);
            _texture.Apply(false);

            _renderedData = data;
            _renderedWidth = width; _renderedHeight = height;
            _renderedStart = start; _renderedDuration = duration;
            _renderedAmplitudeZoom = amplitudeZoom; _renderedDecibels = decibels; _renderedLanes = lanes;
        }

        // Min, max and RMS between two times: of the mono mix (channel -1), or of a channel.
        private static void GetLevels(WaveformData data, double startSeconds, double endSeconds, int channel, out float min, out float max, out float rms, out bool hasData)
        {
            float[] mins = channel < 0 ? data.Min : data.ChannelMin[channel];
            float[] maxs = channel < 0 ? data.Max : data.ChannelMax[channel];
            float[] meanSquares = channel < 0 ? data.MeanSquare : data.ChannelMeanSquare[channel];
            double bucketsPerSecond = (double)data.SampleRate / WaveformData.FramesPerBucket;
            int firstBucket = Math.Max(0, (int)Math.Floor(startSeconds * bucketsPerSecond));
            int lastBucket = Math.Min(data.BucketCount, Math.Max(firstBucket + 1, (int)Math.Ceiling(endSeconds * bucketsPerSecond)));

            min = float.MaxValue; max = float.MinValue;
            double squares = 0;
            for (int bucket = firstBucket; bucket < lastBucket; bucket++)
            {
                if (mins[bucket] < min) min = mins[bucket];
                if (maxs[bucket] > max) max = maxs[bucket];
                squares += meanSquares[bucket];
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
