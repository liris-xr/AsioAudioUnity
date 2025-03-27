using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AsioAudioUnity
{
    [CustomEditor(typeof(CustomAsioAudioSource)), CanEditMultipleObjects]
    public class CustomAsioAudioSourceEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Create a field in the editor to allow drag-and-drop of the audio file
            GUILayout.Label("Drag an audio file here");

            // Display a field for drag-and-drop
            Rect dropArea = GUILayoutUtility.GetRect(0f, 50f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Put an audio file here");

            foreach (CustomAsioAudioSource customAsioAudioSourceTarget in targets)
            {
                // Check if a file has been dragged to this area
                if (dropArea.Contains(Event.current.mousePosition) && DragAndDrop.paths.Length > 0)
                {
                    string filePath = DragAndDrop.paths[0];

                    if (Event.current.type == EventType.DragUpdated)
                    {
                        if (IsAudioFile(filePath)) DragAndDrop.visualMode = DragAndDropVisualMode.Generic;
                    }

                    if (Event.current.type == EventType.DragExited)
                    {
                        // Check that the file is indeed an audio file
                        if (IsAudioFile(filePath))
                        {
                            string fileName = Path.GetFileName(filePath); // File name
                            string fileExtension = Path.GetExtension(filePath); // File extension
                            customAsioAudioSourceTarget.AudioFilePath = filePath; // Set the audio file path in the CustomAsioAudioSource component
                            EditorUtility.SetDirty((CustomAsioAudioSource)target);
                            Debug.Log("File name: " + fileName);
                            Debug.Log("File extension: " + fileExtension);
                        }
                        else
                        {
                            Debug.LogWarning("This file is not a valid audio file.");
                        }
                    }
                }
            }
            

            serializedObject.ApplyModifiedProperties();

            // Display the usual inspector
            DrawDefaultInspector();
        }

        // Check if the file is an audio file based on its extension
        bool IsAudioFile(string filePath)
        {
            string[] audioExtensions = { ".mp3", ".wav", ".ogg", ".aiff", ".flac" };
            string extension = Path.GetExtension(filePath).ToLower();
            return System.Array.Exists(audioExtensions, ext => ext == extension);
        }
    }
}


