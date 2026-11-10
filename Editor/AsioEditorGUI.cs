using UnityEditor;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// GUI helpers shared by the Inspectors of the package and of its plugins (Spat OSC, REAPER OSC).
    /// </summary>
    public static class AsioEditorGUI
    {
        /// <summary>
        /// A card title that folds the card. The arrow sits inside the card, before the title, centered on it
        /// (EditorGUILayout.Foldout puts its arrow in the Inspector's indent area, outside the card's padding). Click anywhere on it to toggle.
        /// Returns the new expanded state.
        /// </summary>
        public static bool CardFoldout(bool expanded, GUIContent title, GUIStyle titleStyle)
        {
            const float arrowSize = 13f, arrowSpace = 16f;
            Rect rect = GUILayoutUtility.GetRect(title, titleStyle, GUILayout.ExpandWidth(true));
            if (!expanded) GUILayout.Space(-titleStyle.margin.bottom); // No gap under the title of a folded card

            Event currentEvent = Event.current;
            if (currentEvent.type == EventType.Repaint)
            {
                Rect arrowRect = new Rect(rect.x, rect.y + Mathf.Round((rect.height - arrowSize) / 2f), arrowSize, arrowSize);
                EditorStyles.foldout.Draw(arrowRect, GUIContent.none, false, false, expanded, false);
            }
            GUI.Label(new Rect(rect.x + arrowSpace, rect.y, rect.width - arrowSpace, rect.height), title, titleStyle); // Also shows the tooltip

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && rect.Contains(currentEvent.mousePosition))
            {
                expanded = !expanded;
                currentEvent.Use();
            }
            return expanded;
        }
    }
}
