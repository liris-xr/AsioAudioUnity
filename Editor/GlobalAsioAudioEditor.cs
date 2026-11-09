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
        static AsioAudioSource[] FindAllAsioAudioSources()
        {
            return Object.FindObjectsByType<AsioAudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        static void ConvertAudioSource(AudioSource audioSourceToConvert)
        {
            if (audioSourceToConvert == null) return;

            GameObject audioSourceToConvertGameObject = audioSourceToConvert.gameObject;

            if (audioSourceToConvertGameObject.GetComponent<AsioAudioSource>() != null)
            {
                Debug.LogWarning("GameObject " + audioSourceToConvertGameObject.name + " already has a ASIO Audio Source, its Audio Source will not be converted.");
                return;
            }

            AsioAudioSource asioAudioSource = Undo.AddComponent<AsioAudioSource>(audioSourceToConvertGameObject);

            // Get file path from AudioClip
            if (audioSourceToConvert.clip == null)
            {
                Debug.LogWarning("AudioSource " + audioSourceToConvert.name + " has no AudioClip attached, the AudioFilePath of ASIO Audio Source will not be set.");
            }
            else
            {
                asioAudioSource.AudioFilePath = AssetDatabase.GetAssetPath(audioSourceToConvert.clip);
            }

            DefineAsioAudioSourceOutputChannel(asioAudioSource);

            asioAudioSource.Loop = audioSourceToConvert.loop;
            asioAudioSource.PlayOnEnable = audioSourceToConvert.playOnAwake;
            asioAudioSource.Volume = audioSourceToConvert.volume;
            EditorUtility.SetDirty(asioAudioSource);

            Undo.DestroyObjectImmediate(audioSourceToConvert);
        }

        /// <summary>
        /// Assigns the lowest output channel not already used by another ASIO Audio Source (if none is set yet).
        /// </summary>
        static void DefineAsioAudioSourceOutputChannel(AsioAudioSource asioAudioSource)
        {
            if (asioAudioSource.TargetOutputChannel > 0) return;

            HashSet<int> usedChannels = new HashSet<int>(FindAllAsioAudioSources()
                .Where(otherSource => otherSource != asioAudioSource)
                .Select(otherSource => otherSource.TargetOutputChannel));

            int targetOutputChannel = 1;
            while (usedChannels.Contains(targetOutputChannel)) targetOutputChannel++;

            asioAudioSource.TargetOutputChannel = targetOutputChannel;
            EditorUtility.SetDirty(asioAudioSource);
        }

        static void AddSourcePositionOsc(AsioAudioSource asioAudioSource)
        {
            if (asioAudioSource == null) return;

            OSC oscManager = Object.FindFirstObjectByType<OSC>();

            if (!oscManager)
            {
                Debug.LogError("No OSC Manager found in scene. Please add an OSC Manager to the scene before adding Source Position OSC to ASIO Audio Sources.");
                return;
            }
            if (asioAudioSource.GetComponent<SourcePositionOsc>() != null)
            {
                Debug.LogWarning("Source Position OSC already attached to ASIO Audio Source " + asioAudioSource.name + ".");
                return;
            }

            SourcePositionOsc sourcePositionOsc = Undo.AddComponent<SourcePositionOsc>(asioAudioSource.gameObject);
            sourcePositionOsc.Osc = oscManager;
            sourcePositionOsc.Index = asioAudioSource.TargetOutputChannel;
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

        static void CreateAsioAudioSource(MenuCommand menuCommand, bool addSourcePositionOsc)
        {
            int undoGroup = Undo.GetCurrentGroup();

            GameObject go = CreateGameObjectInScene("ASIO Audio Source", menuCommand);
            AsioAudioSource asioAudioSource = Undo.AddComponent<AsioAudioSource>(go);

            DefineAsioAudioSourceOutputChannel(asioAudioSource);
            if (addSourcePositionOsc) AddSourcePositionOsc(asioAudioSource);

            Undo.CollapseUndoOperations(undoGroup);
        }

        [MenuItem("AsioAudioUnity/Convert selected Audio Source(s) to ASIO Audio Source(s)", false, 1)]
        static void ConvertSelectedAudioSource()
        {
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Convert Audio Sources to ASIO Audio Sources");

            foreach (GameObject selectedGameObject in Selection.gameObjects)
            {
                ConvertAudioSource(selectedGameObject.GetComponent<AudioSource>());
            }

            Undo.CollapseUndoOperations(undoGroup);
        }

        [MenuItem("AsioAudioUnity/Convert selected Audio Source(s) to ASIO Audio Source(s)", true)]
        static bool ValidateConvertSelectedAudioSource()
        {
            return Selection.gameObjects.Length > 0 && Selection.gameObjects.All(selectedGameObject => selectedGameObject.GetComponent<AudioSource>() != null);
        }

        [MenuItem("AsioAudioUnity/Add Source Position OSC to selected ASIO Audio Source(s)", false, 2)]
        static void AddSourcePositionOscToSelected()
        {
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add Source Position OSC");

            foreach (GameObject selectedGameObject in Selection.gameObjects)
            {
                AddSourcePositionOsc(selectedGameObject.GetComponent<AsioAudioSource>());
            }

            Undo.CollapseUndoOperations(undoGroup);
        }

        [MenuItem("AsioAudioUnity/Add Source Position OSC to selected ASIO Audio Source(s)", true)]
        static bool ValidateAddSourcePositionOscToSelected()
        {
            return Selection.gameObjects.Length > 0 && Selection.gameObjects.All(selectedGameObject => selectedGameObject.GetComponent<AsioAudioSource>() != null);
        }

        [MenuItem("AsioAudioUnity/Convert all Audio Sources in scene to ASIO Audio Sources", false, 21)]
        static void ConvertAllAudioSources()
        {
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Convert all Audio Sources to ASIO Audio Sources");

            foreach (AudioSource basicAudioSource in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                ConvertAudioSource(basicAudioSource);
            }

            Undo.CollapseUndoOperations(undoGroup);

            int remainingAudioSources = Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            if (remainingAudioSources == 0) Debug.Log("All Audio Sources in scene have been converted to ASIO Audio Sources.");
            else Debug.LogWarning(remainingAudioSources + " Audio Source(s) in scene have not been converted to ASIO Audio Sources.");
        }

        [MenuItem("AsioAudioUnity/Add Source Position OSC to all ASIO Audio Sources", false, 22)]
        static void AddAllSourcePositionOsc()
        {
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add Source Position OSC to all ASIO Audio Sources");

            AsioAudioSource[] allAsioAudioSources = FindAllAsioAudioSources();
            foreach (AsioAudioSource asioAudioSource in allAsioAudioSources)
            {
                AddSourcePositionOsc(asioAudioSource);
            }

            Undo.CollapseUndoOperations(undoGroup);

            int sourcesWithoutOsc = allAsioAudioSources.Count(asioAudioSource => asioAudioSource.GetComponent<SourcePositionOsc>() == null);
            if (sourcesWithoutOsc == 0) Debug.Log("All ASIO Audio Sources in scene have a Source Position OSC attached.");
            else Debug.LogWarning(sourcesWithoutOsc + " ASIO Audio Source(s) in scene don't have a Source Position OSC attached.");
        }

        [MenuItem("GameObject/AsioAudioUnity/ASIO Audio Source", false, 5)]
        static void CreateAsioAudioSource(MenuCommand menuCommand)
        {
            CreateAsioAudioSource(menuCommand, false);
        }

        [MenuItem("GameObject/AsioAudioUnity/ASIO Audio Source (with Source Position OSC)", false, 6)]
        static void CreateAsioAudioSourceWithSourcePositionOsc(MenuCommand menuCommand)
        {
            CreateAsioAudioSource(menuCommand, true);
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
