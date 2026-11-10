using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AsioAudioUnity
{
    /// <summary>
    /// When a scene is opened, relinks the ASIO Audio Sources whose audio file isn't found, to a file with the same name under the scene's folder
    /// (when there is exactly one). Paths are stored from the project folder, so they break when a scene and its sounds are moved together,
    /// or when a package sample is imported for another version (Assets/Samples/[package]/[version]/...).
    /// The scene is marked as modified, and each relink is logged.
    /// </summary>
    [InitializeOnLoad]
    internal static class AsioAudioFileRelinker
    {
        static AsioAudioFileRelinker()
        {
            EditorSceneManager.sceneOpened += (scene, mode) => RelinkMissingFiles(scene);
        }

        public static int RelinkMissingFiles(Scene scene)
        {
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path) || EditorApplication.isPlayingOrWillChangePlaymode) return 0;

            string projectFolder = Path.GetDirectoryName(Application.dataPath);
            string sceneFolder = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(projectFolder, scene.path)));
            if (string.IsNullOrEmpty(sceneFolder) || !Directory.Exists(sceneFolder)) return 0;

            int relinked = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (AsioAudioSource source in root.GetComponentsInChildren<AsioAudioSource>(true))
                {
                    string path = source.AudioFilePath;
                    if (string.IsNullOrEmpty(path) || File.Exists(source.ResolvedAudioFilePath)) continue;

                    string fileName = Path.GetFileName(path.Replace('\\', '/'));
                    if (string.IsNullOrEmpty(fileName)) continue;

                    string[] matches;
                    try { matches = Directory.GetFiles(sceneFolder, fileName, SearchOption.AllDirectories); }
                    catch (Exception) { continue; }
                    if (matches.Length != 1) continue;

                    string newPath = ToProjectPath(matches[0], projectFolder);
                    if (newPath == null) continue;

                    SerializedObject serializedSource = new SerializedObject(source);
                    serializedSource.FindProperty("_audioFilePath").stringValue = newPath;
                    serializedSource.ApplyModifiedProperties();
                    relinked++;
                    Debug.Log("ASIO Audio Source \"" + source.name + "\": the audio file \"" + path + "\" wasn't found, relinked to \"" + newPath + "\" (same name, in the scene's folder).", source);
                }
            }

            if (relinked > 0) EditorSceneManager.MarkSceneDirty(scene);
            return relinked;
        }

        // A path from the project folder, with forward slashes (as the source stores it), or null if the file is outside the project.
        private static string ToProjectPath(string fullPath, string projectFolder)
        {
            string full = Path.GetFullPath(fullPath).Replace('\\', '/');
            string root = Path.GetFullPath(projectFolder).Replace('\\', '/').TrimEnd('/') + "/";
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : null;
        }
    }
}
