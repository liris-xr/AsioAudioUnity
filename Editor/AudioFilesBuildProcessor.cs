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
    public class AudioFilesBuildProcessor : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            List<CustomAsioAudioSource> customAsioAudioSources = new List<CustomAsioAudioSource>();
            List<EditorBuildSettingsScene> listScenesInBuild = EditorBuildSettings.scenes.Where(scene => scene.enabled).ToList();

            foreach (EditorBuildSettingsScene editorBuildSettingsScene in listScenesInBuild)
            {
                Scene scene = EditorSceneManager.OpenScene(editorBuildSettingsScene.path, OpenSceneMode.Single);
                customAsioAudioSources.AddRange(scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<CustomAsioAudioSource>()).ToList());
                Debug.Log($"Processed scene: {scene.name}, found {customAsioAudioSources.Count} CustomAsioAudioSource components.");
            }

            foreach (var customAsioAudioSource in customAsioAudioSources)
            {
                string editorAudioFilePath = Path.Combine(Application.dataPath.Substring(0, Application.dataPath.LastIndexOf('/')), customAsioAudioSource.AudioFilePath);
                if (!File.Exists(editorAudioFilePath))
                {
                    Debug.Log(Application.dataPath);
                    Debug.LogError($"Audio file not found at path: {editorAudioFilePath} for GameObject: {customAsioAudioSource.gameObject.name}");
                }
                else
                {
                    string buildAudioFilePath = Path.Combine(Path.GetDirectoryName(report.summary.outputPath), customAsioAudioSource.AudioFilePath);
                    string buildDirectoryPath = Path.GetDirectoryName(buildAudioFilePath);

                    if (!Directory.Exists(buildDirectoryPath))
                    {
                        Directory.CreateDirectory(buildDirectoryPath);
                    }

                    if (!File.Exists(buildAudioFilePath))
                    {
                        File.Copy(editorAudioFilePath, buildAudioFilePath);
                        Debug.Log($"Copied audio file to build path: {buildAudioFilePath}");
                    }
                    else if (!File.ReadAllBytes(buildAudioFilePath).SequenceEqual(File.ReadAllBytes(editorAudioFilePath)) || report.summary.options.HasFlag(BuildOptions.CleanBuildCache))
                    {
                        File.Delete(buildAudioFilePath);
                        File.Copy(editorAudioFilePath, buildAudioFilePath);
                        Debug.Log($"Re-copied audio file: {buildAudioFilePath}");
                    }
                    else Debug.Log($"Audio file already exists at build path: {buildAudioFilePath}");
                }
            }
        }
    }
}