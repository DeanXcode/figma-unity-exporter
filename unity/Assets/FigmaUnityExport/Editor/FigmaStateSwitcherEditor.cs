using Codex.FigmaUnity;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(FigmaStateSwitcher))]
public sealed class FigmaStateSwitcherEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        FigmaStateSwitcher switcher = (FigmaStateSwitcher)target;
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Figma Preview", EditorStyles.boldLabel);

        DrawValidation(switcher);
        FigmaExportReportEditorUtility.DrawReportButtons(switcher);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Reset Default"))
            {
                ResetDefault(switcher);
            }

            if (GUILayout.Button("Validate"))
            {
                Debug.Log(BuildValidationMessage(switcher), switcher);
            }
        }

        if (switcher.states == null || switcher.states.Count == 0)
        {
            EditorGUILayout.HelpBox("No state layers were exported. Use state_ child layers in Figma.", MessageType.Info);
            return;
        }

        foreach (FigmaStateLayer state in switcher.states)
        {
            if (state == null)
            {
                continue;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                string key = string.IsNullOrEmpty(state.key) ? "<empty>" : state.key;
                bool active = state.layer != null && state.layer.activeSelf;
                bool nextActive = EditorGUILayout.ToggleLeft(key, active, GUILayout.MinWidth(120));
                if (nextActive != active)
                {
                    SetVisible(state, nextActive);
                }

                if (GUILayout.Button("Only", GUILayout.Width(58)))
                {
                    SetOnly(switcher, state.key);
                }
            }
        }
    }

    private static void DrawValidation(FigmaStateSwitcher switcher)
    {
        string message = BuildValidationMessage(switcher);
        if (!string.IsNullOrEmpty(message))
        {
            EditorGUILayout.HelpBox(message, MessageType.Warning);
        }
    }

    private static string BuildValidationMessage(FigmaStateSwitcher switcher)
    {
        if (switcher.states == null || switcher.states.Count == 0)
        {
            return "No state layers.";
        }

        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        foreach (FigmaStateLayer state in switcher.states)
        {
            if (state == null)
            {
                builder.AppendLine("Null state.");
                continue;
            }

            string key = string.IsNullOrEmpty(state.key) ? "<empty>" : state.key;
            if (state.layer == null)
            {
                builder.AppendLine(key + ": missing layer.");
            }
        }

        return builder.ToString().Trim();
    }

    private static void ResetDefault(FigmaStateSwitcher switcher)
    {
        RecordStates(switcher);
        switcher.ResetToDefault();
        EditorUtility.SetDirty(switcher);
    }

    private static void SetOnly(FigmaStateSwitcher switcher, string key)
    {
        RecordStates(switcher);
        switcher.SetOnly(key);
        EditorUtility.SetDirty(switcher);
    }

    private static void SetVisible(FigmaStateLayer state, bool visible)
    {
        if (state.layer == null)
        {
            return;
        }

        Undo.RecordObject(state.layer, "Preview Figma State");
        state.layer.SetActive(visible);
        EditorUtility.SetDirty(state.layer);
    }

    private static void RecordStates(FigmaStateSwitcher switcher)
    {
        if (switcher.states == null)
        {
            return;
        }

        foreach (FigmaStateLayer state in switcher.states)
        {
            if (state != null && state.layer != null)
            {
                Undo.RecordObject(state.layer, "Preview Figma State");
            }
        }
    }
}
