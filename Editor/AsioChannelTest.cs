using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// Test Channels (Setup Check and ASIO Audio Mixer toolbars): a short beep on each ASIO Audio Source's output channels in turn, one source per second,
    /// to check where each one arrives (its REAPER track, its Spat Revolution source). In Play mode through the ASIO Audio Manager, in Edit mode through the
    /// project's ASIO driver.
    /// </summary>
    [InitializeOnLoad]
    internal static class AsioChannelTest
    {
        private static AsioTestTone _tone;
        private static string[] _labels = new string[0];
        private static bool _inPlayMode;

        /// <summary>Why the last test couldn't start, or a note about it; null if none.</summary>
        public static string Message { get; private set; }
        private static MessageType _messageType;

        public static bool IsRunning => _tone != null && !_tone.IsFinished;

        static AsioChannelTest()
        {
            // The Play mode tone belongs to the ASIO Audio Manager, the Edit mode one to the editor's driver: neither survives the switch.
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode) Stop();
            };
        }

        public static void Start()
        {
            Stop();
            Message = null;

            AsioAudioSource[] sources = Object.FindObjectsByType<AsioAudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(source => source.TargetOutputChannel > 0)
                .OrderBy(source => source.TargetOutputChannel).ThenBy(source => source.name).ToArray();
            if (sources.Length == 0)
            {
                SetMessage("No ASIO Audio Source with an output channel in the open scenes: nothing to test.", MessageType.Info);
                return;
            }

            List<int[]> steps = new List<int[]>();
            List<string> labels = new List<string>();
            foreach (AsioAudioSource source in sources)
            {
                int first = source.TargetOutputChannel;
                int count = Mathf.Max(1, AsioEditorUtility.GetOutputChannelCount(source));
                steps.Add(Enumerable.Range(first, count).ToArray());
                labels.Add("\"" + source.name + "\", " + (count > 1 ? "channels " + first + "-" + (first + count - 1) : "channel " + first));
            }

            if (EditorApplication.isPlaying)
            {
                AsioAudioManager manager = AsioAudioManager.Existing;
                _tone = manager != null ? manager.PlayTestTone(steps) : null;
                if (_tone == null)
                {
                    SetMessage("The ASIO Audio Manager isn't connected: nothing can be played (see the Console).", MessageType.Warning);
                    return;
                }
                if (manager.IsUsingUnityAudio)
                {
                    SetMessage("Unity's audio fallback mixes every channel together: the beeps can't tell the channels apart.", MessageType.Warning);
                }
            }
            else
            {
                _tone = AsioEditorPreview.StartTestTone(steps);
                if (_tone == null)
                {
                    SetMessage(AsioEditorPreview.LastError, MessageType.Warning);
                    return;
                }
            }
            _inPlayMode = EditorApplication.isPlaying;
            _labels = labels.ToArray();
        }

        public static void Stop()
        {
            if (_tone == null) return;
            if (_inPlayMode) AsioAudioManager.Existing?.StopTestTone();
            else if (AsioEditorPreview.TestTone == _tone) AsioEditorPreview.Stop();
            _tone.Stop();
            _tone = null;
        }

        private static void SetMessage(string message, MessageType type)
        {
            Message = message;
            _messageType = type;
        }

        /// <summary>The Test Channels / Stop Test button, for a toolbar.</summary>
        public static void DrawToolbarButton()
        {
            if (IsRunning)
            {
                if (GUILayout.Button(new GUIContent("Stop Test", "Stop the test beeps."), EditorStyles.toolbarButton)) Stop();
            }
            else if (GUILayout.Button(new GUIContent("Test Channels", "Play a short beep on each ASIO Audio Source's output channels in turn, one source per second, "
                + "to check that each one arrives on its REAPER track and its Spat Revolution source."), EditorStyles.toolbarButton))
            {
                Start();
            }
        }

        /// <summary>Which source is beeping, and the last message; draws nothing when there's neither.</summary>
        public static void DrawStatus()
        {
            if (IsRunning)
            {
                int step = _tone.CurrentStep;
                string label = step >= 0 && step < _labels.Length ? _labels[step] : "";
                EditorGUILayout.HelpBox("Beep " + (step + 1) + "/" + _tone.StepCount + ": " + label
                    + ". Its REAPER track and Spat Revolution source should show a level now.", MessageType.Info);
            }
            if (!string.IsNullOrEmpty(Message))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.HelpBox(Message, _messageType);
                if (GUILayout.Button(new GUIContent("×", "Dismiss"), EditorStyles.miniButton, GUILayout.Width(20f))) Message = null;
                EditorGUILayout.EndHorizontal();
            }
        }
    }
}
