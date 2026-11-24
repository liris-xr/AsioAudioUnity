using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AsioAudioUnity
{
    [CustomEditor(typeof(ReaperController))]
    public class ReaperControllerEditor : Editor
    {
        private static bool _showAddresses;

        private SerializedProperty _reaperAddress;
        private SerializedProperty _reaperPort;
        private SerializedProperty _followTransport;
        private SerializedProperty _stopReaperOnQuit;
        private SerializedProperty _followReaper;
        private SerializedProperty _listenPort;
        private SerializedProperty _playRegionScriptId;
        private SerializedProperty _setupTracksScriptId;
        private SerializedProperty _numberTrackNames;
        private SerializedProperty _commands;
        private SerializedProperty _addresses;
        private SerializedProperty _logMessages;

        private string _installMessage;
        private MessageType _installMessageType;
        private GUIStyle _cardStyle, _titleStyle;

        private ReaperController Controller => (ReaperController)target;

        private void OnEnable()
        {
            _reaperAddress = serializedObject.FindProperty("_reaperAddress");
            _reaperPort = serializedObject.FindProperty("_reaperPort");
            _followTransport = serializedObject.FindProperty("_followTransport");
            _stopReaperOnQuit = serializedObject.FindProperty("_stopReaperOnQuit");
            _followReaper = serializedObject.FindProperty("_followReaper");
            _listenPort = serializedObject.FindProperty("_listenPort");
            _playRegionScriptId = serializedObject.FindProperty("_playRegionScriptId");
            _setupTracksScriptId = serializedObject.FindProperty("_setupTracksScriptId");
            _numberTrackNames = serializedObject.FindProperty("_numberTrackNames");
            _commands = serializedObject.FindProperty("_commands");
            _addresses = serializedObject.FindProperty("_addresses");
            _logMessages = serializedObject.FindProperty("_logMessages");
        }

        public override void OnInspectorGUI()
        {
            EnsureStyles();
            serializedObject.Update();

            DrawConnectionCard();
            DrawUnityCard();
            DrawScriptsCard();
            DrawCommandsCard();

            EditorGUILayout.BeginVertical(_cardStyle);
            _showAddresses = AsioEditorGUI.CardFoldout(_showAddresses, new GUIContent("OSC Addresses", "The addresses of REAPER's Default OSC pattern configuration. Change them only if your control surface uses another pattern file."), _titleStyle);
            if (_showAddresses)
            {
                foreach (SerializedProperty child in Children(_addresses)) EditorGUILayout.PropertyField(child);
            }
            EditorGUILayout.PropertyField(_logMessages);
            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }

        // ---------- Cards ----------

        private void DrawConnectionCard()
        {
            EditorGUILayout.BeginVertical(_cardStyle);
            GUILayout.Label("REAPER", _titleStyle);
            EditorGUILayout.PropertyField(_reaperAddress, new GUIContent("Address", "IP address of the computer running REAPER (127.0.0.1: this computer)."));
            EditorGUILayout.PropertyField(_reaperPort, new GUIContent("Port", "The \"Local listen port\" of REAPER's OSC control surface (8000 by default)."));
            EditorGUILayout.HelpBox("In REAPER: Preferences > Control/OSC/web > Add > OSC (Open Sound Control). Mode: \"Local port\" (or \"Configure device IP + local port\"), "
                + "Local listen port: " + _reaperPort.intValue + ", Pattern config: Default.", MessageType.None);
            EditorGUILayout.EndVertical();
        }

        private void DrawUnityCard()
        {
            EditorGUILayout.BeginVertical(_cardStyle);
            GUILayout.Label("Transport", _titleStyle);
            EditorGUILayout.PropertyField(_followTransport, new GUIContent("Follow Transport", "REAPER follows Unity: PlayAll plays REAPER, PauseAll pauses it, StopAll stops it, RestartAll plays it from the start of the project. When the scene starts with sources playing (Play On Enable), REAPER plays from the start of the project too."));
            EditorGUILayout.PropertyField(_stopReaperOnQuit, new GUIContent("Stop REAPER On Quit", "Stops REAPER when the application quits, or when leaving Play mode."));
            EditorGUILayout.PropertyField(_followReaper, new GUIContent("Follow REAPER", "Unity follows REAPER: when REAPER is played, paused or stopped (by its operator, a control surface...), Unity's sources follow (PlayAll, PauseAll, StopAll)."));
            if (_followReaper.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_listenPort, new GUIContent("Listen Port", "The port REAPER sends its feedback to: the \"Device port\" of its OSC control surface."));
                EditorGUI.indentLevel--;
                EditorGUILayout.HelpBox("In REAPER's OSC control surface: Mode \"Configure device IP + local port\", Device IP: the IP address of this computer (127.0.0.1 if REAPER runs here), "
                    + "Device port: " + _listenPort.intValue + ".", MessageType.None);
                ReaperController controller = (ReaperController)target;
                if (EditorApplication.isPlaying)
                {
                    if (controller.IsListening) EditorGUILayout.LabelField("Listening to REAPER on port " + _listenPort.intValue + ".", EditorStyles.miniLabel);
                    else if (!string.IsNullOrEmpty(controller.ListenError)) EditorGUILayout.HelpBox("Can't listen on port " + _listenPort.intValue + ": " + controller.ListenError, MessageType.Warning);
                }
            }
            EditorGUILayout.EndVertical();
        }

        // When RegisterScripts.lua's file was last looked at, and whether it gave other IDs (checked once per second, not on every repaint).
        private double _nextIdsCheck;
        private bool _idsDiffer;
        private System.Collections.Generic.List<string> _outdatedScripts = new System.Collections.Generic.List<string>();

        // Fills the empty script ID fields from RegisterScripts.lua's file, and notes whether the others differ from it.
        private void CheckRegisteredScriptIds()
        {
            if (EditorApplication.timeSinceStartup < _nextIdsCheck) return;
            _nextIdsCheck = EditorApplication.timeSinceStartup + 1.0;
            _outdatedScripts = ReaperScriptInstaller.OutdatedScripts();
            if (!File.Exists(ReaperController.RegisteredScriptIdsPath))
            {
                _idsDiffer = false;
                return;
            }
            if (string.IsNullOrWhiteSpace(_playRegionScriptId.stringValue) || string.IsNullOrWhiteSpace(_setupTracksScriptId.stringValue))
            {
                serializedObject.ApplyModifiedProperties();
                Undo.RecordObject(Controller, "Fill Script IDs from REAPER");
                if (Controller.FillScriptIdsFromReaper(true) > 0)
                {
                    EditorUtility.SetDirty(Controller);
                    Debug.Log("REAPER Controller: script IDs filled in from " + ReaperController.RegisteredScriptIdsPath + " (written by RegisterScripts.lua).", Controller);
                }
                serializedObject.Update();
            }
            _idsDiffer = Controller.ScriptIdsDifferFromReaper();
        }

        private void DrawScriptsCard()
        {
            CheckRegisteredScriptIds();
            EditorGUILayout.BeginVertical(_cardStyle);
            GUILayout.Label("Lua Scripts", _titleStyle);
            EditorGUILayout.PropertyField(_playRegionScriptId, new GUIContent("Play Region Script ID",
                "Command ID of the PlayRegion.lua script in REAPER (e.g. _RS7d3c...). Play Region commands run it with the region number, and it plays the region in loop. Empty: they go to the region and play from there, without looping."));
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(_setupTracksScriptId, new GUIContent("Set Up Tracks Script ID",
                "Command ID of the SetupTracks.lua script in REAPER. Set Up REAPER Tracks runs it with the list of ASIO Audio Sources, and it creates or updates one track per source."));
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_setupTracksScriptId.stringValue)))
            {
                if (GUILayout.Button(new GUIContent("Set Up REAPER Tracks", "Create or update one REAPER track per ASIO Audio Source of the open scenes: named like the source, "
                    + "on its ReaRoute input, armed with monitoring, without a master send, with the Spat Revolution Send plugin. Run it again after adding or changing sources."), EditorStyles.miniButton, GUILayout.Width(140f)))
                {
                    serializedObject.ApplyModifiedProperties();
                    UpdateOutdatedScripts(); // REAPER runs its installed copy of SetupTracks.lua
                    ReaperController controller = (ReaperController)target;
                    controller.SetUpReaperTracks(FindObjectsByType<AsioAudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                        .Where(source => source.TargetOutputChannel > 0)
                        .Select(source => (source.TargetOutputChannel, AsioEditorGUI.GetOutputChannelCount(source), source.SpatNumber, source.name)));
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.PropertyField(_numberTrackNames, new GUIContent("Number Track Names",
                "Set Up REAPER Tracks starts each track's name with the number of its Spat Revolution source (its output channel without a Spat Controller), e.g. \"3 · Violin\"."));

            if (_idsDiffer)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.HelpBox("RegisterScripts.lua gave other command IDs than these (REAPER reinstalled, or another computer?).", MessageType.Warning);
                if (GUILayout.Button(new GUIContent("Fill from REAPER", "Use the command IDs written by RegisterScripts.lua (" + ReaperController.RegisteredScriptIdsPath + ")."), GUILayout.Width(110f), GUILayout.Height(38f)))
                {
                    FillFromReaper();
                }
                EditorGUILayout.EndHorizontal();
            }

            if (_outdatedScripts.Count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.HelpBox("The scripts installed in REAPER are older than the package's (" + string.Join(", ", _outdatedScripts) + "): REAPER runs the installed ones.", MessageType.Warning);
                if (GUILayout.Button(new GUIContent("Update", "Copy the package's scripts to " + ReaperScriptInstaller.InstalledFolder + ". The command IDs stay the same."), GUILayout.Width(110f), GUILayout.Height(38f)))
                {
                    UpdateOutdatedScripts();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Show Scripts", "Open the folder of the package's REAPER Lua scripts."), EditorStyles.miniButtonLeft))
            {
                string folder = ReaperScriptInstaller.PackageFolder;
                if (folder != null) EditorUtility.RevealInFinder(Directory.GetFiles(folder, "*.lua").FirstOrDefault() ?? folder);
            }
            if (GUILayout.Button(new GUIContent("Install into REAPER", "Copy the scripts to REAPER's Scripts folder (%APPDATA%\\REAPER\\Scripts\\AsioAudioUnity)."), EditorStyles.miniButtonMid))
            {
                InstallScripts();
            }
            using (new EditorGUI.DisabledScope(!File.Exists(ReaperController.RegisteredScriptIdsPath)))
            {
                if (GUILayout.Button(new GUIContent("Fill from REAPER", "Set the script IDs from the file RegisterScripts.lua writes (" + ReaperController.RegisteredScriptIdsPath + "). Empty fields are filled by themselves."), EditorStyles.miniButtonRight))
                {
                    FillFromReaper();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (_installMessage != null) EditorGUILayout.HelpBox(_installMessage, _installMessageType);
            EditorGUILayout.LabelField("Then in REAPER, once: Actions > Show action list > New action > Load ReaScript..., select RegisterScripts.lua, and click Run. It loads the other scripts, and the IDs above fill in by themselves. "
                + "(By hand: load each script, right-click it > Copy selected action command ID, and paste it above.) Your own scripts work the same way, in Run Action (With Value) commands.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawCommandsCard()
        {
            EditorGUILayout.BeginVertical(_cardStyle);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Commands", "Sent automatically on their trigger, from code or a UnityEvent with ReaperController.Trigger(\"name\"), or with Send."), _titleStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ Add Command", EditorStyles.miniButton, GUILayout.Width(100f)))
            {
                _commands.arraySize++;
                SerializedProperty added = _commands.GetArrayElementAtIndex(_commands.arraySize - 1);
                added.FindPropertyRelative("Name").stringValue = "Command " + _commands.arraySize;
                added.FindPropertyRelative("Trigger").enumValueIndex = (int)ReaperCommandTrigger.Manual;
                added.FindPropertyRelative("Source").objectReferenceValue = null;
                added.FindPropertyRelative("Type").enumValueIndex = (int)ReaperCommandType.Play;
                added.FindPropertyRelative("Number").floatValue = 0f;
                added.FindPropertyRelative("Text").stringValue = "";
            }
            EditorGUILayout.EndHorizontal();

            if (_commands.arraySize == 0)
            {
                EditorGUILayout.LabelField("No command. Add one to run REAPER actions, Lua scripts, go to regions or markers... on Unity events.", EditorStyles.wordWrappedMiniLabel);
            }

            for (int i = 0; i < _commands.arraySize; i++)
            {
                if (DrawCommand(_commands.GetArrayElementAtIndex(i), i))
                {
                    _commands.DeleteArrayElementAtIndex(i);
                    break;
                }
            }
            EditorGUILayout.EndVertical();
        }

        // Returns true when the command should be removed.
        private bool DrawCommand(SerializedProperty command, int index)
        {
            SerializedProperty commandName = command.FindPropertyRelative("Name");
            SerializedProperty trigger = command.FindPropertyRelative("Trigger");
            SerializedProperty source = command.FindPropertyRelative("Source");
            SerializedProperty type = command.FindPropertyRelative("Type");
            SerializedProperty number = command.FindPropertyRelative("Number");
            SerializedProperty text = command.FindPropertyRelative("Text");
            bool remove = false;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(commandName, GUIContent.none);
            if (GUILayout.Button(new GUIContent("Send", "Send this command to REAPER now (also works in Edit mode)."), EditorStyles.miniButtonLeft, GUILayout.Width(44f)))
            {
                serializedObject.ApplyModifiedProperties();
                Controller.Trigger(index);
            }
            if (GUILayout.Button(new GUIContent("×", "Remove this command"), EditorStyles.miniButtonRight, GUILayout.Width(22f))) remove = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(trigger, new GUIContent("When"));
            ReaperCommandTrigger triggerValue = (ReaperCommandTrigger)trigger.enumValueIndex;
            bool usesSource = triggerValue == ReaperCommandTrigger.SourcePlay || triggerValue == ReaperCommandTrigger.SourcePause || triggerValue == ReaperCommandTrigger.SourceStop;
            if (usesSource)
            {
                EditorGUILayout.PropertyField(source, new GUIContent("Source"));
                if (source.objectReferenceValue == null) EditorGUILayout.HelpBox("Choose the ASIO Audio Source whose event sends this command.", MessageType.Warning);
            }

            EditorGUILayout.PropertyField(type, new GUIContent("Do"));
            switch ((ReaperCommandType)type.enumValueIndex)
            {
                case ReaperCommandType.GoToTime:
                    EditorGUILayout.PropertyField(number, new GUIContent("Time (s)"));
                    break;
                case ReaperCommandType.GoToMarker:
                    IntField(number, new GUIContent("Marker Number", "The marker's number, as shown in REAPER."));
                    break;
                case ReaperCommandType.GoToRegion:
                    IntField(number, new GUIContent("Region Number", "The region's number, as shown in REAPER."));
                    break;
                case ReaperCommandType.PlayRegion:
                    IntField(number, new GUIContent("Region Number", "The region's number, as shown in REAPER."));
                    if (string.IsNullOrWhiteSpace(_playRegionScriptId.stringValue))
                    {
                        EditorGUILayout.HelpBox("Set the Play Region Script ID (Lua Scripts above) to play the region in loop with PlayRegion.lua. Without it, REAPER goes to the region and plays on from there.", MessageType.Info);
                    }
                    break;
                case ReaperCommandType.RunAction:
                    EditorGUILayout.PropertyField(text, new GUIContent("Command ID", "A number (e.g. 40042) or a named ID (e.g. _RS7d3c... for a script). In REAPER's action list: right-click > Copy selected action command ID."));
                    break;
                case ReaperCommandType.RunActionWithValue:
                    EditorGUILayout.PropertyField(text, new GUIContent("Command ID", "The script's named command ID (e.g. _RS7d3c...). In REAPER's action list: right-click > Copy selected action command ID."));
                    EditorGUILayout.PropertyField(number, new GUIContent("Value", "Sent as the OSC argument: the script reads it from reaper.get_action_context() (\":s=value\")."));
                    break;
                case ReaperCommandType.CustomMessage:
                    EditorGUILayout.PropertyField(text, new GUIContent("OSC Address", "e.g. /track/1/mute"));
                    EditorGUILayout.PropertyField(number, new GUIContent("Value"));
                    break;
            }

            EditorGUILayout.EndVertical();
            return remove;
        }

        private static void IntField(SerializedProperty floatProperty, GUIContent label)
        {
            EditorGUI.BeginChangeCheck();
            int value = EditorGUILayout.IntField(label, Mathf.RoundToInt(floatProperty.floatValue));
            if (EditorGUI.EndChangeCheck()) floatProperty.floatValue = Mathf.Max(0, value);
        }

        // ---------- Lua scripts ----------

        private void InstallScripts()
        {
            try
            {
                int count = ReaperScriptInstaller.Install();
                _outdatedScripts.Clear();
                SetInstallMessage(count + " script(s) copied to " + ReaperScriptInstaller.InstalledFolder + ". Now run RegisterScripts.lua once in REAPER (see below).", MessageType.Info);
                EditorUtility.RevealInFinder(Directory.GetFiles(ReaperScriptInstaller.InstalledFolder, "*.lua").FirstOrDefault() ?? ReaperScriptInstaller.InstalledFolder);
            }
            catch (Exception e)
            {
                SetInstallMessage("Could not copy the scripts: " + e.Message, e is DirectoryNotFoundException ? MessageType.Warning : MessageType.Error);
            }
        }

        // Updates the scripts installed in REAPER when they differ from the package's (same files, so the command IDs don't change).
        private void UpdateOutdatedScripts()
        {
            System.Collections.Generic.List<string> outdated = ReaperScriptInstaller.OutdatedScripts();
            if (outdated.Count == 0) return;
            try
            {
                ReaperScriptInstaller.Install();
                _outdatedScripts.Clear();
                string message = "Updated the scripts installed in REAPER: " + string.Join(", ", outdated) + ".";
                Debug.Log("AsioAudioUnity: " + message);
                SetInstallMessage(message, MessageType.Info);
            }
            catch (Exception e)
            {
                SetInstallMessage("Could not update the scripts installed in REAPER: " + e.Message, MessageType.Error);
            }
        }

        private void FillFromReaper()
        {
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(Controller, "Fill Script IDs from REAPER");
            int changed = Controller.FillScriptIdsFromReaper();
            EditorUtility.SetDirty(Controller);
            serializedObject.Update();
            _nextIdsCheck = 0;
            SetInstallMessage(changed > 0 ? changed + " script ID(s) set from RegisterScripts.lua." : "The script IDs already match RegisterScripts.lua's.", MessageType.Info);
        }

        private void SetInstallMessage(string message, MessageType type)
        {
            _installMessage = message;
            _installMessageType = type;
        }

        // ---------- Helpers ----------

        private static System.Collections.Generic.IEnumerable<SerializedProperty> Children(SerializedProperty property)
        {
            SerializedProperty iterator = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            if (!iterator.NextVisible(true)) yield break;
            while (!SerializedProperty.EqualContents(iterator, end))
            {
                yield return iterator.Copy();
                if (!iterator.NextVisible(false)) yield break;
            }
        }

        private void EnsureStyles()
        {
            if (_cardStyle != null) return;
            _cardStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(10, 10, 8, 10), margin = new RectOffset(0, 0, 6, 0) };
            _titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, margin = new RectOffset(0, 0, 0, 4) };
        }
    }

    /// <summary>Menu item of the REAPER OSC plugin.</summary>
    public static class ReaperMenus
    {
        [MenuItem("GameObject/AsioAudioUnity/REAPER Controller", false, 24)]
        static void CreateReaperController(MenuCommand menuCommand)
        {
            int undoGroup = Undo.GetCurrentGroup();
            GameObject go = GlobalAsioAudioEditor.CreateGameObjectInScene("REAPER Controller", menuCommand);
            Undo.AddComponent<ReaperController>(go);
            Undo.CollapseUndoOperations(undoGroup);
        }
    }
}
