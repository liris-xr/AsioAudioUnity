using System.IO;
using UnityEditor;
using UnityEngine;

namespace AsioAudioUnity
{
    [CustomEditor(typeof(CustomAsioAudioSource)), CanEditMultipleObjects]
    public class CustomAsioAudioSourceEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUI.BeginChangeCheck();

            // Create a field in the editor to allow drag-and-drop of the audio file
            GUILayout.Label("Drag an audio file here");

            // Display a field for drag-and-drop
            Rect dropArea = GUILayoutUtility.GetRect(0f, 50f, GUILayout.ExpandWidth(true));
            GUIStyle dropStyle = new GUIStyle();
            dropStyle.alignment = TextAnchor.MiddleCenter;
            dropStyle.normal.textColor = Color.white;
            dropStyle.clipping = TextClipping.Clip;
            dropStyle.fontSize = 15;

            GUI.Box(dropArea, ((CustomAsioAudioSource)target).AudioFilePath != null ? Path.GetFileName(((CustomAsioAudioSource)target).AudioFilePath) : "Put an audio file here", dropStyle);

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
                            Undo.RegisterCompleteObjectUndo(customAsioAudioSourceTarget, "Set Audio File Path");
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

            if (EditorGUI.EndChangeCheck())
            {
                Debug.Log("Change check");
                Undo.RegisterCompleteObjectUndo(targets, "Set Audio File Path");
                serializedObject.ApplyModifiedProperties();
            }

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.Space(10f);
            EditorGUILayout.BeginHorizontal();

            bool playButton = GUILayout.Button("Play", GUILayout.Height(25f));
            bool pauseButton = GUILayout.Button("Pause", GUILayout.Height(25f));
            bool stopButton = GUILayout.Button("Stop", GUILayout.Height(25f));
            
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(10f);

            foreach (CustomAsioAudioSource customAsioAudioSourceTarget in targets)
            {
                if (playButton) customAsioAudioSourceTarget.Play();
                if (pauseButton) customAsioAudioSourceTarget.Pause();
                if (stopButton) customAsioAudioSourceTarget.Stop();
            }

            // Display the usual inspector
            DrawDefaultInspector();
            

            if(EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
            }
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


