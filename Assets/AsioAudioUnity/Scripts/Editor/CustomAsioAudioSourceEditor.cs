using System.IO;
using UnityEditor;
using UnityEngine;

namespace AsioAudioUnity
{
    [CustomEditor(typeof(CustomAsioAudioSource))]
    public class CustomAsioAudioSourceEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            CustomAsioAudioSource myScript = (CustomAsioAudioSource)target;

            // Create a field in the editor to allow drag-and-drop of the audio file
            GUILayout.Label("Drag an audio file here");

            // Display a field for drag-and-drop
            Rect dropArea = GUILayoutUtility.GetRect(0f, 50f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Put an audio file here");


            if (dropArea.Contains(Event.current.mousePosition) && DragAndDrop.paths.Length > 0)
            {
                // Check if a file has been dropped in this area
                if (Event.current.type == EventType.DragUpdated)
                {
                    DragAndDrop.AcceptDrag();
                    Event.current.Use();


                    string filePath = DragAndDrop.paths[0];
                    if (IsAudioFile(filePath)) DragAndDrop.visualMode = DragAndDropVisualMode.Generic;
                }

                if (Event.current.type == EventType.DragExited)
                {
                    string filePath = DragAndDrop.paths[0];

                    // Check that the file is indeed an audio file
                    if (IsAudioFile(filePath))
                    {
                        string fileName = Path.GetFileName(filePath); // File name
                        string fileExtension = Path.GetExtension(filePath); // File extension
                        myScript.AudioFilePath = filePath; // Store the full path in the script
                        Debug.Log("File name: " + fileName);
                        Debug.Log("File extension: " + fileExtension);
                    }
                    else
                    {
                        Debug.LogWarning("This file is not a valid audio file.");
                    }
                }
            }

            

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


