using System.IO;
using UnityEditor;
using UnityEngine;

public static class FigmaExportReportEditorUtility
{
    public static void DrawReportButtons(Component component)
    {
        string reportAssetPath = FindReportAssetPath(component);
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(reportAssetPath)))
            {
                if (GUILayout.Button("Open Export Report"))
                {
                    OpenReport(reportAssetPath);
                }

                if (GUILayout.Button("Reveal Export Folder"))
                {
                    RevealFolder(reportAssetPath);
                }
            }
        }

        if (string.IsNullOrEmpty(reportAssetPath))
        {
            EditorGUILayout.HelpBox("Export report was not found next to the prefab asset.", MessageType.Info);
        }
    }

    private static string FindReportAssetPath(Component component)
    {
        if (component == null)
        {
            return string.Empty;
        }

        string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(component.gameObject);
        if (string.IsNullOrEmpty(prefabPath))
        {
            prefabPath = AssetDatabase.GetAssetPath(component.gameObject);
        }

        if (string.IsNullOrEmpty(prefabPath))
        {
            return string.Empty;
        }

        string folder = Path.GetDirectoryName(prefabPath);
        if (string.IsNullOrEmpty(folder))
        {
            return string.Empty;
        }

        string reportPath = (folder + "/figma_export_report.json").Replace('\\', '/');
        return File.Exists(reportPath) ? reportPath : string.Empty;
    }

    private static void OpenReport(string reportAssetPath)
    {
        Object report = AssetDatabase.LoadAssetAtPath<Object>(reportAssetPath);
        if (report != null)
        {
            AssetDatabase.OpenAsset(report);
            return;
        }

        RevealFolder(reportAssetPath);
    }

    private static void RevealFolder(string reportAssetPath)
    {
        if (string.IsNullOrEmpty(reportAssetPath))
        {
            return;
        }

        string fullPath = Path.GetFullPath(reportAssetPath);
        EditorUtility.RevealInFinder(fullPath);
    }
}
