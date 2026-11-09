using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// A volume slider in the style of the Unity Editor: a thin rounded track filled up to a round thumb, a slim level meter under it,
    /// and a decibel scale (mixing desk fader law, see AsioVolume). Click or drag to set the volume, double-click to reset it to 0 dB.
    /// </summary>
    internal static class AsioVolumeFader
    {
        private static readonly int FaderHash = "AsioVolumeFader".GetHashCode();
        private static GUIStyle _scaleStyle, _scaleRightStyle, _unitStyle;

        /// <summary>Width of a vertical fader with its scale and meter.</summary>
        public const float VerticalWidth = 46f;

        private const float TrackThickness = 4f;
        private const float MeterThickness = 3f;
        private const float ThumbSize = 12f;
        private const float FieldWidth = 46f;
        private const float UnitWidth = 20f;

        private static bool Pro => EditorGUIUtility.isProSkin;
        private static Color TrackColor => Pro ? new Color(0.2f, 0.2f, 0.2f) : new Color(0.62f, 0.62f, 0.62f);
        private static Color MeterBackgroundColor => Pro ? new Color(1f, 1f, 1f, 0.06f) : new Color(0f, 0f, 0f, 0.1f);
        private static Color FillColor => Pro ? new Color(0.27f, 0.53f, 0.92f) : new Color(0.22f, 0.47f, 0.86f);
        private static Color ThumbColor => Pro ? new Color(0.8f, 0.8f, 0.8f) : new Color(0.98f, 0.98f, 0.98f);
        private static Color ThumbBorderColor => Pro ? new Color(0.1f, 0.1f, 0.1f) : new Color(0.55f, 0.55f, 0.55f);
        private static Color TickColor => Pro ? new Color(1f, 1f, 1f, 0.22f) : new Color(0f, 0f, 0f, 0.3f);

        // ---------- Inspector row ----------

        /// <summary>
        /// A full Inspector row, like Unity's sliders: label, slider (with a meter under it), and an editable dB value; the scale is on the line below.
        /// meterLevel: linear peak level to show (negative: no meter). Returns the new linear volume (GUI.changed is set when it changes).
        /// </summary>
        public static float SliderRow(GUIContent label, float linearVolume, float meterLevel, bool mixedValue = false)
        {
            EnsureStyles();

            Rect row = EditorGUILayout.GetControlRect(true, 20f);
            Rect content = EditorGUI.PrefixLabel(row, label);
            Rect unitRect = new Rect(content.xMax - UnitWidth, content.y + 1f, UnitWidth, 18f);
            Rect fieldRect = new Rect(unitRect.x - FieldWidth, content.y + 1f, FieldWidth, 18f);
            Rect sliderRect = new Rect(content.x, content.y, Mathf.Max(20f, fieldRect.x - 8f - content.x), content.height);

            // The scale line is aligned with the slider
            Rect scaleRow = GUILayoutUtility.GetRect(0f, 13f, GUILayout.ExpandWidth(true));
            Rect scaleRect = new Rect(sliderRect.x, scaleRow.y, sliderRect.width, scaleRow.height);

            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            float volume = Draw(sliderRect, linearVolume, meterLevel, false, mixedValue);
            if (Event.current.type == EventType.Repaint) DrawHorizontalScale(scaleRect, TrackRect(sliderRect, false));

            // Editable value in dB ("-6", "-6.0 dB", "-inf"...)
            EditorGUI.showMixedValue = mixedValue;
            EditorGUI.BeginChangeCheck();
            string typed = EditorGUI.DelayedTextField(fieldRect, AsioVolume.FormatLinear(volume, false));
            if (EditorGUI.EndChangeCheck() && TryParseDecibels(typed, out float decibels))
            {
                volume = AsioVolume.ToLinear(Mathf.Min(0f, decibels));
                GUI.changed = true;
            }
            EditorGUI.showMixedValue = false;
            GUI.Label(unitRect, "dB", _unitStyle);

            EditorGUI.indentLevel = indent;
            return volume;
        }

        public static bool TryParseDecibels(string text, out float decibels)
        {
            text = text.Trim().Replace("dB", "").Replace("db", "").Replace("DB", "").Replace(',', '.').Trim();
            if (text == "-∞" || text == "∞" || text.ToLowerInvariant().Contains("inf"))
            {
                decibels = float.NegativeInfinity;
                return true;
            }
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decibels);
        }

        // ---------- Slider ----------

        /// <summary>
        /// Draws the slider (track, fill, thumb and meter). Vertical sliders include their scale on the left.
        /// Returns the new linear volume (GUI.changed is set when the user changes it).
        /// </summary>
        public static float Draw(Rect rect, float linearVolume, float meterLevel, bool vertical, bool mixedValue = false)
        {
            EnsureStyles();
            int controlId = GUIUtility.GetControlID(FaderHash, FocusType.Passive, rect);
            Rect track = TrackRect(rect, vertical);

            float position = mixedValue ? 1f : AsioVolume.LinearToFaderPosition(linearVolume);
            float newPosition = HandleInput(controlId, rect, track, vertical, position);
            bool changed = !Mathf.Approximately(newPosition, position);
            if (changed) position = newPosition;

            if (Event.current.type == EventType.Repaint)
            {
                bool active = GUIUtility.hotControl == controlId;
                if (vertical) DrawVerticalScale(rect, track);

                // Track and fill
                RoundedRect(track, TrackColor, TrackThickness / 2f);
                if (!mixedValue && position > 0f)
                {
                    Rect fill = vertical
                        ? new Rect(track.x, track.yMax - track.height * position, track.width, track.height * position)
                        : new Rect(track.x, track.y, track.width * position, track.height);
                    RoundedRect(fill, GUI.enabled ? FillColor : FillColor * new Color(1f, 1f, 1f, 0.4f), TrackThickness / 2f);
                }

                // Meter, next to the track, on the same scale
                if (meterLevel >= 0f)
                {
                    Rect meter = vertical
                        ? new Rect(track.xMax + 4f, track.y, MeterThickness, track.height)
                        : new Rect(track.x, track.yMax + 4f, track.width, MeterThickness);
                    DrawMeter(meter, meterLevel, vertical);
                }

                // Thumb
                Vector2 center = vertical
                    ? new Vector2(track.center.x, track.yMax - track.height * position)
                    : new Vector2(track.x + track.width * position, track.center.y);
                float size = active ? ThumbSize + 2f : ThumbSize;
                Rect thumb = new Rect(Mathf.Round(center.x - size / 2f), Mathf.Round(center.y - size / 2f), size, size);
                Color face = active ? Color.white : ThumbColor;
                if (mixedValue || !GUI.enabled) face.a = 0.5f;
                RoundedRect(thumb, active ? FillColor : ThumbBorderColor, size / 2f);
                RoundedRect(new Rect(thumb.x + 1f, thumb.y + 1f, thumb.width - 2f, thumb.height - 2f), face, (size - 2f) / 2f);
            }

            if (changed)
            {
                GUI.changed = true;
                return AsioVolume.FaderPositionToLinear(position);
            }
            return linearVolume;
        }

        private static Rect TrackRect(Rect rect, bool vertical)
        {
            const float margin = ThumbSize / 2f + 1f;
            return vertical
                ? new Rect(rect.x + 28f, rect.y + margin, TrackThickness, rect.height - 2f * margin)
                : new Rect(rect.x + margin, rect.y + 6f, rect.width - 2f * margin, TrackThickness);
        }

        private static float HandleInput(int controlId, Rect rect, Rect track, bool vertical, float position)
        {
            Event currentEvent = Event.current;
            if (GUI.enabled) EditorGUIUtility.AddCursorRect(rect, vertical ? MouseCursor.ResizeVertical : MouseCursor.ResizeHorizontal);

            float PositionAt(Vector2 mouse) => vertical
                ? Mathf.Clamp01((track.yMax - mouse.y) / track.height)
                : Mathf.Clamp01((mouse.x - track.x) / track.width);

            switch (currentEvent.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (!GUI.enabled || currentEvent.button != 0 || !rect.Contains(currentEvent.mousePosition)) break;
                    currentEvent.Use();
                    if (currentEvent.clickCount == 2) return 1f; // 0 dB
                    GUIUtility.hotControl = controlId;
                    return PositionAt(currentEvent.mousePosition);

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != controlId) break;
                    currentEvent.Use();
                    return PositionAt(currentEvent.mousePosition);

                case EventType.MouseUp:
                    if (GUIUtility.hotControl != controlId) break;
                    GUIUtility.hotControl = 0;
                    currentEvent.Use();
                    break;
            }
            return position;
        }

        // ---------- Meter and scales ----------

        // Green up to -12 dB, yellow up to -3 dB, red above.
        private static void DrawMeter(Rect meter, float linearLevel, bool vertical)
        {
            RoundedRect(meter, MeterBackgroundColor, MeterThickness / 2f);

            float fill = AsioVolume.DecibelsToFaderPosition(AsioVolume.ToDecibels(linearLevel));
            if (fill <= 0f) return;

            float yellowStart = AsioVolume.DecibelsToFaderPosition(-12f);
            float redStart = AsioVolume.DecibelsToFaderPosition(-3f);

            void Segment(float from, float to, Color color)
            {
                to = Mathf.Min(to, fill);
                if (to <= from) return;
                Rect segment = vertical
                    ? new Rect(meter.x, meter.yMax - meter.height * to, meter.width, meter.height * (to - from))
                    : new Rect(meter.x + meter.width * from, meter.y, meter.width * (to - from), meter.height);
                RoundedRect(segment, color, MeterThickness / 2f);
            }

            Segment(0f, yellowStart, AsioEditorUtility.PlayingColor);
            Segment(yellowStart, redStart, AsioEditorUtility.PausedColor);
            Segment(redStart, 1f, AsioEditorUtility.ErrorColor);
        }

        private static void DrawHorizontalScale(Rect scaleRect, Rect track)
        {
            float lastLabelEnd = float.MinValue;

            void Mark(float faderPosition, string label)
            {
                float x = Mathf.Round(track.x + track.width * faderPosition);
                EditorGUI.DrawRect(new Rect(x, scaleRect.y, 1f, 3f), TickColor);
                float width = _scaleStyle.CalcSize(new GUIContent(label)).x;
                float labelX = Mathf.Clamp(x - width / 2f, scaleRect.x - 4f, scaleRect.xMax - width + 4f);
                if (labelX < lastLabelEnd + 4f) return;
                GUI.Label(new Rect(labelX, scaleRect.y + 2f, width, 11f), label, _scaleStyle);
                lastLabelEnd = labelX + width;
            }

            Mark(0f, "-∞");
            for (int i = AsioVolume.ScaleMarks.Length - 1; i >= 0; i--)
            {
                float decibels = AsioVolume.ScaleMarks[i];
                Mark(AsioVolume.DecibelsToFaderPosition(decibels), decibels.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void DrawVerticalScale(Rect rect, Rect track)
        {
            float lastLabelTop = float.MaxValue; // labels go upwards

            void Mark(float faderPosition, string label)
            {
                float y = Mathf.Round(track.yMax - track.height * faderPosition);
                EditorGUI.DrawRect(new Rect(track.x - 5f, y, 3f, 1f), TickColor);
                float labelTop = y - 6f;
                if (labelTop + 10f > lastLabelTop) return;
                GUI.Label(new Rect(rect.x, labelTop, track.x - rect.x - 7f, 12f), label, _scaleRightStyle);
                lastLabelTop = labelTop;
            }

            Mark(0f, "-∞");
            for (int i = AsioVolume.ScaleMarks.Length - 1; i >= 0; i--)
            {
                float decibels = AsioVolume.ScaleMarks[i];
                Mark(AsioVolume.DecibelsToFaderPosition(decibels), decibels.ToString(CultureInfo.InvariantCulture));
            }
        }

        // ---------- Drawing ----------

        private static void RoundedRect(Rect rect, Color color, float radius)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            radius = Mathf.Min(radius, rect.width / 2f, rect.height / 2f);
            GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, 0f, radius);
        }

        private static void EnsureStyles()
        {
            if (_scaleStyle != null) return;
            _scaleStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 9, alignment = TextAnchor.UpperCenter, padding = new RectOffset(0, 0, 0, 0) };
            _scaleRightStyle = new GUIStyle(_scaleStyle) { alignment = TextAnchor.MiddleRight };
            _unitStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(3, 0, 0, 0) };
        }
    }
}
