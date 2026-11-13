using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NAudio.Wave;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AsioAudioUnity
{
    /// <summary>
    /// Shared helpers for the ASIO Audio Unity inspectors and the ASIO Audio Mixer window.
    /// </summary>
    internal static class AsioEditorUtility
    {
        public static readonly int[] CommonSampleRates = { 44100, 48000, 88200, 96000, 176400, 192000 };
        // What NAudio can read on Windows (.mp3 and .flac through Windows' own decoders). Not .ogg: NAudio has no Ogg Vorbis decoder.
        public static readonly string[] AudioExtensions = { ".wav", ".mp3", ".aiff", ".aif", ".flac" };

        public static readonly Color PlayingColor = new Color(0.35f, 0.80f, 0.45f);
        public static readonly Color PausedColor = new Color(0.95f, 0.75f, 0.25f);
        public static readonly Color StoppedColor = new Color(0.55f, 0.55f, 0.55f);
        public static readonly Color ErrorColor = new Color(0.95f, 0.35f, 0.35f);

        // ---------- Settings and manager ----------

        /// <summary>The project's ASIO Audio Settings (default, unsaved settings if the asset doesn't exist yet).</summary>
        public static AsioAudioSettings Settings => AsioAudioSettings.Instance;

        /// <summary>The running ASIO Audio Manager (Play mode only, once created), or null.</summary>
        public static AsioAudioManager LiveManager => EditorApplication.isPlaying ? AsioAudioManager.Existing : null;

        /// <summary>Opens Project Settings > ASIO Audio.</summary>
        public static void OpenSettings()
        {
            SettingsService.OpenProjectSettings(AsioAudioSettingsProvider.SettingsPath);
        }

        // ---------- Scene objects ----------

        /// <summary>All ASIO Audio Sources in the open scenes (including inactive ones), sorted by output channel.</summary>
        public static AsioAudioSource[] FindAllSources()
        {
            return Object.FindObjectsByType<AsioAudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .OrderBy(source => source.TargetOutputChannel <= 0 ? int.MaxValue : source.TargetOutputChannel)
                .ThenBy(source => source.name)
                .ToArray();
        }

        /// <summary>
        /// Records an Undo step (and prefab override) for a change made through an object's C# properties.
        /// </summary>
        public static void RecordChange(Object target, string undoName, Action change)
        {
            Undo.RecordObject(target, undoName);
            change();
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            if (EditorUtility.IsPersistent(target)) EditorUtility.SetDirty(target); // assets (the settings) must be saved
        }

        /// <summary>Records an Undo step for a change of the ASIO Audio Settings (creating the settings asset first if needed).</summary>
        public static void RecordSettingsChange(string undoName, Action<AsioAudioSettings> change)
        {
            AsioAudioSettings settings = AsioAudioSettingsProvider.GetOrCreateSettingsAsset();
            RecordChange(settings, undoName, () => change(settings));
        }

        // ---------- Mixer snapshots (with Undo) ----------

        public static string NextSnapshotName()
        {
            List<AsioMixerSnapshot> snapshots = Settings.Snapshots;
            int number = snapshots.Count + 1;
            while (snapshots.Any(snapshot => snapshot.Name == "Snapshot " + number)) number++;
            return "Snapshot " + number;
        }

        public static void SaveSnapshot(string snapshotName)
        {
            AsioAudioSource[] sources = FindAllSources();
            RecordSettingsChange("Save Mixer Snapshot", settings => settings.SaveSnapshot(snapshotName, sources));
        }

        public static void RecallSnapshot(int index)
        {
            AsioAudioSettings settings = AsioAudioSettingsProvider.GetOrCreateSettingsAsset();
            if (index < 0 || index >= settings.Snapshots.Count) return;

            AsioAudioSource[] sources = FindAllSources();
            List<Object> changed = new List<Object> { settings };
            changed.AddRange(sources);
            Undo.RecordObjects(changed.ToArray(), "Recall Mixer Snapshot");
            settings.ApplySnapshot(settings.Snapshots[index], sources);
            foreach (Object target in changed) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            EditorUtility.SetDirty(settings);
        }

        public static void DeleteSnapshot(int index)
        {
            if (index < 0 || index >= Settings.Snapshots.Count) return;
            RecordSettingsChange("Delete Mixer Snapshot", settings => settings.Snapshots.RemoveAt(index));
        }

        // ---------- ASIO driver ----------

        private static string[] _driverNames;

        /// <summary>Names of the ASIO drivers installed on this machine (cached until refreshed).</summary>
        public static string[] GetDriverNames(bool refresh = false)
        {
            if (_driverNames == null || refresh)
            {
                try { _driverNames = AsioOut.GetDriverNames(); }
                catch (Exception) { _driverNames = new string[0]; }
            }
            return _driverNames;
        }

        // The running manager, if it is connected to the driver of the settings.
        private static AsioAudioManager ConnectedManager
        {
            get
            {
                AsioAudioManager manager = LiveManager;
                return manager != null && manager.IsConnected && manager.AsioDriverName == Settings.AsioDriverName ? manager : null;
            }
        }

        /// <summary>
        /// Output channel count of the driver of the settings: the live value when connected, otherwise the last value seen for this driver (0 if never seen).
        /// </summary>
        public static int GetKnownOutputChannelCount()
        {
            string driverName = Settings.AsioDriverName;
            if (string.IsNullOrEmpty(driverName)) return 0;

            AsioAudioManager manager = ConnectedManager;
            return manager != null ? manager.AsioDriverOutputChannelCount : EditorPrefs.GetInt("AsioAudioUnity.OutputChannelCount." + driverName, 0);
        }

        /// <summary>
        /// Sample format of the driver of the settings (e.g. "24-bit integer"): the live value when connected, otherwise the last value seen for this driver (empty if never seen).
        /// </summary>
        public static string GetKnownDriverSampleFormat()
        {
            string driverName = Settings.AsioDriverName;
            if (string.IsNullOrEmpty(driverName)) return "";

            AsioAudioManager manager = ConnectedManager;
            return manager != null && !string.IsNullOrEmpty(manager.AsioDriverSampleFormat)
                ? manager.AsioDriverSampleFormat
                : EditorPrefs.GetString("AsioAudioUnity.DriverSampleFormat." + driverName, "");
        }

        /// <summary>
        /// The driver's name of an output channel (1-based, e.g. "ReaRoute 3"): the live value when connected, otherwise the last value seen for this driver (empty if unknown).
        /// </summary>
        public static string GetKnownOutputChannelName(int channel)
        {
            string driverName = Settings.AsioDriverName;
            if (string.IsNullOrEmpty(driverName) || channel < 1) return "";

            AsioAudioManager manager = ConnectedManager;
            if (manager != null) return manager.GetOutputChannelName(channel);

            string[] names = EditorPrefs.GetString("AsioAudioUnity.OutputChannelNames." + driverName, "").Split('\n');
            return channel <= names.Length ? names[channel - 1] : "";
        }

        // ---------- Output channels ----------

        public enum ChannelIssue { None, NotSet, OutOfRange, Duplicate }

        /// <summary>
        /// The number of output channels a source uses: 1, or its file's channel count with Play All Channels
        /// (from the loaded file in Play mode, otherwise read from the file).
        /// </summary>
        public static int GetOutputChannelCount(AsioAudioSource source)
        {
            if (source == null || !source.PlayAllChannels) return 1;
            if (source.Voice != null && source.Voice.IsLoaded) return source.OutputChannelCount;
            AudioFileInfo info = GetAudioFileInfo(source.ResolvedAudioFilePath);
            return Mathf.Max(1, info != null ? info.Channels : source.Channels);
        }

        private static readonly Dictionary<int, float[]> SilentMeters = new Dictionary<int, float[]>();

        /// <summary>
        /// The levels to show on a source's meters: one per channel it plays on. Live levels in Play mode; otherwise silent meters
        /// (so the layout doesn't change when playback starts).
        /// </summary>
        public static IReadOnlyList<float> GetMeterLevels(AsioAudioSource source, bool live)
        {
            if (live && source.ChannelMeterLevels.Count > 0) return source.ChannelMeterLevels;
            int count = GetOutputChannelCount(source);
            if (!SilentMeters.TryGetValue(count, out float[] silent)) SilentMeters[count] = silent = new float[count];
            return silent;
        }

        /// <summary>Whether the source plays on this output channel (1-based).</summary>
        public static bool UsesChannel(AsioAudioSource source, int channel)
        {
            return source.TargetOutputChannel > 0 && channel >= source.TargetOutputChannel && channel < source.TargetOutputChannel + GetOutputChannelCount(source);
        }

        /// <summary>Whether two sources use a common output channel.</summary>
        public static bool SharesChannel(AsioAudioSource a, AsioAudioSource b)
        {
            if (a.TargetOutputChannel <= 0 || b.TargetOutputChannel <= 0) return false;
            return a.TargetOutputChannel < b.TargetOutputChannel + GetOutputChannelCount(b) && b.TargetOutputChannel < a.TargetOutputChannel + GetOutputChannelCount(a);
        }

        /// <summary>"5", or "5–6" for a source playing several channels.</summary>
        public static string DescribeChannels(AsioAudioSource source)
        {
            if (source.TargetOutputChannel <= 0) return "–";
            int count = GetOutputChannelCount(source);
            return count <= 1 ? source.TargetOutputChannel.ToString() : source.TargetOutputChannel + "–" + (source.TargetOutputChannel + count - 1);
        }

        public static ChannelIssue GetChannelIssue(AsioAudioSource source, IList<AsioAudioSource> allSources, int outputChannelCount)
        {
            int channel = source.TargetOutputChannel;
            if (channel <= 0) return ChannelIssue.NotSet;
            if (outputChannelCount > 0 && channel + GetOutputChannelCount(source) - 1 > outputChannelCount) return ChannelIssue.OutOfRange;
            if (allSources.Any(other => other != source && SharesChannel(source, other))) return ChannelIssue.Duplicate;
            return ChannelIssue.None;
        }

        public static string DescribeChannelIssue(ChannelIssue issue, AsioAudioSource source, IList<AsioAudioSource> allSources, int outputChannelCount)
        {
            switch (issue)
            {
                case ChannelIssue.NotSet:
                    return "No output channel is set: this source will be ignored.";
                case ChannelIssue.OutOfRange:
                    return GetOutputChannelCount(source) > 1
                        ? "Channels " + DescribeChannels(source) + " don't all exist: the ASIO driver has " + outputChannelCount + " output channels (the channels beyond aren't played)."
                        : "Channel " + source.TargetOutputChannel + " doesn't exist: the ASIO driver has " + outputChannelCount + " output channels.";
                case ChannelIssue.Duplicate:
                    string others = string.Join(", ", allSources.Where(other => other != source && SharesChannel(source, other)).Select(other => "\"" + other.name + "\" (" + DescribeChannels(other) + ")"));
                    return "Channel " + DescribeChannels(source) + " is also used by " + others + ". Each source needs its own channels: only one of them will play.";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Builds the options of an output channel popup: "None", then each channel, labelled with the source already using it (other than the edited ones).
        /// </summary>
        public static void BuildChannelOptions(int currentChannel, int outputChannelCount, IList<AsioAudioSource> allSources, ICollection<AsioAudioSource> editedSources, out GUIContent[] labels, out int[] values)
        {
            int highestUsedChannel = allSources.Count == 0 ? 0 : allSources.Max(source => source.TargetOutputChannel + GetOutputChannelCount(source) - 1);
            int channelCount = outputChannelCount > 0 ? outputChannelCount : Mathf.Max(16, highestUsedChannel + 1);

            List<GUIContent> labelList = new List<GUIContent> { new GUIContent("None") };
            List<int> valueList = new List<int> { 0 };

            for (int channel = 1; channel <= Mathf.Max(channelCount, currentChannel); channel++)
            {
                string label = channel.ToString();
                string channelName = GetKnownOutputChannelName(channel);
                if (!string.IsNullOrEmpty(channelName)) label += "  " + channelName.Replace("/", "∕");
                if (channel > channelCount) label += "  (doesn't exist on driver)";

                AsioAudioSource user = allSources.FirstOrDefault(source => UsesChannel(source, channel) && !editedSources.Contains(source));
                if (user != null) label += "  (used by " + user.name.Replace("/", "∕") + ")"; // A '/' would create a submenu

                labelList.Add(new GUIContent(label));
                valueList.Add(channel);
            }

            labels = labelList.ToArray();
            values = valueList.ToArray();
        }

        /// <summary>The output channels used by these sources (several per source with Play All Channels).</summary>
        public static HashSet<int> GetUsedChannels(IEnumerable<AsioAudioSource> sources)
        {
            HashSet<int> used = new HashSet<int>();
            foreach (AsioAudioSource source in sources)
            {
                if (source == null || source.TargetOutputChannel <= 0) continue;
                for (int c = 0; c < GetOutputChannelCount(source); c++) used.Add(source.TargetOutputChannel + c);
            }
            return used;
        }

        /// <summary>The lowest output channel from which <paramref name="count"/> consecutive channels are free.</summary>
        public static int FindFreeChannels(HashSet<int> usedChannels, int count)
        {
            for (int first = 1; ; first++)
            {
                bool free = true;
                for (int c = 0; c < count && free; c++) free = !usedChannels.Contains(first + c);
                if (free) return first;
            }
        }

        /// <summary>Gives each source the lowest output channel(s) not used by any other source.</summary>
        public static void AutoAssignChannels(IList<AsioAudioSource> sourcesToAssign, IList<AsioAudioSource> allSources)
        {
            HashSet<int> usedChannels = GetUsedChannels(allSources.Where(source => !sourcesToAssign.Contains(source)));

            foreach (AsioAudioSource source in sourcesToAssign)
            {
                int count = GetOutputChannelCount(source);
                int channel = FindFreeChannels(usedChannels, count);
                RecordChange(source, "Auto-assign Output Channels", () => source.TargetOutputChannel = channel);
                for (int c = 0; c < count; c++) usedChannels.Add(channel + c);
            }
        }

        // ---------- Audio files ----------

        public class AudioFileInfo
        {
            public double LengthSeconds;
            public int SampleRate;
            public int Channels;
            public int BitsPerSample; // 0 when unknown (compressed formats)
        }

        private static readonly Dictionary<string, (DateTime lastWrite, AudioFileInfo info)> _fileInfoCache = new Dictionary<string, (DateTime, AudioFileInfo)>();

        /// <summary>Reads (and caches) the length and format of an audio file. Returns null if the file is missing or unreadable.</summary>
        public static AudioFileInfo GetAudioFileInfo(string resolvedPath)
        {
            if (string.IsNullOrEmpty(resolvedPath) || !File.Exists(resolvedPath)) return null;

            DateTime lastWrite = File.GetLastWriteTimeUtc(resolvedPath);
            if (_fileInfoCache.TryGetValue(resolvedPath, out var cached) && cached.lastWrite == lastWrite) return cached.info;

            AudioFileInfo info = null;
            try
            {
                using (AudioFileReader reader = new AudioFileReader(resolvedPath))
                {
                    info = new AudioFileInfo
                    {
                        LengthSeconds = reader.TotalTime.TotalSeconds,
                        SampleRate = reader.WaveFormat.SampleRate,
                        Channels = reader.WaveFormat.Channels,
                    };
                }
                if (string.Equals(Path.GetExtension(resolvedPath), ".wav", StringComparison.OrdinalIgnoreCase))
                {
                    using (WaveFileReader waveReader = new WaveFileReader(resolvedPath)) info.BitsPerSample = waveReader.WaveFormat.BitsPerSample;
                }
            }
            catch (Exception) { }

            _fileInfoCache[resolvedPath] = (lastWrite, info);
            return info;
        }

        public static string DescribeAudioFile(AudioFileInfo info)
        {
            if (info == null) return null;
            string channels = info.Channels == 1 ? "Mono" : info.Channels == 2 ? "Stereo" : info.Channels + " channels";
            string bits = info.BitsPerSample > 0 ? "  ·  " + info.BitsPerSample + "-bit" : "";
            return FormatTime(info.LengthSeconds) + "  ·  " + (info.SampleRate / 1000f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " kHz" + bits + "  ·  " + channels;
        }

        public static bool IsAudioFile(string path)
        {
            return AudioExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
        }

        public static string ProjectRootPath => Path.GetFullPath(Path.GetDirectoryName(Application.dataPath));

        /// <summary>Converts a path inside the project folder to a project-relative path ("Assets/..."); other paths are returned as absolute paths.</summary>
        public static string ToProjectRelativePath(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string projectRoot = ProjectRootPath + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase)) return fullPath.Substring(projectRoot.Length).Replace('\\', '/');
            return fullPath;
        }

        public static bool IsOutsideProject(string audioFilePath)
        {
            return !string.IsNullOrEmpty(audioFilePath) && Path.IsPathRooted(audioFilePath);
        }

        // ---------- Drawing ----------

        public static string FormatTime(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
            TimeSpan time = TimeSpan.FromSeconds(seconds);
            return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
        }

        public static Color GetStatusColor(AsioAudioStatus status)
        {
            switch (status)
            {
                case AsioAudioStatus.Playing: return PlayingColor;
                case AsioAudioStatus.Paused: return PausedColor;
                default: return StoppedColor;
            }
        }

        public static void DrawDot(Rect rect, Color color)
        {
            float size = Mathf.Min(rect.width, rect.height, 9f);
            Rect dotRect = new Rect(rect.x + (rect.width - size) / 2f, rect.y + (rect.height - size) / 2f, size, size);
            EditorGUI.DrawRect(dotRect, color);
        }

        /// <summary>Draws a peak level meter (from -60 dB to 0 dB).</summary>
        public static void DrawMeter(Rect rect, float linearLevel, bool vertical)
        {
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(0.12f, 0.12f, 0.12f) : new Color(0.55f, 0.55f, 0.55f));

            float decibels = linearLevel > 0.000001f ? 20f * Mathf.Log10(linearLevel) : -120f;
            float fill = Mathf.Clamp01(Mathf.InverseLerp(-60f, 0f, decibels));
            if (fill <= 0f) return;

            Color color = decibels > -3f ? ErrorColor : decibels > -12f ? PausedColor : PlayingColor;
            Rect fillRect = vertical
                ? new Rect(rect.x, rect.yMax - rect.height * fill, rect.width, rect.height * fill)
                : new Rect(rect.x, rect.y, rect.width * fill, rect.height);
            EditorGUI.DrawRect(fillRect, color);
        }

        /// <summary>
        /// Draws a playback progress bar with the time. When interactive, clicking or dragging on it returns the position to seek to (in seconds), otherwise -1.
        /// </summary>
        public static double DrawProgressBar(Rect rect, double currentSeconds, double lengthSeconds, Color fillColor, bool interactive)
        {
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(0.15f, 0.15f, 0.15f) : new Color(0.65f, 0.65f, 0.65f));

            float progress = lengthSeconds > 0 ? Mathf.Clamp01((float)(currentSeconds / lengthSeconds)) : 0f;
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width * progress, rect.height), fillColor * new Color(1f, 1f, 1f, 0.75f));

            GUIStyle labelStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(rect, FormatTime(currentSeconds) + " / " + FormatTime(lengthSeconds), labelStyle);

            if (!interactive || lengthSeconds <= 0) return -1;

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.SlideArrow);
            Event currentEvent = Event.current;
            if ((currentEvent.type == EventType.MouseDown || currentEvent.type == EventType.MouseDrag) && currentEvent.button == 0 && rect.Contains(currentEvent.mousePosition))
            {
                currentEvent.Use();
                return Mathf.Clamp01((currentEvent.mousePosition.x - rect.x) / rect.width) * lengthSeconds;
            }
            return -1;
        }
    }
}
