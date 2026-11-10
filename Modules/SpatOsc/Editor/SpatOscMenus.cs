using UnityEditor;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// Menu items of the Spat OSC plugin. All operations can be undone.
    /// </summary>
    public static class SpatOscMenus
    {
        [MenuItem("GameObject/AsioAudioUnity/Spat Controller", false, 25)]
        static void CreateSpatController(MenuCommand menuCommand)
        {
            int undoGroup = Undo.GetCurrentGroup();
            GameObject go = GlobalAsioAudioEditor.CreateGameObjectInScene("Spat Controller", menuCommand);
            Undo.AddComponent<SpatController>(go);
            Undo.CollapseUndoOperations(undoGroup);
        }
    }
}
