using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// Menu items to create and convert ASIO Audio Unity objects in the scene. All operations can be undone.
    /// (The OSC library, and the Spat OSC and REAPER OSC plugins, add their own menu items.)
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

        /// <summary>Creates an empty GameObject (child of the context object, if any), with Undo, and selects it.</summary>
        public static GameObject CreateGameObjectInScene(string name, MenuCommand menuCommand)
        {
            GameObject go = new GameObject(name);
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
            return go;
        }

        /// <summary>Creates a GameObject with an ASIO Audio Source on the lowest free output channel (with Undo).</summary>
        public static AsioAudioSource CreateAsioAudioSourceInScene(MenuCommand menuCommand)
        {
            GameObject go = CreateGameObjectInScene("ASIO Audio Source", menuCommand);
            AsioAudioSource asioAudioSource = Undo.AddComponent<AsioAudioSource>(go);
            DefineAsioAudioSourceOutputChannel(asioAudioSource);
            return asioAudioSource;
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

        [MenuItem("GameObject/AsioAudioUnity/ASIO Audio Source", false, 5)]
        static void CreateAsioAudioSource(MenuCommand menuCommand)
        {
            int undoGroup = Undo.GetCurrentGroup();
            CreateAsioAudioSourceInScene(menuCommand);
            Undo.CollapseUndoOperations(undoGroup);
        }

        [MenuItem("AsioAudioUnity/Project Settings…", false, 41)]
        static void OpenProjectSettings()
        {
            AsioEditorUtility.OpenSettings();
        }
    }
}
