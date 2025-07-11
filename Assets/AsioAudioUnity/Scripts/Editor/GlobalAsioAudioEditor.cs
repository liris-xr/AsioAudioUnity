using UnityEngine;
using UnityEditor;
using System.Linq;

namespace AsioAudioUnity
{
    public class GlobalAsioAudioEditor : EditorWindow
    {
        private static AudioSource[] allBasicAudioSources = FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
        private static CustomAsioAudioSource[] allCustomAsioAudioSources = FindObjectsByType<CustomAsioAudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);

        static void ConvertAudioSource(AudioSource audioSourceToConvert)
        {
            GameObject audioSourceToConvertGameObject = audioSourceToConvert.gameObject;

            // Create CustomAsioAudioSource
            audioSourceToConvertGameObject.AddComponent<CustomAsioAudioSource>();

            // Add CustomAsioAudioSource to list
            allCustomAsioAudioSources.ToList().Add(audioSourceToConvertGameObject.GetComponent<CustomAsioAudioSource>());

            // Get file path from AudioClip
            if (audioSourceToConvert.clip == null)
            {
                Debug.LogWarning("AudioSource " + audioSourceToConvert.name + " has no AudioClip attached, the AudioFilePath of Custom ASIO Audio Source will not be set.");
            }
            else
            {
                string assetPath = AssetDatabase.GetAssetPath(audioSourceToConvert.clip.GetInstanceID());
                audioSourceToConvertGameObject.GetComponent<CustomAsioAudioSource>().AudioFilePath = assetPath;
            }

            // Define target output channel
            DefineCustomAsioAudioSourceOutputChannel(audioSourceToConvertGameObject.GetComponent<CustomAsioAudioSource>());

            // Define Loop, Play On Enable and Volume properties
            audioSourceToConvertGameObject.GetComponent<CustomAsioAudioSource>().Loop = audioSourceToConvert.loop;
            audioSourceToConvertGameObject.GetComponent<CustomAsioAudioSource>().PlayOnEnable = audioSourceToConvert.playOnAwake;
            audioSourceToConvertGameObject.GetComponent<CustomAsioAudioSource>().Volume = audioSourceToConvert.volume;

            // Remove this audio source (first in list, then destroy)
            allBasicAudioSources.ToList().Remove(audioSourceToConvert);
            DestroyImmediate(audioSourceToConvert);
        }

        static void DefineCustomAsioAudioSourceOutputChannel(CustomAsioAudioSource customAsioAudioSource)
        {
            allCustomAsioAudioSources = FindObjectsByType<CustomAsioAudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);

            int targetOutputChannelIndex = 1;

            while (customAsioAudioSource.GetComponent<CustomAsioAudioSource>().TargetOutputChannel == 0)
            {
                if (allCustomAsioAudioSources.ToList().Find((customAsioAudioSource) => customAsioAudioSource.TargetOutputChannel == targetOutputChannelIndex)) targetOutputChannelIndex++;
                else customAsioAudioSource.GetComponent<CustomAsioAudioSource>().TargetOutputChannel = targetOutputChannelIndex;
            }
        }

        static void AddSourcePositionOsc(CustomAsioAudioSource customAsioAudioSource)
        {
            OSC oscManager = FindFirstObjectByType<OSC>();

            if (!oscManager)
            {
                Debug.LogError("No OSC Manager found in scene. Please add an OSC Manager to the scene before adding Source Position OSC to Custom ASIO Audio Sources.");
                return;
            }
            if (customAsioAudioSource.gameObject.GetComponent<SourcePositionOsc>() != null)
            {
                Debug.LogWarning("Source Position OSC already attached to Custom ASIO Audio Source " + customAsioAudioSource.name + ".");
                return;
            }

            customAsioAudioSource.gameObject.AddComponent<SourcePositionOsc>();
            customAsioAudioSource.gameObject.GetComponent<SourcePositionOsc>().Osc = oscManager;
            customAsioAudioSource.gameObject.GetComponent<SourcePositionOsc>().Index = customAsioAudioSource.TargetOutputChannel;
        }
        static void CreateCustomAsioAudioSource(MenuCommand menuCommand, bool addSourcePositionOsc)
        {
            GameObject go = new GameObject("Custom ASIO Audio Source");
            go.AddComponent<CustomAsioAudioSource>();

            DefineCustomAsioAudioSourceOutputChannel(go.GetComponent<CustomAsioAudioSource>());
            if (addSourcePositionOsc) AddSourcePositionOsc(go.GetComponent<CustomAsioAudioSource>());

            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;

            allCustomAsioAudioSources.ToList().Add(go.GetComponent<CustomAsioAudioSource>());
        }

        [MenuItem("AsioAudioUnity/Convert selected Audio Source(s) to Custom ASIO Audio Source(s)", false, 1)]
        static void ConvertSelectedAudioSource()
        {
            foreach (GameObject selectedGameObject in Selection.gameObjects)
            {
                ConvertAudioSource(selectedGameObject.GetComponent<AudioSource>());
            }
               
        }

        [MenuItem("AsioAudioUnity/Convert selected Audio Source(s) to Custom ASIO Audio Source(s)", true)]
        static bool ValidateConvertSelectedAudioSource()
        {
            if (!Selection.activeGameObject)
            {
                return false;
            }
            foreach (GameObject selectedGameObject in Selection.gameObjects)
            {
                if (selectedGameObject.GetComponent<AudioSource>() == null) return false;
            }
            return true;
        }

        [MenuItem("AsioAudioUnity/Add Source Position OSC to selected Custom ASIO Audio Source(s)", false, 2)]
        static void AddSourcePositionOscToSelected()
        {
            foreach (GameObject selectedGameObject in Selection.gameObjects) 
            {
                AddSourcePositionOsc(selectedGameObject.GetComponent<CustomAsioAudioSource>());
            }
        }

        [MenuItem("AsioAudioUnity/Add Source Position OSC to selected Custom ASIO Audio Source(s)", true)]
        static bool ValidateAddSourcePositionOscToSelected()
        {
            if (!Selection.activeGameObject)
            {
                return false;
            }
            foreach (GameObject selectedGameObject in Selection.gameObjects)
            {
                if (selectedGameObject.GetComponent<CustomAsioAudioSource>() == null) return false;
            }
            return true;
        }

        [MenuItem("AsioAudioUnity/Convert all Audio Sources in scene to Custom ASIO Audio Sources", false, 21)]
        static void ConvertAllAudioSources()
        {
            allCustomAsioAudioSources = FindObjectsByType<CustomAsioAudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
            allBasicAudioSources = FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);

            foreach (AudioSource basicAudioSource in allBasicAudioSources)
            {
                ConvertAudioSource(basicAudioSource);
            }

            if (FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID).Length == 0) Debug.Log("All Audio Sources in scene have been converted to Custom ASIO Audio Sources.");
            else Debug.LogWarning("Some Audio Sources in scene have not been converted to Custom ASIO Audio Sources.");
        }

        [MenuItem("AsioAudioUnity/Add Source Position OSC to all Custom ASIO Audio Sources", false, 22)]
        static void AddAllSourcePositionOsc()
        {
            allCustomAsioAudioSources = FindObjectsByType<CustomAsioAudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);

            foreach (CustomAsioAudioSource customAsioAudioSource in allCustomAsioAudioSources)
            {
                AddSourcePositionOsc(customAsioAudioSource);
            }

            if (FindObjectsByType<SourcePositionOsc>(FindObjectsSortMode.InstanceID).Length == allCustomAsioAudioSources.Length) Debug.Log("All Source Position OSC scripts in scene have been added to Custom ASIO Audio Sources.");
            else Debug.LogWarning("Some Custom ASIO Audio Sources in scene have not a Source Position OSC attached.");
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
            if (FindFirstObjectByType<AsioAudioManager>())
            {
                Debug.LogError("ASIO Audio Manager already exists in scene.");
                return;
            }

            GameObject go = new GameObject("ASIO Audio Manager");
            go.AddComponent<AsioAudioManager>();
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }

        [MenuItem("GameObject/AsioAudioUnity/OSC Manager", false, 22)]
        static void CreateOscManager(MenuCommand menuCommand)
        {
            if (FindFirstObjectByType<OSC>())
            {
                Debug.LogError("OSC Manager already exists in scene.");
                return;
            }

            GameObject go = new GameObject("OSC Manager");
            go.AddComponent<OSC>();
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
            Selection.activeObject = go;
        }
    }
}

