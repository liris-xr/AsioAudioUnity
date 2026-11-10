using UnityEditor;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// Menu items of the shared OSC library: OSC Manager and ASIO OSC Remote. All operations can be undone.
    /// (The Spat OSC and REAPER OSC plugins add their own menu items.)
    /// </summary>
    public static class AsioOscMenus
    {
        [MenuItem("GameObject/AsioAudioUnity/OSC Manager", false, 22)]
        static void CreateOscManager(MenuCommand menuCommand)
        {
            if (Object.FindFirstObjectByType<OSC>())
            {
                Debug.LogError("OSC Manager already exists in scene.");
                return;
            }

            int undoGroup = Undo.GetCurrentGroup();
            GameObject go = GlobalAsioAudioEditor.CreateGameObjectInScene("OSC Manager", menuCommand);
            Undo.AddComponent<OSC>(go);
            Undo.CollapseUndoOperations(undoGroup);
        }

        [MenuItem("GameObject/AsioAudioUnity/ASIO OSC Remote", false, 23)]
        static void CreateOscRemote(MenuCommand menuCommand)
        {
            int undoGroup = Undo.GetCurrentGroup();
            GameObject go = GlobalAsioAudioEditor.CreateGameObjectInScene("ASIO OSC Remote", menuCommand);
            Undo.AddComponent<AsioOscRemote>(go);
            Undo.CollapseUndoOperations(undoGroup);
        }
    }
}
