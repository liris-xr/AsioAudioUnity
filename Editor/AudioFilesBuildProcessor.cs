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
    /// After a build, copies the audio files used by ASIO Audio Sources (including inactive ones) next to the built player, at the same relative path.
    /// The scenes are the ones actually built, whatever the list comes from: Build Settings, build profiles, or BuildPipeline.BuildPlayer called from a script.
    /// </summary>
    public class AudioFilesBuildProcessor : BuildPlayerProcessor, IPostprocessBuildWithReport
    {
        // The scenes of the build in progress, given by PrepareForBuild.
        private static string[] _builtScenePaths;

        public override int callbackOrder => 0;

        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            _builtScenePaths = buildPlayerContext.BuildPlayerOptions.scenes;
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            string[] scenePaths = _builtScenePaths ?? EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            _builtScenePaths = null;

            // Path stored by the sources (where the player looks for the file) -> file to copy (the same, or found from its asset GUID if it moved)
            Dictionary<string, string> audioFilePaths = CollectAudioFilePaths(scenePaths);

            string projectRootPath = Path.GetDirectoryName(Application.dataPath);
            string buildRootPath = Path.GetDirectoryName(report.summary.outputPath);
            bool cleanBuild = report.summary.options.HasFlag(BuildOptions.CleanBuildCache);
            int copiedFiles = 0;

            foreach (KeyValuePair<string, string> audioFile in audioFilePaths)
            {
                string audioFilePath = audioFile.Key;
                if (Path.IsPathRooted(audioFilePath))
                {
                    Debug.LogWarning($"Audio file \"{audioFilePath}\" is referenced with an absolute path: it was not copied to the build, and the build will only find it on this machine. Move it inside the project to include it in builds.");
                    continue;
                }

                string editorAudioFilePath = Path.Combine(projectRootPath, audioFile.Value);
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

            Debug.Log($"ASIO Audio Unity: {audioFilePaths.Count} audio file(s) referenced in the built scenes, {copiedFiles} copied to the build folder.");
        }

        /// <summary>
        /// Collects the audio file paths of all ASIO Audio Sources (including inactive ones) in the given scenes.
        /// Scenes that are not already loaded are opened additively and closed afterwards, so the scenes open in the editor are left untouched.
        /// </summary>
        private static Dictionary<string, string> CollectAudioFilePaths(IEnumerable<string> scenePaths)
        {
            Dictionary<string, string> audioFilePaths = new Dictionary<string, string>();

            foreach (string scenePath in scenePaths)
            {
                if (string.IsNullOrEmpty(scenePath) || !File.Exists(scenePath))
                {
                    Debug.LogWarning($"ASIO Audio Unity: scene \"{scenePath}\" was not found, its audio files were not copied.");
                    continue;
                }

                Scene scene = SceneManager.GetSceneByPath(scenePath);
                bool wasInHierarchy = scene.IsValid();
                bool openedHere = !scene.isLoaded;

                if (openedHere) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

                try
                {
                    foreach (GameObject rootGameObject in scene.GetRootGameObjects())
                    {
                        foreach (AsioAudioSource asioAudioSource in rootGameObject.GetComponentsInChildren<AsioAudioSource>(true))
                        {
                            if (string.IsNullOrEmpty(asioAudioSource.AudioFilePath))
                            {
                                Debug.LogWarning($"ASIO Audio Source on GameObject \"{asioAudioSource.gameObject.name}\" (scene \"{scene.name}\") has no audio file set.");
                                continue;
                            }
                            string movedTo = AsioAudioFileRelinker.FindMovedFile(asioAudioSource);
                            if (movedTo != null) Debug.LogWarning($"ASIO Audio Source on GameObject \"{asioAudioSource.gameObject.name}\" (scene \"{scene.name}\"): its audio file was moved to \"{movedTo}\". "
                                + "It is copied to the build at the path the scene stores; open and save the scene to update the path.");
                            audioFilePaths[asioAudioSource.AudioFilePath] = movedTo ?? asioAudioSource.AudioFilePath;
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
