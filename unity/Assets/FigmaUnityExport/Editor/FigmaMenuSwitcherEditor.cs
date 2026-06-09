using Codex.FigmaUnity;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(FigmaMenuSwitcher))]
public sealed class FigmaMenuSwitcherEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        FigmaMenuSwitcher switcher = (FigmaMenuSwitcher)target;
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Figma Preview", EditorStyles.boldLabel);

        DrawValidation(switcher);
        FigmaExportReportEditorUtility.DrawReportButtons(switcher);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Preview Default"))
            {
                Preview(switcher, switcher.defaultKey);
            }

            if (GUILayout.Button("Rebind Buttons"))
            {
                switcher.BindButtons();
                Debug.Log("Figma menu buttons rebound for this instance.", switcher);
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Validate"))
            {
                Debug.Log(BuildValidationMessage(switcher), switcher);
            }
        }

        if (switcher.items == null || switcher.items.Count == 0)
        {
            EditorGUILayout.HelpBox("No tab items were exported. Use menu_ with direct tab_ children in Figma.", MessageType.Warning);
            return;
        }

        foreach (FigmaMenuItem item in switcher.items)
        {
            if (item == null)
            {
                continue;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(string.IsNullOrEmpty(item.key) ? "<empty>" : item.key, GUILayout.MinWidth(80));
                if (GUILayout.Button("Preview", GUILayout.Width(82)))
                {
                    Preview(switcher, item.key);
                }
            }
        }
    }

    private static void DrawValidation(FigmaMenuSwitcher switcher)
    {
        string message = BuildValidationMessage(switcher);
        if (!string.IsNullOrEmpty(message))
        {
            EditorGUILayout.HelpBox(message, MessageType.Warning);
        }
    }

    private static string BuildValidationMessage(FigmaMenuSwitcher switcher)
    {
        if (switcher.items == null || switcher.items.Count == 0)
        {
            return "No menu items.";
        }

        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        foreach (FigmaMenuItem item in switcher.items)
        {
            if (item == null)
            {
                builder.AppendLine("Null menu item.");
                continue;
            }

            string key = string.IsNullOrEmpty(item.key) ? "<empty>" : item.key;
            if (item.button == null)
            {
                builder.AppendLine(key + ": missing Button.");
            }

            if (item.normalLayer == null)
            {
                builder.AppendLine(key + ": missing state_normal.");
            }

            if (item.selectedLayer == null)
            {
                builder.AppendLine(key + ": missing state_selected.");
            }
        }

        return builder.ToString().Trim();
    }

    private static void Preview(FigmaMenuSwitcher switcher, string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        Undo.RecordObject(switcher, "Preview Figma Menu");
        if (switcher.items != null)
        {
            foreach (FigmaMenuItem item in switcher.items)
            {
                if (item == null)
                {
                    continue;
                }

                Record(item.normalLayer);
                Record(item.selectedLayer);
                if (item.button != null)
                {
                    Undo.RecordObject(item.button, "Preview Figma Menu");
                }
            }
        }

        switcher.SetSelected(key);
        EditorUtility.SetDirty(switcher);
    }

    private static void Record(GameObject gameObject)
    {
        if (gameObject != null)
        {
            Undo.RecordObject(gameObject, "Preview Figma Menu");
        }
    }
}
