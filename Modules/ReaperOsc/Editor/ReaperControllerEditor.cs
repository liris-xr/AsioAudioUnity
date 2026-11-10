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
        private SerializedProperty _playRegionScriptId;
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
            _playRegionScriptId = serializedObject.FindProperty("_playRegionScriptId");
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
            _showAddresses = EditorGUILayout.Foldout(_showAddresses, new GUIContent("OSC Addresses", "The addresses of REAPER's Default OSC pattern configuration. Change them only if your control surface uses another pattern file."), true);
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
            GUILayout.Label("Follow Unity", _titleStyle);
            EditorGUILayout.PropertyField(_followTransport, new GUIContent("Follow Transport", "PlayAll plays REAPER, PauseAll pauses it, StopAll stops it, RestartAll plays it from the start of the project. When the scene starts with sources playing (Play On Enable), REAPER plays from the start of the project too."));
            EditorGUILayout.PropertyField(_stopReaperOnQuit, new GUIContent("Stop REAPER On Quit", "Stops REAPER when the application quits, or when leaving Play mode."));
            EditorGUILayout.EndVertical();
        }

        private void DrawScriptsCard()
        {
            EditorGUILayout.BeginVertical(_cardStyle);
            GUILayout.Label("Lua Scripts", _titleStyle);
            EditorGUILayout.PropertyField(_playRegionScriptId, new GUIContent("Play Region Script ID",
                "Command ID of the PlayRegion.lua script in REAPER (e.g. _RS7d3c...). Play Region commands run it with the region number, and it plays the region in loop. Empty: they go to the region and play from there, without looping."));

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Show Scripts", "Open the folder of the package's REAPER Lua scripts."), EditorStyles.miniButtonLeft))
            {
                string folder = ScriptsFolder();
                if (folder != null) EditorUtility.RevealInFinder(Directory.GetFiles(folder, "*.lua").FirstOrDefault() ?? folder);
            }
            if (GUILayout.Button(new GUIContent("Install into REAPER", "Copy the scripts to REAPER's Scripts folder (%APPDATA%\\REAPER\\Scripts\\AsioAudioUnity)."), EditorStyles.miniButtonRight))
            {
                InstallScripts();
            }
            EditorGUILayout.EndHorizontal();

            if (_installMessage != null) EditorGUILayout.HelpBox(_installMessage, _installMessageType);
            EditorGUILayout.LabelField("Then in REAPER: Actions > Show action list > New action > Load ReaScript..., load PlayRegion.lua, right-click it > Copy selected action command ID, and paste the ID here. Your own scripts work the same way, in Run Action (With Value) commands.",
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

        private static string ScriptsFolder()
        {
            UnityEditor.PackageManager.PackageInfo package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ReaperController).Assembly);
            string folder = package != null ? Path.Combine(package.resolvedPath, "Modules", "ReaperOsc", "Lua Scripts") : null;
            return folder != null && Directory.Exists(folder) ? folder : null;
        }

        private void InstallScripts()
        {
            string source = ScriptsFolder();
            string reaperFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "REAPER");
            if (source == null)
            {
                SetInstallMessage("The package's Lua scripts folder was not found.", MessageType.Error);
                return;
            }
            if (!Directory.Exists(reaperFolder))
            {
                SetInstallMessage("REAPER's resource folder was not found (" + reaperFolder + "). For a portable REAPER, copy the scripts from \"Show Scripts\" to its Scripts folder.", MessageType.Warning);
                return;
            }

            try
            {
                string destination = Path.Combine(reaperFolder, "Scripts", "AsioAudioUnity");
                Directory.CreateDirectory(destination);
                string[] scripts = Directory.GetFiles(source, "*.lua");
                foreach (string script in scripts) File.Copy(script, Path.Combine(destination, Path.GetFileName(script)), true);
                SetInstallMessage(scripts.Length + " script(s) copied to " + destination + ".", MessageType.Info);
                EditorUtility.RevealInFinder(Path.Combine(destination, Path.GetFileName(scripts.First())));
            }
            catch (Exception e)
            {
                SetInstallMessage("Could not copy the scripts: " + e.Message, MessageType.Error);
            }
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
