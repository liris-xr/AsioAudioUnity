using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace AsioAudioUnity
{
    /// <summary>
    /// After a build, copies the audio files used by Custom ASIO Audio Sources (in scenes enabled in the build) next to the built player, at the same relative path.
    /// </summary>
    public class AudioFilesBuildProcessor : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            HashSet<string> audioFilePaths = CollectAudioFilePathsFromBuildScenes();

            string projectRootPath = Path.GetDirectoryName(Application.dataPath);
            string buildRootPath = Path.GetDirectoryName(report.summary.outputPath);
            bool cleanBuild = report.summary.options.HasFlag(BuildOptions.CleanBuildCache);
            int copiedFiles = 0;

            foreach (string audioFilePath in audioFilePaths)
            {
                if (Path.IsPathRooted(audioFilePath))
                {
                    Debug.LogWarning($"Audio file \"{audioFilePath}\" is referenced with an absolute path: it was not copied to the build, and the build will only find it on this machine. Move it inside the project to include it in builds.");
                    continue;
                }

                string editorAudioFilePath = Path.Combine(projectRootPath, audioFilePath);
                if (!File.Exists(editorAudioFilePath))
                {
                    Debug.LogError($"Audio file not found at path: {editorAudioFilePath}. It was not copied to the build.");
                    continue;
                }

                string buildAudioFilePath = Path.Combine(buildRootPath, audioFilePath);
                Directory.CreateDirectory(Path.GetDirectoryName(buildAudioFilePath));

                if (cleanBuild || !File.Exists(buildAudioFilePath) || FileIsOutdated(editorAudioFilePath, buildAudioFilePath))
                {
                    File.Copy(editorAudioFilePath, buildAudioFilePath, true);
                    copiedFiles++;
                }
            }

            Debug.Log($"ASIO Audio Unity: {audioFilePaths.Count} audio file(s) referenced in build scenes, {copiedFiles} copied to the build folder.");
        }

        /// <summary>
        /// Collects the audio file paths of all Custom ASIO Audio Sources (including inactive ones) in the scenes enabled in the build settings.
        /// Scenes that are not already loaded are opened additively and closed afterwards, so the scenes open in the editor are left untouched.
        /// </summary>
        private static HashSet<string> CollectAudioFilePathsFromBuildScenes()
        {
            HashSet<string> audioFilePaths = new HashSet<string>();

            foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes.Where(scene => scene.enabled))
            {
                Scene scene = SceneManager.GetSceneByPath(buildScene.path);
                bool wasInHierarchy = scene.IsValid();
                bool openedHere = !scene.isLoaded;

                if (openedHere) scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Additive);

                try
                {
                    foreach (GameObject rootGameObject in scene.GetRootGameObjects())
                    {
                        foreach (CustomAsioAudioSource customAsioAudioSource in rootGameObject.GetComponentsInChildren<CustomAsioAudioSource>(true))
                        {
                            if (string.IsNullOrEmpty(customAsioAudioSource.AudioFilePath))
                            {
                                Debug.LogWarning($"Custom ASIO Audio Source on GameObject \"{customAsioAudioSource.gameObject.name}\" (scene \"{scene.name}\") has no audio file set.");
                                continue;
                            }
                            audioFilePaths.Add(customAsioAudioSource.AudioFilePath);
                        }
                    }
                }
                finally
                {
                    if (openedHere) EditorSceneManager.CloseScene(scene, !wasInHierarchy);
                }
            }

            return audioFilePaths;
        }

        private static bool FileIsOutdated(string sourceFilePath, string copiedFilePath)
        {
            FileInfo sourceFile = new FileInfo(sourceFilePath);
            FileInfo copiedFile = new FileInfo(copiedFilePath);
            return sourceFile.Length != copiedFile.Length || sourceFile.LastWriteTimeUtc > copiedFile.LastWriteTimeUtc;
        }
    }
}
