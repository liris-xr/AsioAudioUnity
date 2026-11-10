using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// Moves the settings of a scene's ASIO Audio Manager (before version 3.0) to Project Settings > ASIO Audio, then removes the component.
    /// </summary>
    internal static class AsioSettingsMigration
    {
        /// <summary>The ASIO Audio Managers saved in the open scenes (in Edit mode).</summary>
        public static AsioAudioManager[] FindSceneManagers()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return new AsioAudioManager[0];
            return Object.FindObjectsByType<AsioAudioManager>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(manager => !EditorUtility.IsPersistent(manager))
                .ToArray();
        }

        // Offered by Project Settings > ASIO Audio and by the scene manager's Inspector when a scene has one.
        public static void Migrate()
        {
            AsioAudioManager[] managers = FindSceneManagers();
            if (managers.Length == 0)
            {
                Debug.Log("ASIO Audio Unity: the open scenes have no ASIO Audio Manager to migrate.");
                return;
            }

            AsioAudioManager source = managers.FirstOrDefault(manager => manager.HasLegacySceneSettings) ?? managers[0];
            if (managers.Length > 1) Debug.LogWarning("ASIO Audio Unity: the open scenes have " + managers.Length + " ASIO Audio Managers: the settings of \"" + source.name + "\" are used.");

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Migrate ASIO Audio Manager to Project Settings");

            AsioAudioSettings settings = AsioAudioSettingsProvider.GetOrCreateSettingsAsset();
            Undo.RecordObject(settings, "Migrate ASIO Audio Manager to Project Settings");

            if (source.HasLegacySceneSettings)
            {
                settings.AsioDriverName = source.LegacyDriverName;
                settings.TargetSampleRate = source.LegacySampleRate;
                settings.TargetBitsPerSample = source.LegacyBitsPerSample;
                settings.MasterVolume = source.LegacyMasterVolume;
                settings.DisplayInfoOnGameWindow = source.LegacyDisplayInfoOnGameWindow;
                settings.OverlayToggleKey = source.LegacyOverlayToggleKey;
                settings.OverlayScale = source.LegacyOverlayScale;

                // Snapshots referenced the scene's sources: they now identify them by output channel.
                foreach (AsioMixerSnapshot legacySnapshot in source.LegacySnapshots)
                {
                    AsioMixerSnapshot snapshot = new AsioMixerSnapshot { Name = legacySnapshot.Name, MasterVolume = legacySnapshot.MasterVolume };
                    foreach (AsioMixerSnapshot.SourceSettings legacySettings in legacySnapshot.Sources)
                    {
                        int channel = legacySettings.Source != null ? legacySettings.Source.TargetOutputChannel : legacySettings.OutputChannel;
                        if (channel <= 0 || snapshot.Sources.Any(other => other.OutputChannel == channel)) continue;
                        snapshot.Sources.Add(new AsioMixerSnapshot.SourceSettings { OutputChannel = channel, Volume = legacySettings.Volume, Mute = legacySettings.Mute, Solo = legacySettings.Solo });
                    }
                    settings.Snapshots.RemoveAll(other => other.Name == snapshot.Name);
                    settings.Snapshots.Add(snapshot);
                }
            }
            EditorUtility.SetDirty(settings);

            // Remove the managers (and their GameObject when nothing else is on it).
            HashSet<UnityEngine.SceneManagement.Scene> scenes = new HashSet<UnityEngine.SceneManagement.Scene>();
            foreach (AsioAudioManager manager in managers)
            {
                GameObject managerObject = manager.gameObject;
                scenes.Add(managerObject.scene);
                bool onlyTheManager = managerObject.GetComponents<Component>().Length == 2 && managerObject.transform.childCount == 0; // Transform + manager

                if (PrefabUtility.IsPartOfPrefabInstance(managerObject) && onlyTheManager)
                {
                    GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(managerObject);
                    if (root == managerObject) Undo.DestroyObjectImmediate(managerObject);
                    else Undo.DestroyObjectImmediate(manager);
                }
                else if (onlyTheManager) Undo.DestroyObjectImmediate(managerObject);
                else Undo.DestroyObjectImmediate(manager);
            }
            foreach (UnityEngine.SceneManagement.Scene scene in scenes) EditorSceneManager.MarkSceneDirty(scene);

            Undo.CollapseUndoOperations(undoGroup);
            AssetDatabase.SaveAssets();

            Debug.Log("ASIO Audio Unity: the ASIO Audio Manager's settings were moved to Project Settings > ASIO Audio (driver \"" + settings.AsioDriverName + "\", "
                + settings.TargetSampleRate + " Hz, " + settings.Snapshots.Count + " snapshot(s)), and " + managers.Length + " manager(s) removed from the scene. Save the scene to keep the change.", settings);
            AsioEditorUtility.OpenSettings();
        }
    }
}
