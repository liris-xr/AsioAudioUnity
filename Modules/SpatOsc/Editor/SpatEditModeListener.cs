using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// Lets the Spat Controllers listen to Spat Revolution in Edit mode too (Listen To Spat, Follow Spat), so sources can be placed from Spat Revolution
    /// without entering Play mode. Closes their listen ports before Play mode and before scripts reload, so the Play mode objects can open them.
    /// </summary>
    [InitializeOnLoad]
    internal static class SpatEditModeListener
    {
        private static SpatController[] _controllers = new SpatController[0];
        private static double _nextSearch;

        static SpatEditModeListener()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) StopAll();
            };
            AssemblyReloadEvents.beforeAssemblyReload += StopAll;
            EditorApplication.quitting += StopAll;
        }

        private static void Update()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.timeSinceStartup >= _nextSearch)
            {
                _nextSearch = EditorApplication.timeSinceStartup + 1.0;
                _controllers = Object.FindObjectsByType<SpatController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            }
            foreach (SpatController controller in _controllers)
            {
                if (controller != null) controller.EditModeUpdate();
            }
        }

        private static void StopAll()
        {
            foreach (SpatController controller in _controllers)
            {
                if (controller != null) controller.StopListening();
            }
        }
    }
}
