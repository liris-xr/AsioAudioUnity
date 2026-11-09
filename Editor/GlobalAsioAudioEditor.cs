using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// Menu items to create and convert ASIO Audio Unity objects in the scene. All operations can be undone.
    /// </summary>
    public static class GlobalAsioAudioEditor
    {
        static CustomAsioAudioSource[] FindAllCustomAsioAudioSources()
        {
            return Object.FindObjectsByType<CustomAsioAudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        static void ConvertAudioSource(AudioSource audioSourceToConvert)
        {
            if (audioSourceToConvert == null) return;

            GameObject audioSourceToConvertGameObject = audioSourceToConvert.gameObject;

            if (audioSourceToConvertGameObject.GetComponent<CustomAsioAudioSource>() != null)
            {
                Debug.LogWarning("GameObject " + audioSourceToConvertGameObject.name + " already has a Custom ASIO Audio Source, its Audio Source will not be converted.");
                return;
            }

            CustomAsioAudioSource customAsioAudioSource = Undo.AddComponent<CustomAsioAudioSource>(audioSourceToConvertGameObject);

            // Get file path from AudioClip
            if (audioSourceToConvert.clip == null)
            {
                Debug.LogWarning("AudioSource " + audioSourceToConvert.name + " has no AudioClip attached, the AudioFilePath of Custom ASIO Audio Source will not be set.");
            }
            else
            {
                customAsioAudioSource.AudioFilePath = AssetDatabase.GetAssetPath(audioSourceToConvert.clip);
            }

            DefineCustomAsioAudioSourceOutputChannel(customAsioAudioSource);

            customAsioAudioSource.Loop = audioSourceToConvert.loop;
            customAsioAudioSource.PlayOnEnable = audioSourceToConvert.playOnAwake;
            customAsioAudioSource.Volume = audioSourceToConvert.volume;
            EditorUtility.SetDirty(customAsioAudioSource);

            Undo.DestroyObjectImmediate(audioSourceToConvert);
        }

        /// <summary>
        /// Assigns the lowest output channel not already used by another Custom ASIO Audio Source (if none is set yet).
        /// </summary>
        static void DefineCustomAsioAudioSourceOutputChannel(CustomAsioAudioSource customAsioAudioSource)
        {
            if (customAsioAudioSource.TargetOutputChannel > 0) return;

            HashSet<int> usedChannels = new HashSet<int>(FindAllCustomAsioAudioSources()
                .Where(otherSource => otherSource != customAsioAudioSource)
                .Select(otherSource => otherSource.TargetOutputChannel));

            int targetOutputChannel = 1;
            while (usedChannels.Contains(targetOutputChannel)) targetOutputChannel++;

            customAsioAudioSource.TargetOutputChannel = targetOutputChannel;
            EditorUtility.SetDirty(customAsioAudioSource);
        }

        static void AddSourcePositionOsc(CustomAsioAudioSource customAsioAudioSource)
        {
            if (customAsioAudioSource == null) return;

            OSC oscManager = Object.FindFirstObjectByType<OSC>();

            if (!oscManager)
            {
                Debug.LogError("No OSC Manager found in scene. Please add an OSC Manager to the scene before adding Source Position OSC to Custom ASIO Audio Sources.");
                return;
            }
            if (customAsioAudioSource.GetComponent<SourcePositionOsc>() != null)
            {
                Debug.LogWarning("Source Position OSC already attached to Custom ASIO Audio Source " + customAsioAudioSource.name + ".");
                return;
            }

            SourcePositionOsc sourcePositionOsc = Undo.AddComponent<SourcePositionOsc>(customAsioAudioSource.gameObject);
            sourcePositionOsc.Osc = oscManager;
            sourcePositionOsc.Index = customAsioAudioSource.TargetOutputChannel;
            EditorUtility.SetDirty(sourcePositionOsc);
        }

        static GameObject CreateGameObjectInScene(string name, MenuCommand menuCommand)
        {
            GameObject go = new GameObject(name);
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
            return go;
        }

        static void CreateCustomAsioAudioSource(MenuCommand menuCommand, bool addSourcePositionOsc)
        {
            int undoGroup = Undo.GetCurrentGroup();

            GameObject go = CreateGameObjectInScene("Custom ASIO Audio Source", menuCommand);
            CustomAsioAudioSource customAsioAudioSource = Undo.AddComponent<CustomAsioAudioSource>(go);

            DefineCustomAsioAudioSourceOutputChannel(customAsioAudioSource);
            if (addSourcePositionOsc) AddSourcePositionOsc(customAsioAudioSource);

            Undo.CollapseUndoOperations(undoGroup);
        }

        [MenuItem("AsioAudioUnity/Convert selected Audio Source(s) to Custom ASIO Audio Source(s)", false, 1)]
        static void ConvertSelectedAudioSource()
        {
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Convert Audio Sources to Custom ASIO Audio Sources");

            foreach (GameObject selectedGameObject in Selection.gameObjects)
            {
                ConvertAudioSource(selectedGameObject.GetComponent<AudioSource>());
            }

            Undo.CollapseUndoOperations(undoGroup);
        }

        [MenuItem("AsioAudioUnity/Convert selected Audio Source(s) to Custom ASIO Audio Source(s)", true)]
        static bool ValidateConvertSelectedAudioSource()
        {
            return Selection.gameObjects.Length > 0 && Selection.gameObjects.All(selectedGameObject => selectedGameObject.GetComponent<AudioSource>() != null);
        }

        [MenuItem("AsioAudioUnity/Add Source Position OSC to selected Custom ASIO Audio Source(s)", false, 2)]
        static void AddSourcePositionOscToSelected()
        {
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add Source Position OSC");

            foreach (GameObject selectedGameObject in Selection.gameObjects)
            {
                AddSourcePositionOsc(selectedGameObject.GetComponent<CustomAsioAudioSource>());
            }

            Undo.CollapseUndoOperations(undoGroup);
        }

        [MenuItem("AsioAudioUnity/Add Source Position OSC to selected Custom ASIO Audio Source(s)", true)]
        static bool ValidateAddSourcePositionOscToSelected()
        {
            return Selection.gameObjects.Length > 0 && Selection.gameObjects.All(selectedGameObject => selectedGameObject.GetComponent<CustomAsioAudioSource>() != null);
        }

        [MenuItem("AsioAudioUnity/Convert all Audio Sources in scene to Custom ASIO Audio Sources", false, 21)]
        static void ConvertAllAudioSources()
        {
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Convert all Audio Sources to Custom ASIO Audio Sources");

            foreach (AudioSource basicAudioSource in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                ConvertAudioSource(basicAudioSource);
            }

            Undo.CollapseUndoOperations(undoGroup);

            int remainingAudioSources = Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            if (remainingAudioSources == 0) Debug.Log("All Audio Sources in scene have been converted to Custom ASIO Audio Sources.");
            else Debug.LogWarning(remainingAudioSources + " Audio Source(s) in scene have not been converted to Custom ASIO Audio Sources.");
        }

        [MenuItem("AsioAudioUnity/Add Source Position OSC to all Custom ASIO Audio Sources", false, 22)]
        static void AddAllSourcePositionOsc()
        {
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add Source Position OSC to all Custom ASIO Audio Sources");

            CustomAsioAudioSource[] allCustomAsioAudioSources = FindAllCustomAsioAudioSources();
            foreach (CustomAsioAudioSource customAsioAudioSource in allCustomAsioAudioSources)
            {
                AddSourcePositionOsc(customAsioAudioSource);
            }

            Undo.CollapseUndoOperations(undoGroup);

            int sourcesWithoutOsc = allCustomAsioAudioSources.Count(customAsioAudioSource => customAsioAudioSource.GetComponent<SourcePositionOsc>() == null);
            if (sourcesWithoutOsc == 0) Debug.Log("All Custom ASIO Audio Sources in scene have a Source Position OSC attached.");
            else Debug.LogWarning(sourcesWithoutOsc + " Custom ASIO Audio Source(s) in scene don't have a Source Position OSC attached.");
        }

        [MenuItem("GameObject/AsioAudioUnity/Custom ASIO Audio Source", false, 5)]
        static void CreateCustomAsioAudioSource(MenuCommand menuCommand)
        {
            CreateCustomAsioAudioSource(menuCommand, false);
        }

        [MenuItem("GameObject/AsioAudioUnity/Custom ASIO Audio Source (with Source Position OSC)", false, 6)]
        static void CreateCustomAsioAudioSourceWithSourcePositionOsc(MenuCommand menuCommand)
        {
            CreateCustomAsioAudioSource(menuCommand, true);
        }

        [MenuItem("GameObject/AsioAudioUnity/ASIO Audio Manager", false, 21)]
        static void CreateAsioAudioManager(MenuCommand menuCommand)
        {
            if (Object.FindFirstObjectByType<AsioAudioManager>())
            {
                Debug.LogError("ASIO Audio Manager already exists in scene.");
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            GameObject go = CreateGameObjectInScene("ASIO Audio Manager", menuCommand);
            Undo.AddComponent<AsioAudioManager>(go);
            Undo.CollapseUndoOperations(undoGroup);
        }

        [MenuItem("GameObject/AsioAudioUnity/OSC Manager", false, 22)]
        static void CreateOscManager(MenuCommand menuCommand)
        {
            if (Object.FindFirstObjectByType<OSC>())
            {
                Debug.LogError("OSC Manager already exists in scene.");
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            GameObject go = CreateGameObjectInScene("OSC Manager", menuCommand);
            Undo.AddComponent<OSC>(go);
            Undo.CollapseUndoOperations(undoGroup);
        }
    }
}
