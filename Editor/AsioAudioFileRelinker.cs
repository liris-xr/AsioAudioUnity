using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AsioAudioUnity
{
    /// <summary>
    /// Keeps the audio file paths of the ASIO Audio Sources valid in the Editor. A source stores its file's path, and, for a file inside the project,
    /// its asset GUID. When the path no longer exists (the file was moved or renamed, or a package sample was imported for another version):
    /// <list type="bullet">
    /// <item>when a scene is opened, the path is recovered from the GUID, or else from a file with the same name under the scene's folder (if there is exactly one);</item>
    /// <item>when an asset is moved or renamed in the Project window, the sources of the open scenes that use it follow it.</item>
    /// </list>
    /// Each change is logged, and the scene is marked as modified.
    /// </summary>
    [InitializeOnLoad]
    internal static class AsioAudioFileRelinker
    {
        static AsioAudioFileRelinker()
        {
            EditorSceneManager.sceneOpened += (scene, mode) => RelinkMissingFiles(scene);
        }

        /// <summary>The project path of the source's file found from its asset GUID, if its stored path no longer exists; otherwise null.</summary>
        public static string FindMovedFile(AsioAudioSource source)
        {
            if (source == null || string.IsNullOrEmpty(source.AudioFileGuid)) return null;
            if (!string.IsNullOrEmpty(source.AudioFilePath) && File.Exists(source.ResolvedAudioFilePath)) return null;
            string path = AssetDatabase.GUIDToAssetPath(source.AudioFileGuid);
            return !string.IsNullOrEmpty(path) && File.Exists(Path.Combine(ProjectFolder, path)) ? path : null;
        }

        public static int RelinkMissingFiles(Scene scene)
        {
            if (!scene.IsValid() || EditorApplication.isPlayingOrWillChangePlaymode) return 0;

            string sceneFolder = null;
            if (!string.IsNullOrEmpty(scene.path))
            {
                sceneFolder = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(ProjectFolder, scene.path)));
                if (!Directory.Exists(sceneFolder)) sceneFolder = null;
            }

            int relinked = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (AsioAudioSource source in root.GetComponentsInChildren<AsioAudioSource>(true))
                {
                    string path = source.AudioFilePath;
                    if (string.IsNullOrEmpty(path) || File.Exists(source.ResolvedAudioFilePath)) continue;

                    string newPath = FindMovedFile(source);
                    string reason = "found from its asset GUID: it was moved or renamed";
                    if (newPath == null && sceneFolder != null)
                    {
                        newPath = FindSameNameFile(path, sceneFolder);
                        reason = "same name, in the scene's folder";
                    }
                    if (newPath == null) continue;

                    SetPath(source, newPath);
                    relinked++;
                    Debug.Log("ASIO Audio Source \"" + source.name + "\": the audio file \"" + path + "\" wasn't found, relinked to \"" + newPath + "\" (" + reason + ").", source);
                }
            }

            if (relinked > 0) EditorSceneManager.MarkSceneDirty(scene);
            return relinked;
        }

        // Called after assets are moved or renamed: the sources of the open scenes using them follow.
        internal static void OnAssetsMoved(string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (movedAssets.Length == 0 || EditorApplication.isPlayingOrWillChangePlaymode) return;

            foreach (AsioAudioSource source in UnityEngine.Object.FindObjectsByType<AsioAudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (EditorUtility.IsPersistent(source) || string.IsNullOrEmpty(source.AudioFilePath)) continue;
                string path = source.AudioFilePath.Replace('\\', '/');
                for (int i = 0; i < movedAssets.Length; i++)
                {
                    if (!string.Equals(path, movedFromAssetPaths[i], StringComparison.OrdinalIgnoreCase)) continue;
                    SetPath(source, movedAssets[i]);
                    EditorSceneManager.MarkSceneDirty(source.gameObject.scene);
                    Debug.Log("ASIO Audio Source \"" + source.name + "\": its audio file was moved to \"" + movedAssets[i] + "\".", source);
                    break;
                }
            }
        }

        private static string ProjectFolder => Path.GetDirectoryName(Application.dataPath);

        private static void SetPath(AsioAudioSource source, string newPath)
        {
            SerializedObject serializedSource = new SerializedObject(source);
            serializedSource.FindProperty("_audioFilePath").stringValue = newPath;
            string guid = AsioAudioSource.GetAssetGuid(newPath);
            if (!string.IsNullOrEmpty(guid)) serializedSource.FindProperty("_audioFileGuid").stringValue = guid;
            serializedSource.ApplyModifiedProperties();
        }

        private static string FindSameNameFile(string path, string folder)
        {
            string fileName = Path.GetFileName(path.Replace('\\', '/'));
            if (string.IsNullOrEmpty(fileName)) return null;

            string[] matches;
            try { matches = Directory.GetFiles(folder, fileName, SearchOption.AllDirectories); }
            catch (Exception) { return null; }
            return matches.Length == 1 ? ToProjectPath(matches[0]) : null;
        }

        // A path from the project folder, with forward slashes (as the source stores it), or null if the file is outside the project.
        private static string ToProjectPath(string fullPath)
        {
            string full = Path.GetFullPath(fullPath).Replace('\\', '/');
            string root = Path.GetFullPath(ProjectFolder).Replace('\\', '/').TrimEnd('/') + "/";
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : null;
        }
    }

    internal sealed class AsioAudioFileMoveWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            AsioAudioFileRelinker.OnAssetsMoved(movedAssets, movedFromAssetPaths);
        }
    }
}
