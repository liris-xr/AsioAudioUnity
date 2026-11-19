using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>The status of one link of the setup.</summary>
    public enum AsioCheckStatus { Ok, Info, Warning, Error, Unknown }

    /// <summary>One check of the Setup Check window: its status, what it found, and an optional fix.</summary>
    public sealed class AsioSetupCheck
    {
        public string Title;
        public AsioCheckStatus Status;
        public string Message;
        /// <summary>Label of the fix button (null: no button).</summary>
        public string ActionLabel;
        public Action Action;
        /// <summary>Documentation page explaining this check, relative to the package's Documentation~ folder (e.g. "Tutorials/Troubleshooting.md#audio-file-not-found"), or a full URL. Null: no help button.</summary>
        public string HelpPage;

        /// <summary>Where the package's documentation is published: help pages are opened there.</summary>
        public const string DocumentationRoot = "https://github.com/liris-xr/AsioAudioUnity/blob/main/Documentation~/";

        /// <summary>Sets the documentation page of this check (see <see cref="HelpPage"/>), and returns it.</summary>
        public AsioSetupCheck Help(string page)
        {
            HelpPage = page;
            return this;
        }

        /// <summary>The URL of the help page (null if none).</summary>
        public string HelpUrl => string.IsNullOrEmpty(HelpPage) ? null : HelpPage.StartsWith("http") ? HelpPage : DocumentationRoot + HelpPage;

        public AsioSetupCheck(string title, AsioCheckStatus status, string message, string actionLabel = null, Action action = null)
        {
            Title = title; Status = status; Message = message; ActionLabel = actionLabel; Action = action;
        }
    }

    /// <summary>
    /// The checks shown by the Setup Check window, by section. The package registers its own; the plugins (Spat OSC, REAPER OSC) register theirs
    /// from their editor assemblies, so the core doesn't depend on them.
    /// </summary>
    public static class AsioSetupChecks
    {
        private static readonly List<(string section, int order, Func<IEnumerable<AsioSetupCheck>> provider)> Providers
            = new List<(string, int, Func<IEnumerable<AsioSetupCheck>>)>();

        /// <summary>Adds a section of checks (sections are shown by increasing order). The provider is called each time the window refreshes.</summary>
        public static void Register(string section, int order, Func<IEnumerable<AsioSetupCheck>> provider)
        {
            Providers.RemoveAll(entry => entry.section == section);
            Providers.Add((section, order, provider));
        }

        private static readonly List<(string name, int order, Func<bool> exists, Func<GameObject> create)> SceneObjects
            = new List<(string, int, Func<bool>, Func<GameObject>)>();

        /// <summary>
        /// Adds an object that AsioAudioUnity > Show Setup creates when the open scenes don't have one yet
        /// (e.g. the plugins' REAPER and Spat Controllers). The create function is called inside Show Setup's Undo group.
        /// </summary>
        public static void RegisterSceneObject(string name, int order, Func<bool> exists, Func<GameObject> create)
        {
            SceneObjects.RemoveAll(entry => entry.name == name);
            SceneObjects.Add((name, order, exists, create));
        }

        /// <summary>
        /// Creates what a show needs and the open scenes don't have yet: the ASIO Audio Settings, an ASIO Audio Source, and the registered objects
        /// (REAPER Controller, Spat Controller), then opens the Setup Check window.
        /// </summary>
        [MenuItem("AsioAudioUnity/Show Setup", false, 43)]
        public static void CreateShowSetup()
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Create Show Setup");
            int undoGroup = Undo.GetCurrentGroup();
            List<string> created = new List<string>(), existing = new List<string>();
            List<UnityEngine.Object> createdObjects = new List<UnityEngine.Object>();

            if (!AsioAudioSettings.HasSettingsAsset)
            {
                AsioAudioSettingsProvider.GetOrCreateSettingsAsset();
                created.Add("ASIO Audio Settings");
            }

            if (AsioEditorUtility.FindAllSources().Length == 0)
            {
                createdObjects.Add(GlobalAsioAudioEditor.CreateAsioAudioSourceInScene(new MenuCommand(null)).gameObject);
                created.Add("ASIO Audio Source");
            }
            else existing.Add("ASIO Audio Source");

            foreach ((string name, int order, Func<bool> exists, Func<GameObject> create) in SceneObjects.OrderBy(entry => entry.order))
            {
                if (exists())
                {
                    existing.Add(name);
                    continue;
                }
                GameObject go = create();
                if (go != null) createdObjects.Add(go);
                created.Add(name);
            }

            Undo.CollapseUndoOperations(undoGroup);
            if (createdObjects.Count > 0) Selection.objects = createdObjects.ToArray();
            Debug.Log("Show Setup: " + (created.Count > 0 ? "created " + string.Join(", ", created) : "nothing to create")
                + (existing.Count > 0 ? " (already there: " + string.Join(", ", existing) + ")" : "") + ". Check the setup in the Setup Check window.");
            AsioSetupCheckWindow.Open();
        }

        internal static IEnumerable<(string section, List<AsioSetupCheck> checks)> Run()
        {
            foreach ((string section, int order, Func<IEnumerable<AsioSetupCheck>> provider) in Providers.OrderBy(entry => entry.order))
            {
                List<AsioSetupCheck> checks;
                try { checks = provider().Where(check => check != null).ToList(); }
                catch (Exception e) { checks = new List<AsioSetupCheck> { new AsioSetupCheck("Check failed", AsioCheckStatus.Unknown, e.GetBaseException().Message) }; }
                if (checks.Count > 0) yield return (section, checks);
            }
        }
    }

    /// <summary>
    /// Shows the state of each link of the setup at a glance (ASIO driver, connection, sample rate, sources, REAPER, Spat Revolution),
    /// with a fix button where possible. Refreshes by itself.
    /// </summary>
    public class AsioSetupCheckWindow : EditorWindow
    {
        private Vector2 _scroll;
        private double _nextRefresh;
        private List<(string section, List<AsioSetupCheck> checks)> _results = new List<(string, List<AsioSetupCheck>)>();
        private GUIStyle _titleStyle, _messageStyle, _sectionStyle;

        [MenuItem("AsioAudioUnity/Setup Check", false, 42)]
        public static void Open()
        {
            AsioSetupCheckWindow window = GetWindow<AsioSetupCheckWindow>();
            window.titleContent = new GUIContent("ASIO Setup Check");
            window.minSize = new Vector2(380f, 260f);
            window.Show();
        }

        private void OnEnable() => Refresh();

        private void OnInspectorUpdate()
        {
            if (AsioChannelTest.IsRunning) Repaint();
            if (EditorApplication.timeSinceStartup < _nextRefresh) return;
            Refresh();
            Repaint();
        }

        private void Refresh()
        {
            _results = AsioSetupChecks.Run().ToList();
            _nextRefresh = EditorApplication.timeSinceStartup + (EditorApplication.isPlaying ? 0.5 : 2.0);
        }

        private void OnGUI()
        {
            EnsureStyles();
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(EditorApplication.isPlaying ? "Play mode: live checks" : "Edit mode: the connection is checked in Play mode", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            AsioChannelTest.DrawToolbarButton();
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton)) Refresh();
            if (GUILayout.Button("Settings", EditorStyles.toolbarButton)) AsioEditorUtility.OpenSettings();
            EditorGUILayout.EndHorizontal();
            AsioChannelTest.DrawStatus();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach ((string section, List<AsioSetupCheck> checks) in _results)
            {
                EditorGUILayout.Space(4f);
                GUILayout.Label(section, _sectionStyle);
                foreach (AsioSetupCheck check in checks) DrawCheck(check);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawCheck(AsioSetupCheck check)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            Rect dot = GUILayoutUtility.GetRect(14f, 18f, GUILayout.Width(14f));
            AsioEditorUtility.DrawDot(dot, ColorOf(check.Status));
            EditorGUILayout.BeginVertical();
            GUILayout.Label(check.Title, _titleStyle);
            if (!string.IsNullOrEmpty(check.Message)) GUILayout.Label(check.Message, _messageStyle);
            EditorGUILayout.EndVertical();
            if (check.HelpUrl != null && GUILayout.Button(new GUIContent("?", "Open the documentation about this: " + check.HelpPage), EditorStyles.miniButton, GUILayout.Width(22f)))
            {
                Application.OpenURL(check.HelpUrl);
            }
            if (check.Action != null && !string.IsNullOrEmpty(check.ActionLabel)
                && GUILayout.Button(check.ActionLabel, EditorStyles.miniButton, GUILayout.ExpandWidth(false), GUILayout.MinWidth(70f)))
            {
                check.Action();
                Refresh();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
        }

        private static Color ColorOf(AsioCheckStatus status)
        {
            switch (status)
            {
                case AsioCheckStatus.Ok: return AsioEditorUtility.PlayingColor;
                case AsioCheckStatus.Warning: return AsioEditorUtility.PausedColor;
                case AsioCheckStatus.Error: return AsioEditorUtility.ErrorColor;
                case AsioCheckStatus.Info: return new Color(0.4f, 0.6f, 0.95f);
                default: return AsioEditorUtility.StoppedColor;
            }
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null) return;
            _titleStyle = new GUIStyle(EditorStyles.boldLabel) { wordWrap = true };
            _messageStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
            _sectionStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
        }
    }

    /// <summary>The package's own checks: ASIO driver and connection, sources of the open scenes, Unity audio fallback.</summary>
    [InitializeOnLoad]
    internal static class AsioCoreSetupChecks
    {
        static AsioCoreSetupChecks()
        {
            AsioSetupChecks.Register("ASIO driver", 0, DriverChecks);
            AsioSetupChecks.Register("Sources (open scenes)", 10, SourceChecks);
        }

        private static IEnumerable<AsioSetupCheck> DriverChecks()
        {
            if (!AsioAudioSettings.HasSettingsAsset)
            {
                yield return new AsioSetupCheck("No ASIO Audio Settings", AsioCheckStatus.Error, "The project has no ASIO settings yet.",
                    "Create", () => { AsioAudioSettingsProvider.GetOrCreateSettingsAsset(); AsioEditorUtility.OpenSettings(); }).Help("Tutorials/4-FirstPlay.md");
                yield break;
            }

            AsioAudioSettings settings = AsioEditorUtility.Settings;
            string driver = settings.AsioDriverName;
            bool fallback = settings.FallBackToUnityAudio;
            if (string.IsNullOrEmpty(driver))
            {
                yield return new AsioSetupCheck("No ASIO driver chosen", fallback ? AsioCheckStatus.Warning : AsioCheckStatus.Error,
                    fallback ? "The sources will play through Unity's audio (all channels mixed)." : "Nothing will play.", "Choose", AsioEditorUtility.OpenSettings)
                    .Help("Tutorials/Troubleshooting.md#no-asio-driver-is-set--driver-not-found");
            }
            else if (!AsioEditorUtility.GetDriverNames().Contains(driver))
            {
                yield return new AsioSetupCheck("Driver \"" + driver + "\" isn't installed on this computer", fallback ? AsioCheckStatus.Warning : AsioCheckStatus.Error,
                    (fallback ? "The sources will play through Unity's audio. " : "") + "Installed: " + string.Join(", ", AsioEditorUtility.GetDriverNames()) + ".",
                    "Choose", AsioEditorUtility.OpenSettings).Help("Tutorials/Troubleshooting.md#no-asio-driver-is-set--driver-not-found");
            }
            else
            {
                yield return new AsioSetupCheck("Driver: " + driver, AsioCheckStatus.Ok, null);
            }

            // Connection and audio processing (Play mode)
            AsioAudioManager manager = AsioAudioManager.Existing;
            if (!EditorApplication.isPlaying)
            {
                string rate = settings.TargetSampleRate + " Hz" + (settings.MatchDriverSampleRate ? " (or the driver's own rate, with Match Driver)" : "");
                yield return new AsioSetupCheck("Sample rate: " + rate, AsioCheckStatus.Info,
                    settings.MatchDriverSampleRate ? "The rate in use is shown here in Play mode." : "Must be supported by the driver (ReaRoute: REAPER's rate).")
                    .Help("Reference/AsioAudioSettings.md#match-driver");
                yield return new AsioSetupCheck("Connection", AsioCheckStatus.Unknown, "Checked in Play mode.");
                yield break;
            }
            if (manager == null)
            {
                yield return new AsioSetupCheck("Connection", AsioCheckStatus.Unknown, "No ASIO Audio Source has been enabled yet.");
                yield break;
            }

            if (manager.IsConnected)
            {
                yield return new AsioSetupCheck("Connected: " + manager.AsioDriverOutputChannelCount + " outputs, " + manager.AsioDriverSampleFormat, AsioCheckStatus.Ok, null);
                bool matched = manager.OutputSampleRate != settings.TargetSampleRate;
                yield return new AsioSetupCheck("Sample rate: " + manager.OutputSampleRate + " Hz", matched ? AsioCheckStatus.Info : AsioCheckStatus.Ok,
                    matched ? "The driver doesn't support " + settings.TargetSampleRate + " Hz: its own rate is used (Match Driver)." : null)
                    .Help("Reference/AsioAudioSettings.md#match-driver");

                float elapsed = Time.realtimeSinceStartup - manager.ConnectedAt;
                if (manager.RenderedFrames > 0)
                    yield return new AsioSetupCheck("The driver processes audio", AsioCheckStatus.Ok, null);
                else if (elapsed > 2f)
                    yield return new AsioSetupCheck("The driver isn't asking for audio", AsioCheckStatus.Error,
                        "Nothing can be heard. With ReaRoute, REAPER must be running with its audio device open: turn off \"Close audio device when stopped and application "
                        + "is inactive\" (REAPER Preferences > Audio), and press Play once in REAPER. With another driver, check that no other application holds it.")
                        .Help("Tutorials/Troubleshooting.md#the-sources-show-playing-but-nothing-is-heard");
                else
                    yield return new AsioSetupCheck("Waiting for the driver…", AsioCheckStatus.Unknown, null);
            }
            else if (manager.IsUsingUnityAudio)
            {
                yield return new AsioSetupCheck("Playing through Unity's audio (no ASIO driver)", AsioCheckStatus.Warning,
                    "All channels are mixed together, at " + manager.OutputSampleRate + " Hz. The ASIO setup is used on a computer where the driver is available.")
                    .Help("Reference/AsioAudioSettings.md#fall-back-to-unity-audio");
                if (UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length == 0)
                    yield return new AsioSetupCheck("No Audio Listener in the scene", AsioCheckStatus.Error, "Unity's audio output can't be heard without one (e.g. on the camera).")
                        .Help("Reference/AsioAudioSettings.md#fall-back-to-unity-audio");
                if (IsUnityAudioDisabled())
                    yield return new AsioSetupCheck("Unity's audio is disabled", AsioCheckStatus.Error, "Project Settings > Audio > Disable Unity Audio is on.",
                        "Open", () => SettingsService.OpenProjectSettings("Project/Audio")).Help("Reference/AsioAudioSettings.md#fall-back-to-unity-audio");
            }
            else
            {
                yield return new AsioSetupCheck("Not connected", AsioCheckStatus.Error, "The driver couldn't start: see the Console (sample rate, or another application using it).",
                    "Reconnect", manager.Reconnect).Help("Tutorials/Troubleshooting.md#could-not-start-the-asio-driver");
            }
        }

        private static IEnumerable<AsioSetupCheck> SourceChecks()
        {
            AsioAudioSource[] sources = AsioEditorUtility.FindAllSources();
            if (sources.Length == 0)
            {
                yield return new AsioSetupCheck("No ASIO Audio Source", AsioCheckStatus.Warning, "The open scenes have no ASIO Audio Source.",
                    "Create", () => EditorApplication.ExecuteMenuItem("GameObject/AsioAudioUnity/ASIO Audio Source")).Help("Tutorials/5-AddSources.md");
                yield break;
            }

            int outputs = AsioEditorUtility.GetKnownOutputChannelCount();
            AsioAudioSource[] conflicts = sources.Where(source => AsioEditorUtility.GetChannelIssue(source, sources, outputs) != AsioEditorUtility.ChannelIssue.None).ToArray();
            yield return conflicts.Length == 0
                ? new AsioSetupCheck(sources.Length + " source(s), each on its own channel", AsioCheckStatus.Ok, null)
                : new AsioSetupCheck(conflicts.Length + " source(s) with a channel problem", AsioCheckStatus.Error,
                    string.Join(", ", conflicts.Select(source => "\"" + source.name + "\" (" + AsioEditorUtility.DescribeChannels(source) + ")")),
                    "Fix", () => AsioEditorUtility.AutoAssignChannels(conflicts, sources)).Help("Tutorials/Troubleshooting.md#channel-conflict--no-such-channel");

            AsioAudioSource[] missing = sources.Where(source => string.IsNullOrEmpty(source.AudioFilePath) || !File.Exists(source.ResolvedAudioFilePath)).ToArray();
            if (missing.Length > 0)
            {
                yield return new AsioSetupCheck(missing.Length + " source(s) without a playable file", AsioCheckStatus.Error,
                    string.Join(", ", missing.Select(source => "\"" + source.name + "\"")), "Select", () => Selection.objects = missing.Select(source => (UnityEngine.Object)source.gameObject).ToArray())
                    .Help("Tutorials/Troubleshooting.md#audio-file-not-found");
            }
        }

        private static bool IsUnityAudioDisabled()
        {
            UnityEngine.Object audioManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset").FirstOrDefault();
            if (audioManager == null) return false;
            SerializedProperty disabled = new SerializedObject(audioManager).FindProperty("m_DisableAudio");
            return disabled != null && disabled.boolValue;
        }
    }
}
