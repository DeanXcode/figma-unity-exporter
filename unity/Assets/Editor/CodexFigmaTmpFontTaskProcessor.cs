using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

public sealed class CodexFigmaTmpFontTaskProcessor : AssetPostprocessor
{
    private static bool isProcessing;

    [MenuItem("Tools/Codex/Figma/Process TMP Font Tasks")]
    public static void ProcessFromMenu()
    {
        ProcessPendingTasks();
    }

    private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        if (isProcessing)
        {
            return;
        }

        foreach (string assetPath in importedAssets)
        {
            if (assetPath.EndsWith("figma_tmp_font_tasks.json", StringComparison.OrdinalIgnoreCase))
            {
                ProcessPendingTasks();
                return;
            }
        }
    }

    private static void ProcessPendingTasks()
    {
        isProcessing = true;
        try
        {
            string[] taskFiles = Directory.GetFiles(Application.dataPath, "figma_tmp_font_tasks.json", SearchOption.AllDirectories);
            foreach (string fullTaskPath in taskFiles)
            {
                ProcessTaskFile(ToAssetPath(fullTaskPath));
            }
        }
        finally
        {
            isProcessing = false;
        }
    }

    private static void ProcessTaskFile(string taskAssetPath)
    {
        string json = File.ReadAllText(taskAssetPath);
        FontGenerationTaskEnvelope envelope = JsonUtility.FromJson<FontGenerationTaskEnvelope>(json);
        List<FontGenerationTask> tasks = envelope != null && envelope.tasks != null
            ? envelope.tasks
            : new List<FontGenerationTask>();
        List<TmpMaterialGenerationTask> materialTasks = envelope != null && envelope.materialTasks != null
            ? envelope.materialTasks
            : new List<TmpMaterialGenerationTask>();

        bool changed = false;
        bool allDone = true;
        foreach (FontGenerationTask task in tasks)
        {
            if (string.IsNullOrWhiteSpace(task.SourceFontAssetPath)
                || string.IsNullOrWhiteSpace(task.TargetFontAssetPath))
            {
                allDone = false;
                continue;
            }

            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(task.TargetFontAssetPath) != null)
            {
                changed = true;
                continue;
            }

            AssetDatabase.ImportAsset(task.SourceFontAssetPath, ImportAssetOptions.ForceUpdate);
            Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(task.SourceFontAssetPath);
            if (sourceFont == null)
            {
                Debug.LogWarning("Codex Figma TMP font source not found: " + task.SourceFontAssetPath);
                allDone = false;
                continue;
            }

            string targetFolder = Path.GetDirectoryName(task.TargetFontAssetPath) ?? "Assets";
            Directory.CreateDirectory(ToFullPath(targetFolder));
            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(sourceFont);
            fontAsset.name = Path.GetFileNameWithoutExtension(task.TargetFontAssetPath);
            AssetDatabase.CreateAsset(fontAsset, task.TargetFontAssetPath);
            EditorUtility.SetDirty(fontAsset);
            changed = true;
            Debug.Log("Codex Figma TMP font created: " + task.TargetFontAssetPath);
        }

        foreach (TmpMaterialGenerationTask task in materialTasks)
        {
            if (string.IsNullOrWhiteSpace(task.FontAssetPath)
                || string.IsNullOrWhiteSpace(task.TargetMaterialAssetPath))
            {
                allDone = false;
                continue;
            }

            if (AssetDatabase.LoadAssetAtPath<Material>(task.TargetMaterialAssetPath) != null)
            {
                changed = true;
                continue;
            }

            TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(task.FontAssetPath);
            if (fontAsset == null)
            {
                Debug.LogWarning("Codex Figma TMP material font asset not found: " + task.FontAssetPath);
                allDone = false;
                continue;
            }

            Material sourceMaterial = fontAsset.material;
            if (sourceMaterial == null)
            {
                Debug.LogWarning("Codex Figma TMP material source missing for: " + task.FontAssetPath);
                allDone = false;
                continue;
            }

            string targetFolder = Path.GetDirectoryName(task.TargetMaterialAssetPath) ?? "Assets";
            Directory.CreateDirectory(ToFullPath(targetFolder));
            Material material = new Material(sourceMaterial);
            material.name = Path.GetFileNameWithoutExtension(task.TargetMaterialAssetPath);
            ApplyTmpMaterialStyle(material, task);
            AssetDatabase.CreateAsset(material, task.TargetMaterialAssetPath);
            EditorUtility.SetDirty(material);
            changed = true;
            Debug.Log("Codex Figma TMP material created: " + task.TargetMaterialAssetPath);
        }

        if (changed)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        if (allDone)
        {
            AssetDatabase.DeleteAsset(taskAssetPath);
            AssetDatabase.Refresh();
        }
    }

    private static void ApplyTmpMaterialStyle(Material material, TmpMaterialGenerationTask task)
    {
        bool hasOutline = task.outlineColor != null && task.outlineColor.a > 0f && task.outlineWidth > 0f;
        bool hasUnderlay = task.underlayColor != null && task.underlayColor.a > 0f
            && (Mathf.Abs(task.underlayOffsetX) > 0.0001f
                || Mathf.Abs(task.underlayOffsetY) > 0.0001f
                || task.underlaySoftness > 0f);

        SetColor(material, "_OutlineColor", task.outlineColor);
        SetFloat(material, "_OutlineWidth", hasOutline ? task.outlineWidth : 0f);
        SetColor(material, "_UnderlayColor", task.underlayColor);
        SetFloat(material, "_UnderlayOffsetX", hasUnderlay ? task.underlayOffsetX : 0f);
        SetFloat(material, "_UnderlayOffsetY", hasUnderlay ? task.underlayOffsetY : 0f);
        SetFloat(material, "_UnderlaySoftness", hasUnderlay ? task.underlaySoftness : 0f);
        SetFloat(material, "_UnderlayDilate", 0f);

        if (hasOutline)
        {
            material.EnableKeyword("OUTLINE_ON");
        }
        else
        {
            material.DisableKeyword("OUTLINE_ON");
        }

        if (hasUnderlay)
        {
            material.EnableKeyword("UNDERLAY_ON");
        }
        else
        {
            material.DisableKeyword("UNDERLAY_ON");
        }

        ShaderUtilities.UpdateShaderRatios(material);
    }

    private static void SetColor(Material material, string propertyName, SerializedColor color)
    {
        if (color == null || !material.HasProperty(propertyName))
        {
            return;
        }

        material.SetColor(propertyName, new Color(color.r, color.g, color.b, color.a));
    }

    private static void SetFloat(Material material, string propertyName, float value)
    {
        if (material.HasProperty(propertyName))
        {
            material.SetFloat(propertyName, value);
        }
    }

    private static string ToAssetPath(string fullPath)
    {
        string normalizedFullPath = fullPath.Replace('\\', '/');
        string normalizedDataPath = Application.dataPath.Replace('\\', '/');
        if (!normalizedFullPath.StartsWith(normalizedDataPath, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return "Assets" + normalizedFullPath.Substring(normalizedDataPath.Length);
    }

    private static string ToFullPath(string assetPath)
    {
        string normalizedAssetPath = assetPath.Replace('\\', '/');
        if (normalizedAssetPath == "Assets")
        {
            return Application.dataPath;
        }

        if (normalizedAssetPath.StartsWith("Assets/", StringComparison.Ordinal))
        {
            return Path.Combine(Application.dataPath, normalizedAssetPath.Substring("Assets/".Length));
        }

        return assetPath;
    }

    private sealed class FontGenerationTask
    {
        public string fontName = string.Empty;
        public string sourceFontAssetPath = string.Empty;
        public string targetFontAssetPath = string.Empty;
        public string targetGuid = string.Empty;

        public string FontName => fontName;
        public string SourceFontAssetPath => sourceFontAssetPath;
        public string TargetFontAssetPath => targetFontAssetPath;
        public string TargetGuid => targetGuid;
    }

    [Serializable]
    private sealed class TmpMaterialGenerationTask
    {
        public string fontAssetPath = string.Empty;
        public string targetMaterialAssetPath = string.Empty;
        public string targetGuid = string.Empty;
        public SerializedColor outlineColor = new SerializedColor();
        public float outlineWidth;
        public SerializedColor underlayColor = new SerializedColor();
        public float underlayOffsetX;
        public float underlayOffsetY;
        public float underlaySoftness;

        public string FontAssetPath => fontAssetPath;
        public string TargetMaterialAssetPath => targetMaterialAssetPath;
    }

    [Serializable]
    private sealed class SerializedColor
    {
        public float r = 1f;
        public float g = 1f;
        public float b = 1f;
        public float a = 1f;
    }

    [Serializable]
    private sealed class FontGenerationTaskEnvelope
    {
        public List<FontGenerationTask> tasks = new List<FontGenerationTask>();
        public List<TmpMaterialGenerationTask> materialTasks = new List<TmpMaterialGenerationTask>();
    }
}
