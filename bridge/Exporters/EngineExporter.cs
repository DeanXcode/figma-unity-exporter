namespace FigmaUnityBridge;

internal static partial class Program
{
    private const string UnityExportTarget = "unity";

    private static IEngineExporter ResolveEngineExporter(string? exportTarget)
    {
        string normalizedTarget = NormalizeExportTarget(exportTarget);
        return normalizedTarget switch
        {
            UnityExportTarget => new UnityPrefabExporter(),
            _ => throw new InvalidOperationException("Unsupported export target: " + normalizedTarget),
        };
    }

    private static string NormalizeExportTarget(string? exportTarget)
    {
        string value = string.IsNullOrWhiteSpace(exportTarget) ? UnityExportTarget : exportTarget.Trim().ToLowerInvariant();
        return value switch
        {
            "" => UnityExportTarget,
            "unity-prefab" => UnityExportTarget,
            "prefab" => UnityExportTarget,
            _ => value,
        };
    }

    private interface IEngineExporter
    {
        string Target { get; }
        EngineExportArtifact Export(EngineExportContext context);
    }

    private sealed class EngineExportContext
    {
        public string ProjectRoot { get; init; } = string.Empty;
        public string RootName { get; init; } = string.Empty;
        public string ExportFolderAssetPath { get; init; } = string.Empty;
        public UiModel Model { get; init; } = new();
        public Dictionary<string, string> SpriteGuids { get; init; } = new(StringComparer.Ordinal);
        public FontExportContext FontContext { get; init; } = null!;
    }

    private sealed class EngineExportArtifact
    {
        public string MainAssetPath { get; init; } = string.Empty;
        public string MainAssetKind { get; init; } = string.Empty;
        public string PrefabAssetPath { get; init; } = string.Empty;
    }
}
